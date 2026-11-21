using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

public sealed class StockAlertTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Twenty_new_lots_and_a_later_update_are_aggregated_once()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync(tracksLot: true);
        var (_, otherLocation) = await stock.SeedAsync();
        await SetMinimum(article.Id, 30m);
        var lines = Enumerable.Range(1, 20).Select(i => new StockAllocation(article.Id,
            i % 2 == 0 ? location.Id : otherLocation.Id, $"LOT-{i:D2}", 1m)).ToArray();
        await stock.RunAsync(principal, StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), lines));
        await stock.RunAsync(principal, StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [lines[0] with { Quantity = 3m }]));
        await using var db = fixture.ApplicationDatabase();
        var alert = await db.Set<StockAlert>().SingleAsync(x => x.ArticleId == article.Id);
        Assert.Equal((23m, 0m, 23m, 7m), (alert.PhysicalSnapshot, alert.ReservedSnapshot, alert.AvailableSnapshot, alert.SuggestedQuantity));
    }

    private async Task SetMinimum(Guid id, decimal value)
    {
        await using var db = fixture.ApplicationDatabase();
        var policy = await db.Set<StockPolicy>().SingleAsync(x => x.ArticleId == id);
        db.Entry(policy).Property(x => x.MinimumAvailable).CurrentValue = value;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Concurrent_reservations_update_a_single_alert_and_replay_does_not_duplicate()
    {
        using var factory = fixture.Factory();
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await SetMinimum(article.Id, 9m);
        await stock.RunAsync(principal, StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article.Id, location.Id, null, 10m)]));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var commands = keys.Select(_ => StockCore.Prepare(StockOperation.Reserve, StockTestHarness.Context("reservation"), [new(article.Id, location.Id, null, 2m)])).ToArray();
        var tasks = commands.Select((command, i) => Task.Run(async () => { await start.Task; return await stock.RunAsync(principal, keys[i], command); })).ToArray();
        start.SetResult();
        await Task.WhenAll(tasks);
        await stock.RunAsync(principal, keys[0], commands[0]);
        await using var db = fixture.ApplicationDatabase();
        var alert = await db.Set<StockAlert>().SingleAsync(x => x.ArticleId == article.Id && x.State == "Active");
        Assert.Equal(6m, alert.AvailableSnapshot);
        Assert.Equal(3m, alert.SuggestedQuantity);
        Assert.Equal(4m, (await stock.BalanceAsync(article.Id, location.Id))!.Reserved);
        Assert.Equal(1, await db.Set<StockAlert>().CountAsync(x => x.ArticleId == article.Id));
    }

    [Fact]
    public async Task Failure_before_commit_rolls_back_alert_and_stock_then_same_key_succeeds()
    {
        var interceptor = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, interceptor);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        await SetMinimum(article.Id, 10m);
        var key = Guid.NewGuid();
        var command = StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article.Id, location.Id, null, 3m)]);
        interceptor.Arm();
        await Assert.ThrowsAsync<InvalidOperationException>(() => stock.RunAsync(principal, key, command));
        await using (var db = fixture.ApplicationDatabase())
        {
            Assert.False(await db.Set<StockAlert>().AnyAsync(x => x.ArticleId == article.Id));
            Assert.False(await db.Set<StockBalance>().AnyAsync(x => x.ArticleId == article.Id));
            Assert.False(await db.OperationResults.AnyAsync(x => x.OperationId == key));
        }
        await stock.RunAsync(principal, key, command);
        await using var check = fixture.ApplicationDatabase();
        var alert = await check.Set<StockAlert>().SingleAsync(x => x.ArticleId == article.Id);
        Assert.Equal(7m, alert.SuggestedQuantity);
        Assert.Equal(3m, alert.PhysicalSnapshot);
    }
}
