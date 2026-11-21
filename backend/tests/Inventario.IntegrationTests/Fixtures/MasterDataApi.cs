using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Inventario.IntegrationTests.Fixtures;

public sealed class MasterDataApi(HttpClient client, string token) : IDisposable
{
    public HttpClient Client { get; } = client;
    public string Token { get; } = token;
    public static async Task<MasterDataApi> LoginAsync(IdentityFixture fixture, ApiFactory factory, string role = "GestorMaestros", params string[] additionalRoles)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { email = await fixture.CreateUserAsync([role, .. additionalRoles]), password = IdentityFixture.Password }) };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new(client, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!);
    }

    public Task<HttpResponseMessage> WriteAsync(HttpMethod method, string path, object body, Guid? key = null, string? version = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", Token);
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        if (version is not null) request.Headers.Add("If-Match", version);
        return Client.SendAsync(request);
    }

    public async Task<Guid> FamilyAsync(bool active = true)
    {
        var response = await WriteAsync(HttpMethod.Post, "/api/v1/families", new { code = "F-" + Guid.NewGuid().ToString("N"), name = "Familia de artículos" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var family = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        var id = family.GetProperty("id").GetGuid();
        if (!active)
            Assert.Equal(HttpStatusCode.OK, (await WriteAsync(HttpMethod.Put, $"/api/v1/families/{id:D}",
                new { code = family.GetProperty("code").GetString(), name = "Familia inactiva", isActive = false },
                version: response.Headers.ETag!.ToString())).StatusCode);
        return id;
    }

    public void Dispose() => Client.Dispose();
}
