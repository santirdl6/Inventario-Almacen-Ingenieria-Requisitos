using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingAuthorizationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task All_public_routes_apply_their_permission_and_antiforgery_before_business_effects()
    {
        using var support = new PurchasingTestSupport(fixture); var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var reader = await support.SessionAsync("Operario"); using var admin = await support.SessionAsync("Administrador");
        using var anonymous = support.Factory.CreateClient();
        var body = PurchasingTestSupport.Draft(supplier, article); var key = Guid.NewGuid();
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); var path = created.Headers.Location!.ToString();
        foreach (var route in new[] { "/api/v1/purchase-orders", "/api/v1/purchase-orders/settings", path, path + "/context", path + "/delivery", path + "/send-preview", path + "/delivery/document" })
        {
            using var noSession = await anonymous.GetAsync(route); Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
            using var noRole = await admin.Client.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
            using var read = await reader.Client.GetAsync(route);
            Assert.Equal(route.EndsWith("send-preview") ? HttpStatusCode.Forbidden : route.EndsWith("/document") ? HttpStatusCode.NotFound : HttpStatusCode.OK, read.StatusCode);
        }
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var denied = await reader.WriteAsync(method, method == HttpMethod.Post ? "/api/v1/purchase-orders" : path, body, version: created.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var sendDenied = await reader.WriteAsync(HttpMethod.Post, path + "/send", new { kind = "initial", reviewToken = "invalid" });
        Assert.Equal(HttpStatusCode.Forbidden, sendDenied.StatusCode);
        using var noCsrfRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/purchase-orders") { Content = JsonContent.Create(body) };
        noCsrfRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var noCsrf = await buyer.Client.SendAsync(noCsrfRequest); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("AntiforgeryValidationFailed", (await noCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var otherBuyer = await support.SessionAsync();
        using var stolen = await otherBuyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body, key);
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        using var lookup = await reader.Client.GetAsync($"/api/v1/operations/{key:D}"); Assert.Equal(HttpStatusCode.Forbidden, lookup.StatusCode);
    }

    [Fact]
    public async Task Manage_and_send_are_independent_even_when_the_usual_buyer_role_combines_them()
    {
        using var support = new PurchasingTestSupport(fixture); var (supplier, article) = await support.MastersAsync();
        async Task<MasterDataApi> WithPermission(string permission)
        {
            var email = await fixture.CreateUserAsync("Operario");
            using var scope = fixture.BootstrapServices.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.AddClaimAsync((await users.FindByEmailAsync(email))!, new System.Security.Claims.Claim("permission", permission))).Succeeded);
            var client = support.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password = IdentityFixture.Password }) };
            request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return new(client, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!);
        }
        using var manager = await WithPermission("Purchasing.Manage"); using var sender = await WithPermission("Purchasing.Send");
        var body = PurchasingTestSupport.Draft(supplier, article);
        using var created = await manager.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = created.Headers.Location!.ToString();
        using var deniedPreview = await manager.Client.GetAsync(path + "/send-preview"); Assert.Equal(HttpStatusCode.Forbidden, deniedPreview.StatusCode);
        using var deniedCreate = await sender.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", body); Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);
        var preview = await sender.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        using var requested = await sender.WriteAsync(HttpMethod.Post, path + "/send", new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() }, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
    }
}
