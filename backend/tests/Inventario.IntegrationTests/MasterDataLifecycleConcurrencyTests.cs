using System.Net;
using System.Net.Http.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataLifecycleConcurrencyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Definition_and_first_stock_reference_serialize_in_both_orders(bool referenceFirst)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var factory = new ApiFactory(fixture.Settings, "Development", services =>
            services.ConfigureDbContext<InventoryDbContext>((_, options) => options.AddInterceptors(barrier)));
        using var manager = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, actor) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var path = $"/api/v1/articles/{article.Id:D}";
        using var current = await manager.Client.GetAsync(path);
        var command = StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"),
            [new(article.Id, location.Id, "L-NEW", 1.125m)]);
        // La definición original no admite fracciones ni lote; la nueva debe leerse bajo el bloqueo.
        // Si la referencia gana, utiliza una asignación válida para la definición original.
        if (referenceFirst) command = StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"),
            [new(article.Id, location.Id, null, 1m)]);
        barrier.Predicate = referenceFirst
            ? entity => entity is StockEntry entry && entry.ArticleId == article.Id
            : entity => entity is Article value && value.Id == article.Id;
        Task<HttpResponseMessage> Edit() => manager.WriteAsync(HttpMethod.Put, path,
            MasterDataArticleTests.Body(article.FamilyId, article.Sku, unit: "KG", fractions: true, lot: true),
            version: current.Headers.ETag!.ToString());
        Task<StockOperationOutcome> referencing;
        Task<HttpResponseMessage> editing;
        try
        {
            if (referenceFirst)
            {
                referencing = stock.RunAsync(actor, command);
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                editing = Edit();
            }
            else
            {
                editing = Edit();
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                referencing = stock.RunAsync(actor, command);
            }
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }
        await referencing;
        using var result = await editing;
        Assert.Equal(referenceFirst ? HttpStatusCode.PreconditionFailed : HttpStatusCode.OK, result.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        var persisted = await db.Articles.SingleAsync(value => value.Id == article.Id);
        Assert.True(persisted.IsDefinitionLocked);
        Assert.Equal(referenceFirst ? "UD" : "KG", persisted.BaseUnit);
        Assert.Equal(!referenceFirst, persisted.AllowsFractional);
        Assert.Equal(!referenceFirst, persisted.TracksLot);
        var entry = await db.Set<StockEntry>().SingleAsync(value => value.ArticleId == article.Id);
        Assert.Equal(persisted.BaseUnit, entry.BaseUnitSnapshot);
        Assert.Equal(referenceFirst ? 1m : 1.125m, entry.Quantity);
        Assert.Equal(referenceFirst ? 0 : 1, await db.Set<MasterDataAudit>().CountAsync(value => value.EntityId == article.Id));
        await stock.AssertJournalMatchesBalanceAsync(article.Id, location.Id);
        if (referenceFirst)
        {
            using var latest = await manager.Client.GetAsync(path);
            using var rejected = await manager.WriteAsync(HttpMethod.Put, path,
                MasterDataArticleTests.Body(article.FamilyId, article.Sku, unit: "KG", fractions: true, lot: true),
                version: latest.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Receipt_and_lifecycle_commands_serialize_in_both_orders(bool receiptFirst, bool delete)
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var factory = new ApiFactory(fixture.Settings, "Development", services =>
            services.ConfigureDbContext<InventoryDbContext>((_, options) => options.AddInterceptors(barrier)));
        using var manager = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, actor) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        StockCommand Receipt() => StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"),
            [new(article.Id, location.Id, null, 1m)]);
        // Existing history keeps the article version stable during a subsequent receipt.
        if (!delete) await stock.RunAsync(actor, Receipt());

        var path = $"/api/v1/articles/{article.Id:D}";
        using var current = await manager.Client.GetAsync(path);
        var etag = current.Headers.ETag!.ToString();
        barrier.Predicate = receiptFirst
            ? entity => entity is StockEntry entry && entry.ArticleId == article.Id
            : entity => entity is MasterDataAudit audit && audit.EntityId == article.Id;

        async Task<string?> ReceiveAsync()
        {
            try { await stock.RunAsync(actor, Receipt()); return null; }
            catch (InventoryProblemException problem) { return problem.Code; }
        }
        Task<HttpResponseMessage> ChangeAsync() => manager.WriteAsync(
            delete ? HttpMethod.Delete : HttpMethod.Put, path,
            delete ? new { } : MasterDataArticleTests.Body(article.FamilyId, article.Sku, lot: false, active: false), version: etag);

        Task<string?> receiving;
        Task<HttpResponseMessage> changing;
        try
        {
            if (receiptFirst)
            {
                receiving = ReceiveAsync();
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                changing = ChangeAsync();
            }
            else
            {
                changing = ChangeAsync();
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                receiving = ReceiveAsync();
            }
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { barrier.Release.TrySetResult(); }

        Assert.Equal(receiptFirst ? null : delete ? "ValidationFailed" : "ArticleInactive", await receiving);
        using var changed = await changing;
        Assert.Equal(delete ? receiptFirst ? HttpStatusCode.PreconditionFailed : HttpStatusCode.NoContent : HttpStatusCode.OK,
            changed.StatusCode);

        await using var database = fixture.ApplicationDatabase();
        var persisted = await database.Articles.SingleOrDefaultAsync(value => value.Id == article.Id);
        if (delete && !receiptFirst)
            Assert.Null(persisted);
        else
            Assert.Equal(delete, persisted!.IsActive);
        Assert.Equal((delete ? 0 : 1) + (receiptFirst ? 1 : 0),
            await database.Set<StockEntry>().CountAsync(entry => entry.ArticleId == article.Id));
    }
}
