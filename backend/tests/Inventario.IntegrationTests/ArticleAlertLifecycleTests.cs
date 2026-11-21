using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class ArticleAlertLifecycleTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Policy_and_receipt_observe_the_committed_article_state_in_both_orders(bool deactivateFirst)
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory, "GestorMaestros", "ResponsableAlmacen");
        var stock = new StockTestHarness(fixture, factory.Services);
        var (_, principal) = await stock.ActorAsync();
        var (article, location) = await stock.SeedAsync();
        var path = $"/api/v1/articles/{article.Id:D}";
        var policyPath = path + "/stock-policy";
        var version = (await api.Client.GetAsync(policyPath)).Headers.ETag!.ToString();
        var receipt = StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(article.Id, location.Id, null, 2m)]);
        async Task Deactivate()
        {
            var etag = (await api.Client.GetAsync(path)).Headers.ETag!.ToString();
            var body = MasterDataArticleTests.Body(article.FamilyId, article.Sku, unit: article.BaseUnit,
                fractions: article.AllowsFractional, lot: article.TracksLot, active: false);
            Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path, body, version: etag)).StatusCode);
        }
        if (deactivateFirst) await Deactivate();
        var updated = await api.WriteAsync(HttpMethod.Put, policyPath, new { minimumAvailable = "5" }, version: version);
        Assert.Equal(deactivateFirst ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK, updated.StatusCode);
        if (deactivateFirst)
            Assert.Equal("ArticleInactive", (await Assert.ThrowsAsync<InventoryProblemException>(() => stock.RunAsync(principal, receipt))).Code);
        else
        {
            await stock.RunAsync(principal, receipt);
            await Deactivate();
        }
        var alerts = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-alerts?articleId={article.Id:D}&state=All");
        Assert.Equal(deactivateFirst ? 0 : 1, alerts.GetProperty("totalCount").GetInt32());
        if (!deactivateFirst) Assert.Equal("Resolved", alerts.GetProperty("items")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Failed_deactivation_rolls_back_article_and_alert_together()
    {
        var failure = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, failure);
        using var api = await MasterDataApi.LoginAsync(fixture, factory, "GestorMaestros", "ResponsableAlmacen");
        var family = await api.FamilyAsync();
        var sku = "ROLL-" + Guid.NewGuid().ToString("N");
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, sku));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var path = $"/api/v1/articles/{id:D}";
        var policy = await api.Client.GetAsync(path + "/stock-policy");
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path + "/stock-policy", new { minimumAvailable = "5" }, version: policy.Headers.ETag!.ToString())).StatusCode);
        failure.Arm();
        var failed = await api.WriteAsync(HttpMethod.Put, path, MasterDataArticleTests.Body(family, sku, active: false), version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var article = await api.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.True(article.GetProperty("isActive").GetBoolean());
        var alerts = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-alerts?articleId={id:D}");
        Assert.Equal(1, alerts.GetProperty("totalCount").GetInt32());
        Assert.Equal("Active", alerts.GetProperty("items")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Deactivation_resolves_and_reactivation_creates_a_new_episode()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory, "GestorMaestros", "ResponsableAlmacen");
        var family = await api.FamilyAsync();
        var sku = "CYCLE-" + Guid.NewGuid().ToString("N");
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, sku));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var path = $"/api/v1/articles/{id:D}";
        var policy = await api.Client.GetAsync(path + "/stock-policy");
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path + "/stock-policy",
            new { minimumAvailable = "5.000" }, version: policy.Headers.ETag!.ToString())).StatusCode);
        var disabled = await api.WriteAsync(HttpMethod.Put, path, MasterDataArticleTests.Body(family, sku, active: false), version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var inactive = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-alerts?articleId={id:D}&state=All");
        Assert.Equal("Resolved", inactive.GetProperty("items")[0].GetProperty("state").GetString());
        var reactivated = await api.WriteAsync(HttpMethod.Put, path, MasterDataArticleTests.Body(family, sku), version: disabled.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        var active = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-alerts?articleId={id:D}&state=All");
        Assert.Equal(2, active.GetProperty("totalCount").GetInt32());
        Assert.Single(active.GetProperty("items").EnumerateArray(), x => x.GetProperty("state").GetString() == "Active");
        Assert.Single(active.GetProperty("items").EnumerateArray(), x => x.GetProperty("state").GetString() == "Resolved");
    }
}
