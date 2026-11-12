using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Inventario.Api.Domain.Identity;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class H0ReferenceTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private InventoryDbContext ApplicationDatabase()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(fixture.Settings).Build();
        return new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(DatabaseConfiguration.ApplicationConnection(configuration), sql => sql.EnableRetryOnFailure())
            .Options);
    }

    [Fact]
    public async Task Article_reference_preserves_metadata_and_enforces_unique_skus_in_sql()
    {
        var sku = "REF-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var article = new Article($" {sku.ToLowerInvariant()} ", " Artículo H0 ", " ud ", false, true);
        await using (var database = ApplicationDatabase())
        {
            database.Articles.Add(article);
            await database.SaveChangesAsync();
        }

        await using (var database = ApplicationDatabase())
        {
            var stored = await database.Articles.SingleAsync(value => value.Id == article.Id);
            Assert.Equal(sku, stored.Sku);
            Assert.Equal("Artículo H0", stored.Name);
            Assert.Equal("UD", stored.BaseUnit);
            Assert.False(stored.AllowsFractional);
            Assert.True(stored.TracksLot);
            Assert.True(stored.IsActive);
            Assert.Equal(8, stored.Version.Length);
            Assert.False(database.Database.HasPendingModelChanges());
        }

        await using (var database = ApplicationDatabase())
        {
            // Saltar la normalización del constructor para comprobar también la restricción SQL.
            database.Articles.Add(new Article(sku, "Duplicado", "UD", false, false));
            database.Entry(database.Articles.Local.Single()).Property(value => value.Sku).CurrentValue = sku.ToLowerInvariant();
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(error.InnerException);
            Assert.True(sql.Number is 2601 or 2627);
        }

        await using var check = ApplicationDatabase();
        Assert.Equal(1, await check.Articles.CountAsync(value => value.Sku == sku));
    }

    [Fact]
    public async Task Article_rowversion_prevents_a_stale_write_in_sql()
    {
        var article = new Article("VER-" + Guid.NewGuid().ToString("N"), "Original", "UD", false, false);
        await using (var setup = ApplicationDatabase())
        {
            setup.Articles.Add(article);
            await setup.SaveChangesAsync();
        }

        await using var first = ApplicationDatabase();
        await using var second = ApplicationDatabase();
        var firstArticle = await first.Articles.SingleAsync(value => value.Id == article.Id);
        var staleArticle = await second.Articles.SingleAsync(value => value.Id == article.Id);
        first.Entry(firstArticle).Property(value => value.Name).CurrentValue = "Primera edición";
        await first.SaveChangesAsync();
        second.Entry(staleArticle).Property(value => value.Name).CurrentValue = "Edición obsoleta";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = ApplicationDatabase();
        Assert.Equal("Primera edición", (await check.Articles.SingleAsync(value => value.Id == article.Id)).Name);
    }

    [Fact]
    public async Task Article_insert_is_rolled_back_on_failure_in_the_shared_sql_context()
    {
        var article = new Article("RB-" + Guid.NewGuid().ToString("N"), "No confirmado", "UD", false, false);
        await using (var database = ApplicationDatabase())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await database.Database.BeginTransactionAsync();
                database.Articles.Add(article);
                await database.SaveChangesAsync();
                throw new InvalidOperationException("Fallo simulado después de escribir y antes del commit.");
            }));
        }

        await using var check = ApplicationDatabase();
        Assert.False(await check.Articles.AnyAsync(value => value.Id == article.Id));
    }

    [Theory]
    [InlineData(false, "e1ce1ad4-809b-489f-b07d-ac79953c87eb")]
    [InlineData(true, null)]
    [InlineData(true, "no-es-un-guid")]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    [InlineData(true, "e1ce1ad4-809b-489f-b07d-ac79953c87eb")]
    public async Task Actor_rejects_anonymous_missing_malformed_empty_and_unknown_identities(bool authenticated, string? id)
    {
        using var scope = fixture.BootstrapServices.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity(authenticated ? IdentityConstants.ApplicationScheme : null);
        if (id is not null)
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        try
        {
            var actor = scope.ServiceProvider.GetRequiredService<ICurrentActor>();
            await Assert.ThrowsAsync<UnauthenticatedActorException>(() => actor.GetRequiredActorIdAsync());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public async Task Actor_rejects_execution_outside_an_authenticated_request()
    {
        using var scope = fixture.BootstrapServices.CreateScope();
        var actor = scope.ServiceProvider.GetRequiredService<ICurrentActor>();
        await Assert.ThrowsAsync<UnauthenticatedActorException>(() => actor.GetRequiredActorIdAsync());
    }

    [Fact]
    public async Task Actor_uses_the_real_identity_session_and_rejects_a_deleted_user()
    {
        var email = await fixture.CreateUserAsync("Operario");
        using var scope = fixture.BootstrapServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        using var factory = fixture.Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password = IdentityFixture.Password, actorId = Guid.NewGuid() })
        };
        login.Headers.Add("X-XSRF-TOKEN", token.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(login)).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());

        Assert.True((await users.DeleteAsync(user)).Succeeded);
        var rejected = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unauthenticated", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }
}
