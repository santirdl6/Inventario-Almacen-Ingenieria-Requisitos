using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Stock;

// Integración del núcleo de stock con los controles de Article de A (HU-08): cobertura de
// referencias, primera referencia (IsDefinitionLocked) y borrado con historial.
public sealed class StockArticleIntegrationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static async Task<(Guid Id, string ETag)> CreateArticleAsync(MasterDataApi api, string prefix, bool lot = false)
    {
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles",
            MasterDataArticleTests.Body(family, prefix + "-" + Guid.NewGuid().ToString("N"), lot: lot));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        return (id, created.Headers.ETag!.ToString());
    }

    private async Task<Guid> LocationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var location = new Inventario.Api.Domain.Locations.Location("L-" + suffix, "A" + suffix[..8], "E01", "N1",
            Inventario.Api.Domain.Locations.LocationKind.Storage);
        await using var database = fixture.ApplicationDatabase();
        database.Add(location);
        await database.SaveChangesAsync();
        return location.Id;
    }

    private static StockCommand Receive(Guid article, Guid location, decimal quantity) =>
        StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article, location, null, quantity)]);

    [Fact]
    public void Stock_is_the_only_article_consumer_and_its_contributor_covers_every_foreign_key()
    {
        using var factory = fixture.Factory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(new HashSet<Type> { typeof(Lot), typeof(StockBalance), typeof(StockEntry) },
            ArticleUsageCoverageValidator.RequiredTypes(database.Model));
        // Ni la cobertura de uso ni la de primera referencia quedan como 503 DependencyUnavailable.
        scope.ServiceProvider.GetRequiredService<ArticleUsageCoverageValidator>().EnsureCovered();
        scope.ServiceProvider.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
    }

    [Fact]
    public async Task An_article_with_stock_cannot_be_deleted_and_reports_history()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (id, _) = await CreateArticleAsync(api, "STOCKED");
        var location = await LocationAsync();
        await stock.RunAsync(principal, Receive(id, location, 2m));

        using (var scope = factory.Services.CreateScope())
            Assert.Equal(new ArticleUsage(true, true),
                await scope.ServiceProvider.GetRequiredService<IArticleUsageReader>().ReadAsync(id));

        var path = $"/api/v1/articles/{id:D}";
        var current = await api.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var deleted = await api.WriteAsync(HttpMethod.Delete, path, new { }, version: current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("HistoryPreventsDeletion", (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        await using var check = fixture.ApplicationDatabase();
        Assert.True(await check.Articles.AnyAsync(value => value.Id == id));
        Assert.Equal(2m, await check.Set<StockBalance>().Where(value => value.ArticleId == id).SumAsync(value => value.Physical));
    }

    [Fact]
    public async Task Deleting_an_unreferenced_article_still_works_with_balances_protected_from_delete()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();

        // Otro artículo con saldo: la tabla tiene filas y el DENY DELETE está en vigor.
        var (stocked, _) = await CreateArticleAsync(api, "NEIGHBOUR");
        await stock.RunAsync(principal, Receive(stocked, await LocationAsync(), 1m));
        await using (var database = fixture.ApplicationDatabase())
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM StockBalances WHERE ArticleId = {stocked}"));
            Assert.Equal(229, denied.Number);
        }

        var (id, etag) = await CreateArticleAsync(api, "UNUSED");
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(new ArticleUsage(false, false),
                await scope.ServiceProvider.GetRequiredService<IArticleUsageReader>().ReadAsync(id));
        var path = $"/api/v1/articles/{id:D}";
        var deleted = await api.WriteAsync(HttpMethod.Delete, path, new { }, version: etag);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task The_first_movement_locks_the_article_definition_under_the_same_lock_and_transaction()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (id, _) = await CreateArticleAsync(api, "FIRSTREF");
        var path = $"/api/v1/articles/{id:D}";
        var initial = await api.Client.GetAsync(path);
        var before = await initial.Content.ReadFromJsonAsync<JsonElement>();
        var staleETag = initial.Headers.ETag!.ToString();
        Assert.False(before.GetProperty("isDefinitionLocked").GetBoolean());

        await stock.RunAsync(principal, Receive(id, await LocationAsync(), 1m));

        var current = await api.Client.GetAsync(path);
        var after = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(after.GetProperty("isDefinitionLocked").GetBoolean());
        var currentETag = current.Headers.ETag!.ToString();
        Assert.NotEqual(staleETag, currentETag);
        var family = after.GetProperty("familyId").GetGuid();
        var body = MasterDataArticleTests.Body(family, after.GetProperty("sku").GetString()!, unit: "KG", lot: false);

        // La primera referencia cambió la versión: el ETag leído antes de recibir está obsoleto.
        var stale = await api.WriteAsync(HttpMethod.Put, path, body, Guid.NewGuid(), staleETag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("VersionConflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Con el ETag vigente, el cambio de definición se rechaza por la primera referencia.
        var changed = await api.WriteAsync(HttpMethod.Put, path, body, Guid.NewGuid(), currentETag);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("ArticleDefinitionLocked", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
