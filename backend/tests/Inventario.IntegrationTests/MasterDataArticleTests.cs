using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataArticleTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    internal static object Body(Guid familyId, string sku, string unit = "UD", string name = "Artículo de validación",
        bool fractions = false, bool lot = true, bool active = true) =>
        new { sku, name, familyId, baseUnit = unit, allowsFractional = fractions, tracksLot = lot, isActive = active };

    [Fact]
    public async Task Creation_is_atomic_with_zero_policy_and_replay_does_not_duplicate_anything()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var sku = "ART-" + Guid.NewGuid().ToString("N"); var key = Guid.NewGuid();
        var body = Body(family, $" {sku.ToLowerInvariant()} ");
        var response = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", body, key);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var article = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        var id = article.GetProperty("id").GetGuid();
        Assert.Equal(sku.ToUpperInvariant(), article.GetProperty("sku").GetString());
        Assert.Equal(family, article.GetProperty("familyId").GetGuid());
        Assert.Equal($"/api/v1/articles/{id:D}", response.Headers.Location!.ToString());
        Assert.False(article.TryGetProperty("family", out _));
        var replay = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", body, key);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(response.Headers.ETag, replay.Headers.ETag);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(0m, (await db.Set<StockPolicy>().SingleAsync(value => value.ArticleId == id)).MinimumAvailable);
        Assert.Single(await db.Set<MasterDataAudit>().Where(value => value.EntityId == id).ToListAsync());
        Assert.Single(await db.OperationResults.Where(value => value.OperationId == key).ToListAsync());
        var context = await api.Client.GetAsync($"/api/v1/articles/{id:D}/context");
        Assert.Null(context.Headers.ETag);
        var extra = await context.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(response.Headers.ETag!.ToString(), extra.GetProperty("articleVersion").GetString());
        Assert.Equal("0.000", extra.GetProperty("minimumAvailable").GetString());
        Assert.True(extra.GetProperty("capabilities").GetProperty("canEditDefinition").GetBoolean());
    }

    [Fact]
    public async Task An_inactive_current_family_is_preserved_but_cannot_be_a_new_assignment()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var sku = "REF-" + Guid.NewGuid().ToString("N");
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, sku));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var familyDetail = await api.Client.GetAsync($"/api/v1/families/{family:D}");
        var oldFamily = await familyDetail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, $"/api/v1/families/{family:D}",
            new { code = oldFamily.GetProperty("code").GetString(), name = "Nombre cambiado", isActive = false },
            version: familyDetail.Headers.ETag!.ToString())).StatusCode);
        var unchanged = await api.Client.GetAsync($"/api/v1/articles/{id:D}");
        Assert.Equal(created.Headers.ETag, unchanged.Headers.ETag);
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{id:D}",
            Body(family, sku, name: "Editado sin reasignar"), version: unchanged.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles",
            Body(family, "DENY-" + Guid.NewGuid().ToString("N")))).StatusCode);
        var detail = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{id:D}");
        Assert.True(detail.GetProperty("isActive").GetBoolean());
        Assert.Equal(family, detail.GetProperty("familyId").GetGuid());
        var context = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{id:D}/context");
        Assert.False(context.GetProperty("family").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Definition_is_editable_before_use_and_permanently_locked_by_its_recorded_marker()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var sku = "DEF-" + Guid.NewGuid().ToString("N");
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, sku));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{id:D}",
            Body(family, sku, "KG", fractions: true, lot: false), version: created.Headers.ETag!.ToString())).StatusCode);
        // Marca histórica preparada en SQL: no simula ni acredita una integración de stock inexistente.
        await using (var db = fixture.ApplicationDatabase())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Articles SET IsDefinitionLocked = 1 WHERE Id = {id}");
        var current = await api.Client.GetAsync($"/api/v1/articles/{id:D}");
        var rejected = await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{id:D}", Body(family, sku), version: current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("ArticleDefinitionLocked", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{id:D}",
            Body(family, sku, "KG", "Solo descripción", true, false), version: current.Headers.ETag!.ToString())).StatusCode);
    }

    [Fact]
    public async Task Lists_search_literal_text_with_family_state_and_stable_pages()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var other = await api.FamilyAsync();
        var prefix = "LIST-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var first = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, prefix + "-A", name: "Tuerca%_literal"));
        var second = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, prefix + "-B"));
        await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(other, prefix + "-C"));
        var id = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, $"/api/v1/articles/{id:D}",
            Body(family, prefix + "-B", active: false), version: second.Headers.ETag!.ToString())).StatusCode);
        var active = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles?q={prefix.ToLowerInvariant()}&familyId={family}");
        Assert.Equal(1, active.GetProperty("totalCount").GetInt32());
        Assert.Equal(prefix + "-A", active.GetProperty("items")[0].GetProperty("sku").GetString());
        var page = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles?q={prefix}&familyId={family}&state=all&page=2&pageSize=1");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        var inactive = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles?q={prefix}&familyId={family}&state=inactive");
        Assert.Equal(id, inactive.GetProperty("items")[0].GetProperty("id").GetGuid());
        var literal = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles?q=%25_literal&familyId={family}");
        Assert.Equal(1, literal.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/api/v1/articles?familyId=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Client.GetAsync("/api/v1/articles?state=unknown")).StatusCode);
    }

    [Fact]
    public async Task Sku_prefix_is_literal_case_insensitive_and_does_not_replace_the_existing_contains_search()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var prefix = "PREFIX%_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, prefix + "-A"));
        await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, "X-" + prefix));
        var escaped = Uri.EscapeDataString(prefix.ToLowerInvariant());
        var selected = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles?skuPrefix={escaped}&familyId={family}");
        Assert.Equal(1, selected.GetProperty("totalCount").GetInt32());
        Assert.Equal(prefix + "-A", selected.GetProperty("items")[0].GetProperty("sku").GetString());
        var contains = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles?q={escaped}&familyId={family}");
        Assert.Equal(2, contains.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.Client.GetAsync("/api/v1/articles?skuPrefix=" + new string('A', 65))).StatusCode);
    }

    [Fact]
    public async Task Invalid_internal_fields_duplicates_and_stale_versions_leave_no_partial_changes()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var sku = "VALID-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, sku));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var path = $"/api/v1/articles/{id:D}";
        foreach (var property in new[] { "actorId", "recordedAt", "version", "operationId", "isDefinitionLocked", "minimumAvailable", "quantity", "locationId" })
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(Body(family, sku)))!;
            body[property] = JsonSerializer.SerializeToElement("internal");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", body)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, $" {sku.ToLowerInvariant()} "))).StatusCode);
        foreach (var body in new[] { Body(Guid.NewGuid(), "ABSENT"), Body(family, " "), Body(family, "EMPTY-NAME", name: " "), Body(family, "NO-UNIT", " ") })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", new { familyId = "not-a-uuid" })).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await api.WriteAsync(HttpMethod.Put, path, Body(family, sku))).StatusCode);
        var key = Guid.NewGuid(); var changed = Body(family, sku, name: "Ganador");
        var saved = await api.WriteAsync(HttpMethod.Put, path, changed, key, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await api.WriteAsync(HttpMethod.Put, path, Body(family, sku, name: "Obsoleto"), version: created.Headers.ETag.ToString())).StatusCode);
        var replay = await api.WriteAsync(HttpMethod.Put, path, changed, key, created.Headers.ETag.ToString());
        Assert.Equal(saved.Headers.ETag, replay.Headers.ETag);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal("Ganador", (await db.Articles.SingleAsync(value => value.Id == id)).Name);
        Assert.Equal(2, await db.Set<MasterDataAudit>().CountAsync(value => value.EntityId == id));
        Assert.Single(await db.Set<StockPolicy>().Where(value => value.ArticleId == id).ToListAsync());
    }

    [Fact]
    public async Task Reads_and_writes_require_the_real_session_permission_and_csrf()
    {
        using var factory = fixture.Factory(); using var manager = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await manager.FamilyAsync(); var body = Body(family, "AUTH-" + Guid.NewGuid().ToString("N"));
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/articles")).StatusCode);
        using var reader = await MasterDataApi.LoginAsync(fixture, factory, "Operario");
        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync("/api/v1/articles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.WriteAsync(HttpMethod.Post, "/api/v1/articles", body)).StatusCode);
        using var admin = await MasterDataApi.LoginAsync(fixture, factory, "Administrador");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.WriteAsync(HttpMethod.Post, "/api/v1/articles", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.Client.PostAsJsonAsync("/api/v1/articles", body)).StatusCode);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_article_policy_and_confirmed_result()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync(); var sku = "ROLLBACK-" + Guid.NewGuid().ToString("N"); var key = Guid.NewGuid();
        using var scope = fixture.BootstrapServices.CreateScope(); var admin = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER dbo.__ArticleAuditFailure ON dbo.MasterDataAudit AFTER INSERT AS
                THROW 51001, N'Fallo de auditoría inducido por prueba', 1;
            """);
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, sku), key)).StatusCode); }
        finally { await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.__ArticleAuditFailure"); }
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Articles.AnyAsync(value => value.Sku == sku));
        Assert.False(await db.Set<StockPolicy>().AnyAsync(value => !db.Articles.Any(article => article.Id == value.ArticleId)));
        Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
        Assert.False(await db.Set<MasterDataAudit>().AnyAsync(value => value.OperationId == key));
        Assert.Equal(HttpStatusCode.Created, (await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, sku), key)).StatusCode);
    }

    [Fact]
    public async Task Missing_initial_policy_is_a_dependency_failure_not_an_invented_zero()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", Body(family, "POLICY-" + Guid.NewGuid().ToString("N")));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        await using var db = fixture.ApplicationDatabase();
        await db.Set<StockPolicy>().Where(value => value.ArticleId == id).ExecuteDeleteAsync();
        var response = await api.Client.GetAsync($"/api/v1/articles/{id:D}/context");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("DependencyUnavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/v1/articles/{id:D}")).StatusCode);
    }
}
