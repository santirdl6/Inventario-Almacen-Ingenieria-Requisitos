using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class StockPolicyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Failed_update_rolls_back_policy_and_alert_and_does_not_consume_the_intention()
    {
        var failure = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, failure);
        var stock = new StockTestHarness(fixture, factory.Services);
        var (article, _) = await stock.SeedAsync();
        using var api = await MasterDataApi.LoginAsync(fixture, factory, "ResponsableAlmacen");
        var path = $"/api/v1/articles/{article.Id:D}/stock-policy";
        var version = (await api.Client.GetAsync(path)).Headers.ETag!.ToString();
        var key = Guid.NewGuid();
        var body = new { minimumAvailable = "5" };
        failure.Arm();
        Assert.Equal(HttpStatusCode.InternalServerError, (await api.WriteAsync(HttpMethod.Put, path, body, key, version)).StatusCode);
        await using (var db = fixture.ApplicationDatabase())
        {
            Assert.Equal(0m, (await db.Set<StockPolicy>().SingleAsync(x => x.ArticleId == article.Id)).MinimumAvailable);
            Assert.False(await db.Set<StockAlert>().AnyAsync(x => x.ArticleId == article.Id));
            Assert.False(await db.OperationResults.AnyAsync(x => x.OperationId == key));
        }
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path, body, key, version)).StatusCode);
    }

    [Fact]
    public async Task Policy_requires_version_validates_integer_and_replays_original_response()
    {
        using var factory = fixture.Factory();
        using var api = await MasterDataApi.LoginAsync(fixture, factory, "GestorMaestros", "ResponsableAlmacen");
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, "POL-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var path = $"/api/v1/articles/{id:D}/stock-policy";
        var initial = await api.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var version = initial.Headers.ETag!.ToString();
        var fractional = await api.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "1.500" }, version: version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fractional.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "-1" }, version: version)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await api.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "5" })).StatusCode);
        using var noPermission = await MasterDataApi.LoginAsync(fixture, factory, "Operario");
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermission.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "5" }, version: version)).StatusCode);
        using (var noToken = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(new { minimumAvailable = "5" }) })
        {
            noToken.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); noToken.Headers.Add("If-Match", version);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.SendAsync(noToken)).StatusCode);
        }
        var key = Guid.NewGuid();
        var updated = await api.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "5.000" }, key, version);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var replay = await api.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "5.000" }, key, version);
        Assert.Equal(await updated.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(updated.Headers.ETag, replay.Headers.ETag);
        var stale = await api.WriteAsync(HttpMethod.Put, path, new { minimumAvailable = "6.000" }, version: version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(5m, (await db.Set<StockPolicy>().SingleAsync(x => x.ArticleId == id)).MinimumAvailable);
        Assert.Single(await db.Set<MasterDataAudit>().Where(x => x.OperationId == key && x.EntityId == id).ToListAsync());
        Assert.Equal(1, await db.Set<StockAlert>().CountAsync(x => x.ArticleId == id && x.State == "Active"));
    }
}
