using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Stock;
using Xunit;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T006 (H-03; FR-001a, US1.11; research D-11): búsqueda exacta del pedido por su referencia
// externa, sin distinguir mayúsculas ni espacios extremos, con la regla de unicidad del alta de
// HU-05. Devuelve como mucho un pedido; nunca una lista ni coincidencias parciales.
public sealed class CustomerOrderLookupTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string Lookup = "/api/v1/customer-orders/lookup";

    [Fact]
    public async Task H03_finds_the_order_ignoring_case_and_outer_spaces_and_shows_its_state()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var (article, _) = await support.ArticleAsync();
        var (order, _, reference) = await StockApiSupport.CreateOrderAsync(operario, article, "6");

        using var found = await operario.Client.GetAsync($"{Lookup}?externalReference={Uri.EscapeDataString("  " + reference.ToLowerInvariant() + " ")}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Contains("no-store", found.Headers.CacheControl!.ToString());
        var body = await found.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(order, body.GetProperty("id").GetGuid());
        Assert.Equal(reference, body.GetProperty("externalReference").GetString());
        Assert.Equal("Open", body.GetProperty("state").GetString());
        Assert.Equal(3, body.EnumerateObject().Count());
    }

    [Fact]
    public async Task H03_an_unknown_or_partial_reference_is_not_found_and_blank_or_too_long_is_rejected()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var (article, _) = await support.ArticleAsync();
        var (_, _, reference) = await StockApiSupport.CreateOrderAsync(operario, article, "1");

        foreach (var unknown in new[] { "PED-NO-EXISTE-" + StockApiSupport.Unique(6), reference[..^1] })
        {
            using var missing = await operario.Client.GetAsync($"{Lookup}?externalReference={Uri.EscapeDataString(unknown)}");
            var problem = await StockApiSupport.ProblemAsync(missing, HttpStatusCode.NotFound, "NotFound");
            Assert.Equal("No hay ningún pedido con esa referencia.", problem.GetProperty("detail").GetString());
        }

        foreach (var invalid in new[] { "", "   ", new string('P', 65) })
        {
            using var rejected = await operario.Client.GetAsync($"{Lookup}?externalReference={Uri.EscapeDataString(invalid)}");
            var problem = await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
            Assert.True(problem.GetProperty("errors").TryGetProperty("externalReference", out _));
        }
        using (var absent = await operario.Client.GetAsync(Lookup))
            await StockApiSupport.ProblemAsync(absent, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
    }

    [Fact]
    public async Task H03_requires_a_session_and_inventory_read()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var (article, _) = await support.ArticleAsync();
        var (order, _, reference) = await StockApiSupport.CreateOrderAsync(operario, article, "1");
        var path = $"{Lookup}?externalReference={Uri.EscapeDataString(reference)}";

        foreach (var role in new[] { "GestorMaestros", "ResponsableAlmacen" })
        {
            var session = await support.SessionAsync(role);
            using var allowed = await session.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(order, (await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        var admin = await support.SessionAsync("Administrador");
        using (var denied = await admin.Client.GetAsync(path))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
    }
}
