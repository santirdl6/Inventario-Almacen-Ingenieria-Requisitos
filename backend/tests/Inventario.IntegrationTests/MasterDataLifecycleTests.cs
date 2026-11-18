using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataLifecycleTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_marker_alone_does_not_prevent_deletion_and_evidence_and_replay_survive()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, "DELETE-" + Guid.NewGuid().ToString("N")));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        // Marca técnica histórica, no prueba de retirada de una referencia real inexistente en H1.
        await using (var db = fixture.ApplicationDatabase()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Articles SET IsDefinitionLocked=1 WHERE Id={id}");
        var path = $"/api/v1/articles/{id:D}"; var current = await api.Client.GetAsync(path);
        Assert.Equal((HttpStatusCode)428, (await api.WriteAsync(HttpMethod.Delete, path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await api.WriteAsync(HttpMethod.Delete, path, new { }, version: created.Headers.ETag!.ToString())).StatusCode);
        var key = Guid.NewGuid();
        var deleted = await api.WriteAsync(HttpMethod.Delete, path, new { }, key, current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("", await deleted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.WriteAsync(HttpMethod.Delete, path, new { }, key)).StatusCode);
        var outcome = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/operations/{key:D}");
        Assert.Equal(204, outcome.GetProperty("status").GetInt32());
        await using var check = fixture.ApplicationDatabase();
        Assert.False(await check.Set<StockPolicy>().AnyAsync(value => value.ArticleId == id));
        var audits = await check.Set<MasterDataAudit>().Where(value => value.EntityId == id).ToListAsync();
        Assert.Equal(2, audits.Count); var audit = Assert.Single(audits, value => value.Action == MasterDataAction.Delete);
        Assert.NotNull(audit.Before); Assert.Null(audit.After);
        Assert.Equal(2, await check.OperationResults.CountAsync(value => value.SourceDocumentId == id));
    }

    [Fact]
    public async Task Inactive_article_keeps_history_access_but_missing_journal_is_not_an_empty_page()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var sku = "HISTORY-" + Guid.NewGuid().ToString("N");
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, sku));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var changed = await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{id:D}", MasterDataArticleTests.Body(family, sku, active: false), version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var response = await api.Client.GetAsync($"/api/v1/articles/{id:D}/movements");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("DependencyUnavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/api/v1/articles/{Guid.NewGuid():D}/movements")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Client.GetAsync($"/api/v1/articles/{id:D}/movements?from=2026-11-17T12:00:00")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Client.GetAsync($"/api/v1/articles/{id:D}/movements?from=2026-11-18T00:00:00Z&to=2026-11-17T00:00:00Z")).StatusCode);
    }
}
