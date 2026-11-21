using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingContingencyAuthorizationTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Composite_policy_is_enforced_for_mutation_replay_and_result_lookup()
    {
        using var operatorSession = await fixture.AsAsync();
        using var adjusterSession = await fixture.AsAsync("ResponsableAlmacen");
        using var bothSession = await fixture.AsContingencyAsync();
        async Task<ClaimsPrincipal> Principal(Inventario.IntegrationTests.Fixtures.MasterDataApi session)
        {
            var me = await session.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
            using var scope = fixture.Database.BootstrapServices.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(me.GetProperty("id").GetString()!);
            var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user!);
            return await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);
        }
        var receiving = await Principal(operatorSession); var adjusting = await Principal(adjusterSession); var both = await Principal(bothSession);
        var id = Guid.NewGuid();
        var request = new OperationRequest(id, "POST", "/api/v1/contingencies/reconcile", "Receiving.Reconcile",
            JsonSerializer.SerializeToElement(new { voucherId = "V-01" }));
        var accessor = fixture.Support.Factory.Services.GetRequiredService<IHttpContextAccessor>();
        async Task<OperationResponse> Execute(ClaimsPrincipal principal)
        {
            accessor.HttpContext = new DefaultHttpContext { User = principal };
            try
            {
                using var scope = fixture.Support.Factory.Services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteAsync(request,
                    (_, _) => Task.FromResult(OperationResponse.Json(200, new { reconciled = true })));
            }
            finally { accessor.HttpContext = null; }
        }
        Assert.Equal(403, (await Assert.ThrowsAsync<Inventario.Api.Infrastructure.Http.InventoryProblemException>(() => Execute(receiving))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<Inventario.Api.Infrastructure.Http.InventoryProblemException>(() => Execute(adjusting))).Status);
        var first = await Execute(both);
        Assert.Equal(first.Body, (await Execute(both)).Body);
        Assert.Equal(403, (await Assert.ThrowsAsync<Inventario.Api.Infrastructure.Http.InventoryProblemException>(() => Execute(receiving))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<Inventario.Api.Infrastructure.Http.InventoryProblemException>(() => Execute(adjusting))).Status);
        Assert.Equal(403, (int)(await operatorSession.Client.GetAsync($"/api/v1/operations/{id}")).StatusCode);
        Assert.Equal(403, (int)(await adjusterSession.Client.GetAsync($"/api/v1/operations/{id}")).StatusCode);
        Assert.Equal(200, (int)(await bothSession.Client.GetAsync($"/api/v1/operations/{id}")).StatusCode);
    }
}
