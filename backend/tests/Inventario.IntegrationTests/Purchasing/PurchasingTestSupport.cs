using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

internal sealed class PurchasingTestSupport : IDisposable
{
    private readonly IdentityFixture fixture;
    public ApiFactory Factory { get; }

    public PurchasingTestSupport(IdentityFixture fixture, IInterceptor? interceptor = null,
        Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settingsOverrides = null)
    {
        this.fixture = fixture;
        var settings = new Dictionary<string, string?>(fixture.Settings)
        { ["PurchaseDelivery:Enabled"] = "false", ["Purchasing:Currency"] = "EUR" };
        if (settingsOverrides is not null) foreach (var value in settingsOverrides) settings[value.Key] = value.Value;
        Factory = new ApiFactory(settings, "Development", services =>
        {
            if (interceptor is not null)
                services.ConfigureDbContext<InventoryDbContext>((_, options) => options.AddInterceptors(interceptor));
            configure?.Invoke(services);
        });
    }

    public Task<MasterDataApi> SessionAsync(string role = "Comprador") => MasterDataApi.LoginAsync(fixture, Factory, role);

    public async Task<(Guid SupplierId, Guid ArticleId)> MastersAsync(string? email = "compras@proveedor.test", string articleName = "Artículo de validación")
    {
        using var manager = await SessionAsync("GestorMaestros");
        using var supplier = await manager.WriteAsync(HttpMethod.Post, "/api/v1/suppliers",
            new { code = "P-" + Guid.NewGuid().ToString("N"), name = "Proveedor de compras", email });
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        var supplierId = (await supplier.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        using var article = await manager.WriteAsync(HttpMethod.Post, "/api/v1/articles",
            MasterDataArticleTests.Body(await manager.FamilyAsync(), "P-" + Guid.NewGuid().ToString("N"), name: articleName, lot: false));
        Assert.Equal(HttpStatusCode.Created, article.StatusCode);
        return (supplierId, (await article.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid());
    }

    public static object Draft(Guid supplierId, Guid articleId, string quantity = "10", Guid? lineId = null) => new
    {
        supplierId, expectedDeliveryDate = "2026-12-01", lines = new[]
        { new { id = lineId ?? Guid.NewGuid(), articleId, quantity, unitPrice = "12.50", expectedDeliveryDate = (string?)null } }
    };

    public void Dispose() => Factory.Dispose();
}
