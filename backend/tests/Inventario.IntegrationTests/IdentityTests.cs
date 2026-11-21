using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class IdentityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") };

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        return data.GetProperty("requestToken").GetString()!;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path,
        object body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, data.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("traceId").GetString()));
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Protected_session_routes_do_not_allow_anonymous_access()
    {
        using var factory = fixture.Factory();
        using var client = factory.CreateClient(ClientOptions);
        await AssertProblemAsync(await client.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "Unauthenticated");
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/logout", new { }), HttpStatusCode.Unauthorized, "Unauthenticated");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/health")).StatusCode);
        await AssertProblemAsync(await client.GetAsync("/api/v1/unknown"), HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task Login_rotates_request_token_and_logout_requires_the_authenticated_token()
    {
        using var factory = fixture.Factory();
        using var client = factory.CreateClient(ClientOptions);
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        var cookies = csrf.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, cookie => cookie.StartsWith("Inventario.Antiforgery=") && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cookies, cookie => cookie.StartsWith("XSRF-TOKEN=") && !cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        var anonymous = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!;

        var login = await PostAsync(client, "/api/v1/auth/login", new { email = IdentityFixture.AdminEmail, password = IdentityFixture.Password }, anonymous);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("Inventario.Session=") && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        var authenticated = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!;
        Assert.NotEqual(anonymous, authenticated);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/logout", new { }), HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/logout", new { }, anonymous), HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/api/v1/auth/logout", new { }, authenticated)).StatusCode);
        await AssertProblemAsync(await client.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized, "Unauthenticated");
    }

    [Fact]
    public async Task Login_rejects_missing_and_foreign_tokens_and_invalid_fields()
    {
        using var factory = fixture.Factory();
        using var client = factory.CreateClient(ClientOptions);
        using var other = factory.CreateClient(ClientOptions);
        var credentials = new { email = IdentityFixture.AdminEmail, password = IdentityFixture.Password };
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/login", credentials), HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        var token = await TokenAsync(client);
        await TokenAsync(other);
        await AssertProblemAsync(await PostAsync(other, "/api/v1/auth/login", credentials, token), HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/login", new { email = (string?)null, password = "" }, token), HttpStatusCode.UnprocessableEntity, "ValidationFailed");
    }

    [Fact]
    public async Task Failed_logins_lock_the_account_without_disclosing_its_existence()
    {
        var email = await fixture.CreateUserAsync("Operario");
        using var factory = fixture.Factory();
        using var client = factory.CreateClient(ClientOptions);
        var token = await TokenAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
            await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/login", new { email, password = "Wrong_2026!Password" }, token), HttpStatusCode.Unauthorized, "InvalidCredentials");
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/login", new { email, password = IdentityFixture.Password }, token), HttpStatusCode.Unauthorized, "InvalidCredentials");
        await AssertProblemAsync(await PostAsync(client, "/api/v1/auth/login", new { email = "absent@h0.test", password = IdentityFixture.Password }, token), HttpStatusCode.Unauthorized, "InvalidCredentials");
        using var scope = fixture.BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync(email))!));
    }

    [Fact]
    public async Task Role_permissions_are_additive_idempotent_and_do_not_grant_administrators_stock_access()
    {
        var email = await fixture.CreateUserAsync("Operario", "Comprador");
        using var factory = fixture.Factory();
        using var client = factory.CreateClient(ClientOptions);
        var token = await TokenAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "/api/v1/auth/login", new { email, password = IdentityFixture.Password }, token)).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("Inventory.Read", permissions);
        Assert.Contains("Purchasing.Send", permissions);
        Assert.Equal(permissions.Length, permissions.Distinct().Count());
        Assert.DoesNotContain("Inventory.Adjust", permissions);
        Assert.DoesNotContain("Users.Manage", permissions);

        using var scope = factory.Services.CreateScope();
        var transform = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Administrador")], "test"));
        principal = await transform.TransformAsync(principal);
        principal = await transform.TransformAsync(principal);
        Assert.Single(principal.FindAll("permission"));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(principal, null, "Users.Manage")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, null, "Inventory.Adjust")).Succeeded);
    }

    [Fact]
    public async Task Bootstrap_is_repeatable_and_does_not_reset_an_existing_password()
    {
        var settings = new Dictionary<string, string?>(fixture.Settings)
        { ["Bootstrap:AdminPassword"] = "Different_2026!Password" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await DatabaseBootstrapper.InitializeAsync(configuration, fixture.BootstrapServices);
        using var scope = fixture.BootstrapServices.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(IdentityFixture.AdminEmail))!;
        Assert.True(await users.CheckPasswordAsync(admin, IdentityFixture.Password));
        Assert.False(await users.CheckPasswordAsync(admin, settings["Bootstrap:AdminPassword"]!));
        Assert.Equal(5, await database.Roles.CountAsync());
        Assert.Equal(1, await database.Users.CountAsync(user => user.Email == IdentityFixture.AdminEmail));
        Assert.Single(await users.GetRolesAsync(admin));
        Assert.False(database.Database.HasPendingModelChanges());
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(DatabaseConfiguration.ApplicationConnection(configuration));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(int, IS_SRVROLEMEMBER('sysadmin')) + COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0)";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Session_can_be_read_by_a_new_host_with_the_same_database_and_protection_keys()
    {
        using var first = fixture.Factory();
        using var client = first.CreateClient(ClientOptions);
        var token = await TokenAsync(client);
        var login = await PostAsync(client, "/api/v1/auth/login", new { email = IdentityFixture.AdminEmail, password = IdentityFixture.Password }, token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("Inventario.Session=")).Split(';')[0];
        using var second = fixture.Factory();
        using var restarted = second.CreateClient(ClientOptions);
        restarted.DefaultRequestHeaders.Add("Cookie", session);
        Assert.Equal(HttpStatusCode.OK, (await restarted.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Production_cookies_are_secure_and_openapi_is_only_exposed_in_development()
    {
        using var production = fixture.Factory("Production");
        using var client = production.CreateClient(ClientOptions);
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        Assert.All(csrf.Headers.GetValues("Set-Cookie"), cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        using var development = fixture.Factory();
        using var devClient = development.CreateClient(ClientOptions);
        var document = await devClient.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/auth/login", out _));
    }
}
