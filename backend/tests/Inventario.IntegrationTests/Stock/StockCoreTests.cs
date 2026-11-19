using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// Núcleo de stock H0 contra SQL Server real, sobre el protocolo común de A: idempotencia y
// resultado en el único OperationResult, InventoryLocks, rollback, concurrencia e invariantes.
public sealed class StockCoreTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static StockCommand Receive(Guid article, Guid location, decimal quantity, string? lot = null,
        StockCommandContext? context = null) =>
        StockCore.Prepare(StockOperation.Receive, context ?? StockTestHarness.Context("receipt"), [new(article, location, lot, quantity)]);

    private static StockCommand Reserve(Guid article, Guid location, decimal quantity, string? lot = null) =>
        StockCore.Prepare(StockOperation.Reserve, StockTestHarness.Context("reservation"), [new(article, location, lot, quantity)]);

    private static StockCommand Release(Guid article, Guid location, decimal quantity, string? lot = null) =>
        StockCore.Prepare(StockOperation.Release, StockTestHarness.Context("release"), [new(article, location, lot, quantity)]);

    private static StockCommand Ship(Guid article, Guid location, decimal quantity, decimal own, string? lot = null) =>
        StockCore.PrepareShipment(StockTestHarness.Context("shipment"), [new(article, location, lot, quantity, own)]);

    private static async Task<InventoryProblemException> RejectedAsync(Func<Task> action) =>
        await Assert.ThrowsAsync<InventoryProblemException>(action);

    [Fact]
    public async Task Receive_reserve_and_mixed_shipment_follow_the_contract_example()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (actor, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();

        await stock.RunAsync(principal, Receive(article.Id, location.Id, 10));
        await stock.RunAsync(principal, Reserve(article.Id, location.Id, 4));
        await stock.RunAsync(principal, Reserve(article.Id, location.Id, 3));

        // F=10, R=7 (propia 4, ajena 3), D=3. Servir 8 se rechaza: tomaría reserva ajena.
        var rejected = await RejectedAsync(() => stock.RunAsync(principal, Ship(article.Id, location.Id, 8, 4)));
        Assert.Equal((409, "InsufficientStock"), (rejected.Status, rejected.Code));

        // Servir 6 consume 4 de reserva propia y 2 disponibles: quedan F=4, R=3.
        var shipped = await stock.RunAsync(principal, Ship(article.Id, location.Id, 6, 4));
        var balance = (await stock.BalanceAsync(article.Id, location.Id))!;
        Assert.Equal((4m, 3m, 1m), (balance.Physical, balance.Reserved, balance.Available));

        await using (var database = fixture.ApplicationDatabase())
        {
            var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
                .SingleAsync(value => value.Id == shipped.MovementId);
            Assert.Equal(MovementKind.Shipment, movement.Kind);
            Assert.Equal("shipment", movement.SourceDocumentType);
            Assert.Equal(actor, movement.ActorId);
            Assert.Equal(shipped.OperationId, movement.OperationId);
            Assert.Equal(6m, movement.Entries.Sum(entry => entry.Quantity));
            Assert.Contains(movement.Entries, entry => entry.Effect == StockEffect.ShipReserved && entry.Quantity == 4);
            Assert.Contains(movement.Entries, entry => entry.Effect == StockEffect.ShipAvailable && entry.Quantity == 2);
            // El resultado de la operación está en la única tabla común de A.
            Assert.True(await database.OperationResults.AnyAsync(value => value.OperationId == shipped.OperationId));
            var lot = await database.Set<Lot>().AsNoTracking().SingleAsync(value => value.ArticleId == article.Id);
            Assert.Equal(Lot.UntrackedCode, lot.Code);
            Assert.True((await database.Articles.AsNoTracking().SingleAsync(value => value.Id == article.Id)).IsDefinitionLocked);
            Assert.False(database.Database.HasPendingModelChanges());
        }
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Retry_with_the_same_key_returns_the_stored_result_without_duplicating_stock()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var key = Guid.NewGuid();
        var context = StockTestHarness.Context("receipt");
        var document = context.Source.Id;

        var first = await stock.RunAsync(principal, key, Receive(article.Id, location.Id, 6m, context: context));
        // 6 y 6.000 son el mismo cuerpo normalizado: el ejecutor devuelve el resultado guardado.
        var retry = await stock.RunAsync(principal, key, Receive(article.Id, location.Id, 6.000m, context: context));
        Assert.Equal(first, retry);
        Assert.Equal(6m, (await stock.BalanceAsync(article.Id, location.Id))!.Physical);

        var conflict = await RejectedAsync(() => stock.RunAsync(principal, key, Receive(article.Id, location.Id, 7m, context: context)));
        Assert.Equal("IdempotencyConflict", conflict.Code);

        var (_, other) = await stock.ActorAsync();
        var foreign = await RejectedAsync(() => stock.RunAsync(other, key, Receive(article.Id, location.Id, 6m, context: context)));
        Assert.Equal("IdempotencyConflict", foreign.Code);

        // Otra clave sobre el mismo hecho documental tampoco duplica.
        var duplicate = await RejectedAsync(() => stock.RunAsync(principal, Receive(article.Id, location.Id, 6m, context: context)));
        Assert.Equal("DocumentAlreadyConfirmed", duplicate.Code);

        await using var database = fixture.ApplicationDatabase();
        Assert.Equal(1, await database.Set<StockMovement>().CountAsync(value => value.SourceDocumentId == document));
        Assert.Equal(1, await database.OperationResults.CountAsync(value => value.SourceDocumentId == document));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_apply_the_operation_once()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var key = Guid.NewGuid();
        var command = Receive(article.Id, location.Id, 5m);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => stock.RunAsync(principal, key, command))));

        Assert.Single(results.Select(result => result.MovementId).Distinct());
        Assert.Equal(5m, (await stock.BalanceAsync(article.Id, location.Id))!.Physical);
        await using (var database = fixture.ApplicationDatabase())
            Assert.Equal(1, await database.Set<StockMovement>().CountAsync(value => value.OperationId == key));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Failure_after_writing_and_before_commit_leaves_no_partial_effects()
    {
        var interceptor = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, interceptor);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync(tracksLot: true);
        var key = Guid.NewGuid();
        var command = Receive(article.Id, location.Id, 3m, "l-001");

        // El ejecutor ya escribió diario, saldo, lote, primera referencia y resultado; falla el commit.
        interceptor.Arm();
        await Assert.ThrowsAsync<InvalidOperationException>(() => stock.RunAsync(principal, key, command));

        await using (var database = fixture.ApplicationDatabase())
        {
            Assert.False(await database.Set<Lot>().AnyAsync(lot => lot.ArticleId == article.Id));
            Assert.False(await database.Set<StockBalance>().AnyAsync(balance => balance.ArticleId == article.Id));
            Assert.False(await database.Set<StockMovement>().AnyAsync(movement => movement.SourceDocumentId == command.SourceId));
            Assert.False(await database.OperationResults.AnyAsync(result => result.OperationId == key));
            // La primera referencia también se revierte con el resto de la operación.
            Assert.False((await database.Articles.AsNoTracking().SingleAsync(value => value.Id == article.Id)).IsDefinitionLocked);
        }

        // Al no existir resultado, repetir la misma clave aplica la operación una sola vez.
        await stock.RunAsync(principal, key, command);
        await using var check = fixture.ApplicationDatabase();
        var lot = await check.Set<Lot>().AsNoTracking().SingleAsync(value => value.ArticleId == article.Id);
        Assert.Equal("L-001", lot.Code);
        Assert.Equal(3m, (await stock.BalanceAsync(article.Id, location.Id))!.Physical);
        Assert.Equal(1, await check.Set<StockMovement>().CountAsync(value => value.OperationId == key));
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_the_physical_stock()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 10m));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try
            {
                await stock.RunAsync(principal, Reserve(article.Id, location.Id, 3m));
                return "ok";
            }
            catch (InventoryProblemException error) { return error.Code; }
        })));

        Assert.Equal(3, attempts.Count(result => result == "ok"));
        Assert.Equal(3, attempts.Count(result => result == "InsufficientStock"));
        var balance = (await stock.BalanceAsync(article.Id, location.Id))!;
        Assert.Equal((10m, 9m), (balance.Physical, balance.Reserved));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Concurrent_shipments_and_reservations_keep_physical_non_negative()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 10m));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            try
            {
                await stock.RunAsync(principal, index % 3 == 0
                    ? Reserve(article.Id, location.Id, 2m)
                    : Ship(article.Id, location.Id, 2m, 0m));
                return "ok";
            }
            catch (InventoryProblemException error) { return error.Code; }
        })));

        // 10 unidades admiten exactamente cinco operaciones de 2 entre salidas y reservas.
        Assert.Equal(5, attempts.Count(result => result == "ok"));
        Assert.All(attempts.Where(result => result != "ok"), result => Assert.Equal("InsufficientStock", result));
        var balance = (await stock.BalanceAsync(article.Id, location.Id))!;
        Assert.Equal(0m, balance.Available);
        Assert.True(balance.Physical >= 0);
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Rules_on_lots_units_activity_and_releases_are_enforced()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (tracked, location) = await stock.SeedAsync(tracksLot: true);

        Assert.Equal("ValidationFailed", (await RejectedAsync(() => stock.RunAsync(principal, Receive(tracked.Id, location.Id, 1m)))).Code);
        Assert.Equal("ValidationFailed", (await RejectedAsync(() => stock.RunAsync(principal, Receive(tracked.Id, location.Id, 1m, "sin-lote")))).Code);
        Assert.Equal(422, (await RejectedAsync(() => stock.RunAsync(principal, Receive(tracked.Id, location.Id, 1.5m, "L1")))).Status);
        Assert.Equal("InsufficientStock", (await RejectedAsync(() => stock.RunAsync(principal, Reserve(tracked.Id, location.Id, 1m, "L1")))).Code);
        // Tipo de documento no canónico: no podría bloquearse como document:{type}:{id}.
        Assert.Equal(422, Assert.Throws<InventoryProblemException>(() => StockCore.Prepare(StockOperation.Receive,
            StockTestHarness.Context("Receipt Doc"), [new(tracked.Id, location.Id, "L1", 1m)])).Status);

        await stock.RunAsync(principal, Receive(tracked.Id, location.Id, 4m, "L1"));
        await stock.RunAsync(principal, Reserve(tracked.Id, location.Id, 2m, "L1"));
        Assert.Equal("ReservedStockConflict", (await RejectedAsync(() => stock.RunAsync(principal, Release(tracked.Id, location.Id, 3m, "L1")))).Code);

        // Desactivado: no admite recepciones ni reservas, pero sí liberar y servir lo comprometido.
        await using (var database = fixture.ApplicationDatabase())
            await database.Database.ExecuteSqlInterpolatedAsync($"UPDATE Articles SET IsActive = 0 WHERE Id = {tracked.Id}");
        Assert.Equal("ArticleInactive", (await RejectedAsync(() => stock.RunAsync(principal, Reserve(tracked.Id, location.Id, 1m, "L1")))).Code);
        await stock.RunAsync(principal, Release(tracked.Id, location.Id, 1m, "L1"));
        await stock.RunAsync(principal, Ship(tracked.Id, location.Id, 1m, 1m, "L1"));

        var balance = (await stock.BalanceAsync(tracked.Id, location.Id))!;
        Assert.Equal((3m, 0m), (balance.Physical, balance.Reserved));
        await stock.AssertJournalMatchesBalanceAsync(tracked.Id, location.Id);

        // El ejecutor de A autoriza el permiso de la operación: un gestor de maestros no recibe stock.
        var (_, manager) = await stock.ActorAsync("GestorMaestros");
        Assert.Equal(403, (await RejectedAsync(() => stock.RunAsync(manager, Receive(tracked.Id, location.Id, 1m, "L1")))).Status);
    }

    [Fact]
    public async Task Journal_is_append_only_and_balances_are_guarded_by_sql_constraints()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (actor, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 2m));

        await using var database = fixture.ApplicationDatabase();
        foreach (var statement in new[]
                 {
                     "UPDATE StockEntries SET Quantity = Quantity WHERE ArticleId = {0}",
                     "DELETE FROM StockEntries WHERE ArticleId = {0}",
                     "UPDATE StockMovements SET Reason = Reason WHERE ActorId = {1}",
                     "DELETE FROM StockMovements WHERE ActorId = {1}",
                     "DELETE FROM StockBalances WHERE ArticleId = {0}",
                     "DELETE FROM OperationResults WHERE ActorId = {1}"
                 })
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() =>
                database.Database.ExecuteSqlRawAsync(statement, article.Id, actor));
            Assert.Equal(229, denied.Number);
        }

        var check = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(
            "UPDATE StockBalances SET Reserved = Physical + 1 WHERE ArticleId = {0}", article.Id));
        Assert.Equal(547, check.Number);
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }
}
