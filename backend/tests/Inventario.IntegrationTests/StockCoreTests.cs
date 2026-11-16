using Inventario.Api.Domain.Identity;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

// Núcleo de stock H0 contra SQL Server real: idempotencia, rollback, concurrencia e invariantes.
// Los saldos se preparan siempre mediante operaciones del núcleo, nunca insertando filas sueltas.
public sealed class StockCoreTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private InventoryDbContext ApplicationDatabase()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(fixture.Settings).Build();
        return new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(DatabaseConfiguration.ApplicationConnection(configuration), sql => sql.EnableRetryOnFailure())
            .Options);
    }

    private async Task<Guid> ActorAsync()
    {
        var email = await fixture.CreateUserAsync("Operario");
        using var scope = fixture.BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }

    private async Task<(Guid Actor, Article Article, Location Location)> SeedAsync(
        bool allowsFractional = false, bool tracksLot = false)
    {
        var actor = await ActorAsync();
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var article = new Article("STK-" + suffix, "Artículo de stock", "UD", allowsFractional, tracksLot);
        var location = new Location("L-" + suffix, "A01", "E01", "N1", LocationKind.Storage);
        await using var database = ApplicationDatabase();
        database.Articles.Add(article);
        database.Add(location);
        await database.SaveChangesAsync();
        return (actor, article, location);
    }

    private static StockCommandContext Context(string type, Guid? document = null, Guid? operation = null) =>
        new(operation ?? Guid.NewGuid(), new StockDocumentRef(type, document ?? Guid.NewGuid()),
            DateTimeOffset.UtcNow.AddMinutes(-1), "Prueba del núcleo H0",
            SupportNotApplicableReason: "Fixture de integración");

    private async Task<StockOperationOutcome> RunAsync(Guid actor,
        Func<StockCore, Task<StockOperationOutcome>> action, Func<CancellationToken, Task>? beforeCommit = null)
    {
        await using var database = ApplicationDatabase();
        var core = new StockCore(database, new TestActor(actor)) { BeforeCommit = beforeCommit };
        return await action(core);
    }

    private async Task<StockBalance?> BalanceAsync(Guid article, Guid location)
    {
        await using var database = ApplicationDatabase();
        return await database.Set<StockBalance>().AsNoTracking()
            .SingleOrDefaultAsync(balance => balance.ArticleId == article && balance.LocationId == location);
    }

    private async Task AssertJournalMatchesBalanceAsync(Guid article, Guid location)
    {
        await using var database = ApplicationDatabase();
        var balance = await database.Set<StockBalance>().AsNoTracking()
            .SingleAsync(value => value.ArticleId == article && value.LocationId == location);
        var entries = database.Set<StockEntry>().AsNoTracking()
            .Where(entry => entry.ArticleId == article && entry.LocationId == location);
        Assert.Equal(balance.Physical, await entries.SumAsync(entry => entry.PhysicalDelta));
        Assert.Equal(balance.Reserved, await entries.SumAsync(entry => entry.ReservedDelta));
        Assert.InRange(balance.Reserved, 0m, balance.Physical);
    }

    [Fact]
    public async Task Receive_reserve_and_mixed_shipment_follow_the_contract_example()
    {
        var (actor, article, location) = await SeedAsync();
        StockAllocation Line(decimal quantity) => new(article.Id, location.Id, null, quantity);

        await RunAsync(actor, core => core.ReceiveAsync(Context("Receipt"), [Line(10)]));
        var ownOrder = Guid.NewGuid();
        await RunAsync(actor, core => core.ReserveAsync(Context("Reservation", ownOrder), [Line(4)]));
        await RunAsync(actor, core => core.ReserveAsync(Context("Reservation"), [Line(3)]));

        // F=10, R=7 (propia 4, ajena 3), D=3. Servir 8 se rechaza: tomaría reserva ajena.
        var rejected = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ShipAsync(Context("Shipment"), [new(article.Id, location.Id, null, 8, 4)])));
        Assert.Equal("InsufficientStock", rejected.Code);
        Assert.Equal(409, rejected.Status);

        // Servir 6 consume 4 de reserva propia y 2 disponibles: quedan F=4, R=3.
        var shipped = await RunAsync(actor, core =>
            core.ShipAsync(Context("Shipment"), [new(article.Id, location.Id, null, 6, 4)]));
        var balance = (await BalanceAsync(article.Id, location.Id))!;
        Assert.Equal(4m, balance.Physical);
        Assert.Equal(3m, balance.Reserved);
        Assert.Equal(1m, balance.Available);

        await using (var database = ApplicationDatabase())
        {
            var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
                .SingleAsync(value => value.Id == shipped.MovementId);
            Assert.Equal(MovementKind.Shipment, movement.Kind);
            Assert.Equal(actor, movement.ActorId);
            Assert.Equal(6m, movement.Entries.Sum(entry => entry.Quantity));
            Assert.Contains(movement.Entries, entry => entry.Effect == StockEffect.ShipReserved && entry.Quantity == 4);
            Assert.Contains(movement.Entries, entry => entry.Effect == StockEffect.ShipAvailable && entry.Quantity == 2);
            var lot = await database.Set<Lot>().AsNoTracking().SingleAsync(value => value.ArticleId == article.Id);
            Assert.Equal(Lot.UntrackedCode, lot.Code);
            Assert.False(database.Database.HasPendingModelChanges());
        }
        await AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Retry_with_the_same_key_returns_the_stored_result_without_duplicating_stock()
    {
        var (actor, article, location) = await SeedAsync();
        var context = Context("Receipt");
        var first = await RunAsync(actor, core =>
            core.ReceiveAsync(context, [new(article.Id, location.Id, null, 6m)]));
        // 6 y 6.000 son el mismo cuerpo normalizado.
        var retry = await RunAsync(actor, core =>
            core.ReceiveAsync(context, [new(article.Id, location.Id, null, 6.000m)]));

        Assert.False(first.Replayed);
        Assert.True(retry.Replayed);
        Assert.Equal(first.MovementId, retry.MovementId);
        Assert.Equal(6m, (await BalanceAsync(article.Id, location.Id))!.Physical);

        var conflict = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReceiveAsync(context, [new(article.Id, location.Id, null, 7m)])));
        Assert.Equal("IdempotencyConflict", conflict.Code);
        Assert.Equal(context.OperationId, conflict.OperationId);

        var otherActor = await ActorAsync();
        var foreign = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(otherActor, core =>
            core.ReceiveAsync(context, [new(article.Id, location.Id, null, 6m)])));
        Assert.Equal("IdempotencyConflict", foreign.Code);

        // Otra clave sobre el mismo hecho documental tampoco duplica.
        var duplicate = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReceiveAsync(context with { OperationId = Guid.NewGuid() }, [new(article.Id, location.Id, null, 6m)])));
        Assert.Equal("DocumentAlreadyConfirmed", duplicate.Code);

        await using var database = ApplicationDatabase();
        Assert.Equal(1, await database.Set<StockMovement>().CountAsync(value => value.SourceDocumentId == context.Source.Id));
        await AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_apply_the_operation_once()
    {
        var (actor, article, location) = await SeedAsync();
        var context = Context("Receipt");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => RunAsync(actor, core =>
            core.ReceiveAsync(context, [new(article.Id, location.Id, null, 5m)])))));

        Assert.Single(results.Select(result => result.MovementId).Distinct());
        Assert.Single(results, result => !result.Replayed);
        Assert.Equal(5m, (await BalanceAsync(article.Id, location.Id))!.Physical);
        await AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Failure_after_writing_and_before_commit_leaves_no_partial_effects()
    {
        var (actor, article, location) = await SeedAsync(tracksLot: true);
        var context = Context("Receipt");
        StockAllocation[] lines = [new(article.Id, location.Id, "l-001", 3m)];

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(actor,
            core => core.ReceiveAsync(context, lines),
            _ => throw new InvalidOperationException("Fallo simulado antes del commit.")));

        await using (var database = ApplicationDatabase())
        {
            Assert.False(await database.Set<Lot>().AnyAsync(lot => lot.ArticleId == article.Id));
            Assert.False(await database.Set<StockBalance>().AnyAsync(balance => balance.ArticleId == article.Id));
            Assert.False(await database.Set<StockMovement>().AnyAsync(movement => movement.SourceDocumentId == context.Source.Id));
            Assert.False(await database.Set<OperationResult>().AnyAsync(result => result.OperationId == context.OperationId));
        }

        // Al no existir resultado, repetir la misma clave aplica la operación una sola vez.
        var retry = await RunAsync(actor, core => core.ReceiveAsync(context, lines));
        Assert.False(retry.Replayed);
        await using var check = ApplicationDatabase();
        var lot = await check.Set<Lot>().AsNoTracking().SingleAsync(value => value.ArticleId == article.Id);
        Assert.Equal("L-001", lot.Code);
        Assert.Equal(3m, (await BalanceAsync(article.Id, location.Id))!.Physical);
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_the_physical_stock()
    {
        var (actor, article, location) = await SeedAsync();
        await RunAsync(actor, core => core.ReceiveAsync(Context("Receipt"), [new(article.Id, location.Id, null, 10m)]));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try
            {
                await RunAsync(actor, core => core.ReserveAsync(Context("Reservation"),
                    [new(article.Id, location.Id, null, 3m)]));
                return "ok";
            }
            catch (StockOperationException error) { return error.Code; }
        })));

        Assert.Equal(3, attempts.Count(result => result == "ok"));
        Assert.Equal(3, attempts.Count(result => result == "InsufficientStock"));
        var balance = (await BalanceAsync(article.Id, location.Id))!;
        Assert.Equal(10m, balance.Physical);
        Assert.Equal(9m, balance.Reserved);
        await AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Concurrent_shipments_and_reservations_keep_physical_non_negative()
    {
        var (actor, article, location) = await SeedAsync();
        await RunAsync(actor, core => core.ReceiveAsync(Context("Receipt"), [new(article.Id, location.Id, null, 10m)]));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            try
            {
                if (index % 3 == 0)
                    await RunAsync(actor, core => core.ReserveAsync(Context("Reservation"),
                        [new(article.Id, location.Id, null, 2m)]));
                else
                    await RunAsync(actor, core => core.ShipAsync(Context("Shipment"),
                        [new(article.Id, location.Id, null, 2m, 0m)]));
                return "ok";
            }
            catch (StockOperationException error) { return error.Code; }
        })));

        // 10 unidades admiten exactamente cinco operaciones de 2 entre salidas y reservas.
        Assert.Equal(5, attempts.Count(result => result == "ok"));
        Assert.All(attempts.Where(result => result != "ok"), result => Assert.Equal("InsufficientStock", result));
        var balance = (await BalanceAsync(article.Id, location.Id))!;
        Assert.Equal(0m, balance.Available);
        Assert.True(balance.Physical >= 0);
        await AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Rules_on_lots_units_activity_and_releases_are_enforced()
    {
        var (actor, tracked, location) = await SeedAsync(tracksLot: true);
        var missingLot = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReceiveAsync(Context("Receipt"), [new(tracked.Id, location.Id, null, 1m)])));
        Assert.Equal("ValidationFailed", missingLot.Code);
        var reservedLotCode = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReceiveAsync(Context("Receipt"), [new(tracked.Id, location.Id, "sin-lote", 1m)])));
        Assert.Equal("ValidationFailed", reservedLotCode.Code);

        var fraction = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReceiveAsync(Context("Receipt"), [new(tracked.Id, location.Id, "L1", 1.5m)])));
        Assert.Equal(422, fraction.Status);

        var emptyBucket = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReserveAsync(Context("Reservation"), [new(tracked.Id, location.Id, "L1", 1m)])));
        Assert.Equal("InsufficientStock", emptyBucket.Code);

        await RunAsync(actor, core => core.ReceiveAsync(Context("Receipt"), [new(tracked.Id, location.Id, "L1", 4m)]));
        await RunAsync(actor, core => core.ReserveAsync(Context("Reservation"), [new(tracked.Id, location.Id, "L1", 2m)]));
        var overRelease = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReleaseAsync(Context("Release"), [new(tracked.Id, location.Id, "L1", 3m)])));
        Assert.Equal("ReservedStockConflict", overRelease.Code);

        // Desactivado: no admite recepciones ni reservas, pero sí liberar y servir lo comprometido.
        await using (var database = ApplicationDatabase())
        {
            var stored = await database.Articles.SingleAsync(value => value.Id == tracked.Id);
            database.Entry(stored).Property(value => value.IsActive).CurrentValue = false;
            await database.SaveChangesAsync();
        }
        var inactive = await Assert.ThrowsAsync<StockOperationException>(() => RunAsync(actor, core =>
            core.ReserveAsync(Context("Reservation"), [new(tracked.Id, location.Id, "L1", 1m)])));
        Assert.Equal("ArticleInactive", inactive.Code);
        await RunAsync(actor, core => core.ReleaseAsync(Context("Release"), [new(tracked.Id, location.Id, "L1", 1m)]));
        await RunAsync(actor, core => core.ShipAsync(Context("Shipment"), [new(tracked.Id, location.Id, "L1", 1m, 1m)]));

        var balance = (await BalanceAsync(tracked.Id, location.Id))!;
        Assert.Equal(3m, balance.Physical);
        Assert.Equal(0m, balance.Reserved);
        await AssertJournalMatchesBalanceAsync(tracked.Id, location.Id);
    }

    [Fact]
    public async Task Journal_is_append_only_and_balances_are_guarded_by_sql_constraints()
    {
        var (actor, article, location) = await SeedAsync();
        await RunAsync(actor, core => core.ReceiveAsync(Context("Receipt"), [new(article.Id, location.Id, null, 2m)]));

        await using var database = ApplicationDatabase();
        foreach (var statement in new[]
                 {
                     "UPDATE StockEntries SET Quantity = Quantity WHERE ArticleId = {0}",
                     "DELETE FROM StockEntries WHERE ArticleId = {0}",
                     "UPDATE StockMovements SET Reason = Reason WHERE ActorId = {1}",
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
        await AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    private sealed class TestActor(Guid userId) : ICurrentActor
    {
        public Task<Guid> GetRequiredActorIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(userId);
    }
}
