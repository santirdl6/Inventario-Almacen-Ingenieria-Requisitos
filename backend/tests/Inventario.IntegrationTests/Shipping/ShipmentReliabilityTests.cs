using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Shipping.ShippingApiSupport;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T011 (H-19, V-08; FR-020, US5.7, SC-004): un fallo tras escribir y antes del commit en crear,
// editar, eliminar y confirmar no deja ningún efecto ni OperationResult; repetir la misma clave aplica
// un solo efecto. Usa StockTestHarness.FailingFactory sin modificarlo.
public sealed class ShipmentReliabilityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task H19_a_failure_before_commit_leaves_nothing_and_the_same_key_applies_once()
    {
        var failure = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, failure);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");

        // Crear.
        var createKey = Guid.NewGuid();
        var body = Body(seed, "4");
        failure.Arm();
        using (var failed = await operario.WriteAsync(HttpMethod.Post, Shipments, body, createKey))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(createKey, db => db.Set<Shipment>().AnyAsync(value => value.CustomerOrderId == seed.Order));
        var (id, etag) = await CreateDraftAsync(operario, body, createKey);

        // Editar.
        var editKey = Guid.NewGuid();
        var editBody = Body(seed, "5");
        failure.Arm();
        using (var failed = await operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{id:D}", editBody, editKey, etag))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(editKey, db => db.Set<ShipmentLine>().AnyAsync(value => value.ShipmentId == id && value.Quantity == 5m));
        Assert.Equal(etag, (await DetailAsync(operario.Client, id)).ETag);
        using (var edited = await operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{id:D}", editBody, editKey, etag))
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using (var replay = await operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{id:D}", editBody, editKey, etag))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        // Eliminar (otro borrador, para conservar el primero).
        var (other, otherTag) = await CreateDraftAsync(operario, Body(seed, "1"));
        var deleteKey = Guid.NewGuid();
        failure.Arm();
        using (var failed = await operario.WriteAsync(HttpMethod.Delete, $"{Shipments}/{other:D}", new { }, deleteKey, otherTag))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(deleteKey, async db => !await db.Set<Shipment>().AnyAsync(value => value.Id == other));
        using (var deleted = await operario.WriteAsync(HttpMethod.Delete, $"{Shipments}/{other:D}", new { }, deleteKey, otherTag))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Confirmar: nada cambia tras el fallo; la misma clave confirma una sola vez.
        var (_, current) = await DetailAsync(operario.Client, id);
        var (_, orderVersion) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        var confirmKey = Guid.NewGuid();
        failure.Arm();
        using (var failed = await ConfirmAsync(operario, id, seed.Order, current, orderVersion, confirmKey))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(confirmKey, db => db.Set<StockMovement>().AnyAsync(value => value.SourceDocumentId == id));
        var balance = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!;
        Assert.Equal(10m, balance.Physical);
        Assert.Equal(4m, balance.Reserved);
        Assert.Equal(orderVersion, (await StockApiSupport.OrderAsync(operario.Client, seed.Order)).ETag);
        Assert.Equal("Draft", (await DetailAsync(operario.Client, id)).Body.GetProperty("state").GetString());

        string text;
        using (var confirmed = await ConfirmAsync(operario, id, seed.Order, current, orderVersion, confirmKey))
        {
            text = await confirmed.Content.ReadAsStringAsync();
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, text);
        }
        using (var replay = await ConfirmAsync(operario, id, seed.Order, current, orderVersion, confirmKey))
            Assert.Equal(text, await replay.Content.ReadAsStringAsync());
        await using var check = fixture.ApplicationDatabase();
        Assert.Equal(1, await check.Set<StockMovement>().CountAsync(value => value.SourceDocumentId == id));
        Assert.Equal(5m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
        await AssertShipmentInvariantsAsync(support, id);
    }

    private async Task AssertNothingAsync(Guid key, Func<Inventario.Api.Infrastructure.Persistence.InventoryDbContext, Task<bool>> effect)
    {
        await using var database = fixture.ApplicationDatabase();
        Assert.False(await database.Set<OperationResult>().AnyAsync(value => value.OperationId == key));
        Assert.False(await effect(database));
    }
}
