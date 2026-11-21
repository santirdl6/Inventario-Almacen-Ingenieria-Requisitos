using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataSupplierTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Optional_contact_is_normalized_and_states_versions_and_replays_are_preserved()
    {
        using var factory = fixture.Factory(); using var api = await MasterDataApi.LoginAsync(fixture, factory);
        var code = "SUP-" + Guid.NewGuid().ToString("N").ToUpperInvariant(); var key = Guid.NewGuid();
        var body = new { code = $" {code.ToLowerInvariant()} ", name = " Proveedor ", email = " ", taxIdentifier = " ", isActive = true };
        var created = await api.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var resource = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        var id = resource.GetProperty("id").GetGuid(); var path = $"/api/v1/suppliers/{id:D}";
        Assert.Equal(code, resource.GetProperty("code").GetString()); Assert.Equal("Proveedor", resource.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, resource.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, resource.GetProperty("taxIdentifier").ValueKind);
        Assert.Equal(path, created.Headers.Location!.ToString());
        var replay = await api.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", body, key);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var changed = await api.WriteAsync(HttpMethod.Put, path, new { code, name = "Contacto", email = " supplier@example.test ", taxIdentifier = " Libre 123 ", isActive = false }, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var detail = await api.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("supplier@example.test", detail.GetProperty("email").GetString());
        Assert.Equal("Libre 123", detail.GetProperty("taxIdentifier").GetString());
        Assert.False(detail.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await api.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", body)).StatusCode);
        var list = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers?q={code}&state=inactive");
        Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await api.WriteAsync(HttpMethod.Put, path, body, version: created.Headers.ETag.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.WriteAsync(HttpMethod.Put, path, body, version: changed.Headers.ETag!.ToString())).StatusCode);
    }

    [Fact]
    public async Task Invalid_contacts_internal_fields_and_unauthorized_writes_are_rejected()
    {
        using var factory = fixture.Factory(); using var manager = await MasterDataApi.LoginAsync(fixture, factory);
        var code = "INVALID-" + Guid.NewGuid().ToString("N");
        var invalid = await manager.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", new { code, name = "Válido", email = "invalid", taxIdentifier = (string?)null });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.True((await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("email", out _));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", new { code, name = "Válido", actorId = Guid.NewGuid() })).StatusCode);
        var body = new { code, name = "Válido" };
        using var reader = await MasterDataApi.LoginAsync(fixture, factory, "Operario");
        using var admin = await MasterDataApi.LoginAsync(fixture, factory, "Administrador");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync("/api/v1/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.WriteAsync(HttpMethod.Post, "/api/v1/suppliers", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.Client.PostAsJsonAsync("/api/v1/suppliers", body)).StatusCode);
        var list = await manager.Client.GetFromJsonAsync<JsonElement>($"/api/v1/suppliers?q={code}&state=all");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }
}
