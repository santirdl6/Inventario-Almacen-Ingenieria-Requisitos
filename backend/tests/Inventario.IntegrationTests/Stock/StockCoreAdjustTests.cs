using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// HU-04 T004 (C-02; research D-13, INT-HU04-02). Operación Adjust del núcleo: diferencia con signo por
// bucket; AdjustUp crea lote y saldo si faltan; AdjustDown rechaza bajo el reservado y sobre el
// físico; el artículo desactivado se admite. Contraparte solo con CounterpartyNotApplicableReason.
public sealed class StockCoreAdjustTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static StockCommandContext Context(Guid? count = null) =>
        new(new StockDocumentRef("cycle-count", count ?? Guid.NewGuid()), DateTimeOffset.UtcNow.AddMinutes(-1),
            "Ajuste por conteo cíclico", SupportReference: "CC-000000000000",
            CounterpartyNotApplicableReason: "Ajuste de inventario: sin contraparte externa");

    private static StockCommand Adjust(params AdjustmentAllocation[] lines) => StockCore.PrepareAdjustment(Context(), lines);

    private static AdjustmentAllocation Line(Guid article, Guid location, string? lot, decimal difference) =>
        new(article, location, lot, difference);

    private static StockCommand Receive(Guid article, Guid location, decimal quantity, string? lot = null) =>
        StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article, location, lot, quantity)]);

    private static async Task<InventoryProblemException> RejectedAsync(Func<Task> action) =>
        await Assert.ThrowsAsync<InventoryProblemException>(action);

    [Fact]
    public async Task Adjust_up_creates_the_lot_and_balance_and_adjust_down_reduces_the_physical()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (actor, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync(tracksLot: true);
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 10m, "L1"));

        // Un solo movimiento con una subida (lote nuevo, hallazgo) y una bajada.
        var outcome = await stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, "l-nuevo", 3m),
            new(article.Id, location.Id, "L1", -2m)));

        await using var database = fixture.ApplicationDatabase();
        var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
            .SingleAsync(value => value.Id == outcome.MovementId);
        Assert.Equal(MovementKind.Adjustment, movement.Kind);
        Assert.Equal("cycle-count", movement.SourceDocumentType);
        Assert.Equal(actor, movement.ActorId);
        Assert.Equal("CC-000000000000", movement.SupportReference);
        Assert.Null(movement.CounterpartyReference);
        Assert.Equal("Ajuste de inventario: sin contraparte externa", movement.CounterpartyNotApplicableReason);
        var lots = await database.Set<Lot>().AsNoTracking().Where(value => value.ArticleId == article.Id)
            .ToDictionaryAsync(value => value.Code, value => value.Id);
        Assert.Contains(movement.Entries, entry => entry.Effect == StockEffect.AdjustUp && entry.Quantity == 3m
            && entry.LotId == lots["L-NUEVO"]);
        Assert.Contains(movement.Entries, entry => entry.Effect == StockEffect.AdjustDown && entry.Quantity == 2m
            && entry.LotId == lots["L1"]);
        var balances = await database.Set<StockBalance>().AsNoTracking().Where(value => value.ArticleId == article.Id)
            .ToDictionaryAsync(value => value.LotId, value => value.Physical);
        Assert.Equal(8m, balances[lots["L1"]]);
        Assert.Equal(3m, balances[lots["L-NUEVO"]]);
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Adjust_down_is_rejected_below_the_reserved_and_above_the_physical_without_changes()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 5m));
        await stock.RunAsync(principal, StockCore.Prepare(StockOperation.Reserve, StockTestHarness.Context("reservation"),
            [new(article.Id, location.Id, null, 3m)]));

        // F=5, R=3: bajar 3 dejaría F=2 < R.
        var reserved = await RejectedAsync(() => stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, null, -3m))));
        Assert.Equal((409, "ReservedStockConflict"), (reserved.Status, reserved.Code));
        var insufficient = await RejectedAsync(() => stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, null, -6m))));
        Assert.Equal((409, "InsufficientStock"), (insufficient.Status, insufficient.Code));
        // Bajar en un bucket sin saldo tampoco es posible.
        var (other, _) = await stock.SeedAsync();
        Assert.Equal("InsufficientStock",
            (await RejectedAsync(() => stock.RunAsync(principal, Adjust(Line(other.Id, location.Id, null, -1m))))).Code);

        var balance = (await stock.BalanceAsync(article.Id, location.Id))!;
        Assert.Equal((5m, 3m), (balance.Physical, balance.Reserved));
        // Hasta el reservado sí: F=3, R=3.
        await stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, null, -2m)));
        balance = (await stock.BalanceAsync(article.Id, location.Id))!;
        Assert.Equal((3m, 3m), (balance.Physical, balance.Reserved));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task An_inactive_article_is_adjusted_up_and_down()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 4m));
        await using (var database = fixture.ApplicationDatabase())
            await database.Database.ExecuteSqlInterpolatedAsync($"UPDATE Articles SET IsActive = 0 WHERE Id = {article.Id}");

        await stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, null, -1m)));
        await stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, null, 2m)));

        Assert.Equal(5m, (await stock.BalanceAsync(article.Id, location.Id))!.Physical);
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
    }

    [Fact]
    public async Task Preparation_rejects_zero_differences_fractions_and_a_counterparty_reference()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await stock.RunAsync(principal, Receive(article.Id, location.Id, 4m));

        Assert.Equal(422, Assert.Throws<InventoryProblemException>(() => Adjust(Line(article.Id, location.Id, null, 0m))).Status);
        Assert.Equal(422, Assert.Throws<InventoryProblemException>(() => Adjust(Line(article.Id, location.Id, null, -0.0001m))).Status);
        // Unidades enteras: la fracción la rechaza el núcleo tras leer el artículo.
        Assert.Equal(422, (await RejectedAsync(() => stock.RunAsync(principal, Adjust(Line(article.Id, location.Id, null, -0.5m))))).Status);
        // Regla XOR (PR #8): un ajuste no lleva referencia de contraparte.
        var both = Context() with { CounterpartyReference = "PROVEEDOR-1" };
        Assert.Equal(422, (await RejectedAsync(() => stock.RunAsync(principal,
            StockCore.PrepareAdjustment(both, [new(article.Id, location.Id, null, 1m)])))).Status);
        // La intención con signo forma parte del cuerpo normalizado: +1 y −1 no son la misma.
        var same = Context();
        Assert.NotEqual(StockCore.PrepareAdjustment(same, [new(article.Id, location.Id, null, 1m)]).NormalizedBody.ToString(),
            StockCore.PrepareAdjustment(same, [new(article.Id, location.Id, null, -1m)]).NormalizedBody.ToString());
    }
}
