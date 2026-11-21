using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Inventario.Api.Infrastructure.Identity;
using Xunit;

namespace Inventario.IntegrationTests.Operations;

public sealed class OperationQueryTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static async Task<HttpClient> LoginAsync(ApiFactory factory, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { email, password = IdentityFixture.Password }) };
        request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        return client;
    }

    [Fact]
    public async Task Outcome_is_reauthorized_without_loading_a_deleted_resource()
    {
        using var factory = fixture.Factory();
        var email = await fixture.CreateUserAsync("GestorMaestros");
        using var bootstrap = fixture.BootstrapServices.CreateScope();
        var users = bootstrap.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var principal = await bootstrap.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>()
            .CreateUserPrincipalAsync((await users.FindByEmailAsync(email))!);
        principal = await bootstrap.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);
        var operation = Guid.NewGuid();
        var originalResource = $"/api/v1/articles/{Guid.NewGuid():D}";
        var accessor = factory.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = principal };
        try
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteAsync(
                new OperationRequest(operation, "DELETE", originalResource, "MasterData.Manage", default),
                (_, _) => Task.FromResult(new OperationResponse(204, null)));
        }
        finally { accessor.HttpContext = null; }

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/operations/{operation:D}")).StatusCode);
        using var reader = await LoginAsync(factory, await fixture.CreateUserAsync("Operario"));
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/v1/operations/{operation:D}")).StatusCode);
        using var manager = await LoginAsync(factory, email);
        var response = await manager.GetAsync($"/api/v1/operations/{operation:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var outcome = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(204, outcome.GetProperty("status").GetInt32());
        Assert.Equal(originalResource, outcome.GetProperty("resource").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("body").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/operations/{Guid.NewGuid():D}")).StatusCode);
    }
}
