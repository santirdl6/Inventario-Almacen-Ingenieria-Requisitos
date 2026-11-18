using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataFamilyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Renaming_and_deactivating_a_family_preserves_article_identity_state_and_policy()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var family = await api.FamilyAsync();
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, "FAMILY-" + Guid.NewGuid().ToString("N")));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var detail = await api.Client.GetAsync($"/api/v1/families/{family:D}");
        var changed = await api.WriteAsync(HttpMethod.Put, $"/api/v1/families/{family:D}",
            new { code = "RENAMED-" + Guid.NewGuid().ToString("N"), name = "Familia renombrada e inactiva", isActive = false }, version: detail.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var article = await api.Client.GetAsync($"/api/v1/articles/{id:D}");
        Assert.Equal(created.Headers.ETag, article.Headers.ETag);
        var body = await article.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(family, body.GetProperty("familyId").GetGuid()); Assert.True(body.GetProperty("isActive").GetBoolean());
        var context = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/articles/{id:D}/context");
        Assert.Equal("Familia renombrada e inactiva", context.GetProperty("family").GetProperty("name").GetString());
        Assert.Equal("0.000", context.GetProperty("minimumAvailable").GetString());
    }

    private async Task<(HttpClient Client, string Token)> LoginAsync(ApiFactory factory, string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { email = await fixture.CreateUserAsync(role), password = IdentityFixture.Password }) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var login = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (client, (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!);
    }

    private static Task<HttpResponseMessage> Write(HttpClient client, string token, HttpMethod method, string path,
        object body, Guid key, string? version = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token); request.Headers.Add("Idempotency-Key", key.ToString());
        if (version is not null) request.Headers.Add("If-Match", version);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Families_are_normalized_audited_and_replayed_with_the_original_version()
    {
        using var factory = fixture.Factory();
        var (client, token) = await LoginAsync(factory, "GestorMaestros");
        using (client)
        {
            var key = Guid.NewGuid();
            var code = "F-" + Guid.NewGuid().ToString("N");
            var body = new { code = $" {code.ToLowerInvariant()} ", name = " General ", isActive = true };
            var created = await Write(client, token, HttpMethod.Post, "/api/v1/families", body, key);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var value = await created.Content.ReadFromJsonAsync<JsonElement>();
            var family = value.GetProperty("resource");
            var id = family.GetProperty("id").GetGuid();
            Assert.Equal(code.ToUpperInvariant(), family.GetProperty("code").GetString());
            Assert.Equal("General", family.GetProperty("name").GetString());
            Assert.Equal($"/api/v1/families/{id:D}", created.Headers.Location!.ToString());
            var originalTag = created.Headers.ETag!.ToString();
            var replay = await Write(client, token, HttpMethod.Post, "/api/v1/families", body, key);
            Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            Assert.Equal(originalTag, replay.Headers.ETag!.ToString());

            var editKey = Guid.NewGuid();
            var changed = new { code, name = "Nombre actualizado", isActive = false };
            var edited = await Write(client, token, HttpMethod.Put, $"/api/v1/families/{id:D}", changed, editKey, originalTag);
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.NotEqual(originalTag, edited.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await Write(client, token, HttpMethod.Put,
                $"/api/v1/families/{id:D}", changed, Guid.NewGuid(), originalTag)).StatusCode);
            var editReplay = await Write(client, token, HttpMethod.Put, $"/api/v1/families/{id:D}", changed, editKey, originalTag);
            Assert.Equal(await edited.Content.ReadAsStringAsync(), await editReplay.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, (await Write(client, token, HttpMethod.Post,
                "/api/v1/families", body, Guid.NewGuid())).StatusCode);
            var inactive = await client.GetFromJsonAsync<JsonElement>($"/api/v1/families?state=inactive&q={code}");
            Assert.Single(inactive.GetProperty("items").EnumerateArray());

            await using var db = fixture.ApplicationDatabase();
            var audits = await db.Set<MasterDataAudit>().Where(row => row.EntityId == id).ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.Contains(audits, row => row.Action == MasterDataAction.Create);
            Assert.Contains(audits, row => row.Action == MasterDataAction.Deactivate);
            Assert.Equal(2, await db.OperationResults.CountAsync(row => row.SourceDocumentId == id));
        }
    }

    [Fact]
    public async Task Reader_cannot_write_and_invalid_fields_or_versions_do_not_persist()
    {
        using var factory = fixture.Factory();
        var (manager, token) = await LoginAsync(factory, "GestorMaestros");
        using (manager)
        {
            var bad = await Write(manager, token, HttpMethod.Post, "/api/v1/families",
                new { code = "", name = "Válido", isActive = true }, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
            Assert.Equal("ValidationFailed", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            var forged = await Write(manager, token, HttpMethod.Post, "/api/v1/families",
                new { code = "A", name = "Válido", actorId = Guid.NewGuid() }, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, forged.StatusCode);
            var (reader, readerToken) = await LoginAsync(factory, "Operario");
            using (reader)
            {
                Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/v1/families")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await Write(reader, readerToken, HttpMethod.Post,
                    "/api/v1/families", new { code = "DENIED", name = "No permitido" }, Guid.NewGuid())).StatusCode);
            }
        }
    }
}
