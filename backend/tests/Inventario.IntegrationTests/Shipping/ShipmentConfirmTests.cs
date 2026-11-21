using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Shipping.ShippingApiSupport;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T009/T010 (H-08…H-15, H-18, H-20; D-04, D-05, D-07; FR-007–FR-016; reglas 3 y 4 del contrato
// HU-03 de HU-05): confirmación de la salida por HTTP con sesión real, reservas reales de HU-05 y
// existencias preparadas con StockCore. T009 trae el núcleo (sin reservas y V-04); T010 lo completa.
public sealed class ShipmentConfirmTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task H08_confirms_without_reservations_and_records_one_shipment_movement()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "6"));
        var key = Guid.NewGuid();

        using var confirmed = await ConfirmAsync(operario, id, seed.Order, key: key);
        var text = await confirmed.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == confirmed.StatusCode, text);
        Assert.Null(confirmed.Headers.ETag);
        var result = JsonDocument.Parse(text).RootElement;
        Assert.Equal(key, result.GetProperty("operationId").GetGuid());
        Assert.Equal(id, result.GetProperty("shipmentId").GetGuid());
        Assert.Equal("Fulfilled", result.GetProperty("orderState").GetString());
        Assert.Equal("0.000", result.GetProperty("lines")[0].GetProperty("ownReservationConsumed").GetString());

        Assert.Equal(4m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
        var (order, _) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        Assert.Equal("6.000", order.GetProperty("lines")[0].GetProperty("shipped").GetString());
        Assert.Equal("Fulfilled", order.GetProperty("state").GetString());
        var (detail, _) = await DetailAsync(operario.Client, id);
        Assert.Equal("Confirmed", detail.GetProperty("state").GetString());
        Assert.Equal(key, detail.GetProperty("operationId").GetGuid());

        await using var database = fixture.ApplicationDatabase();
        var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
            .SingleAsync(value => value.SourceDocumentType == "shipment" && value.SourceDocumentId == id);
        Assert.Equal(MovementKind.Shipment, movement.Kind);
        Assert.Equal(key, movement.OperationId);
        Assert.Equal("PED", movement.SupportReference![..3]);
        Assert.Equal("Cliente Norte", movement.CounterpartyReference);
        Assert.Null(movement.CounterpartyNotApplicableReason);
        Assert.StartsWith("Salida del pedido ", movement.Reason);
        var entry = Assert.Single(movement.Entries);
        Assert.Equal(StockEffect.ShipAvailable, entry.Effect);
        Assert.Equal(6m, entry.Quantity);
        await support.Stock.AssertJournalMatchesBalanceAsync(seed.Article, seed.Location);
    }

    // V-04 (H-10): reserva propia frente a ajena en el mismo bucket.
    [Fact]
    public async Task H10_consumes_the_own_reservation_and_never_the_foreign_one()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (orderB, lineB, _) = await StockApiSupport.CreateOrderAsync(operario, seed.Article, "5");
        var own = await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        var foreign = await ReserveAsync(responsable, orderB, lineB, seed.Location, seed.Lot, "3");
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "6"));

        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
        {
            var text = await confirmed.Content.ReadAsStringAsync();
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, text);
            Assert.Equal("4.000", JsonDocument.Parse(text).RootElement.GetProperty("lines")[0].GetProperty("ownReservationConsumed").GetString());
        }

        var balance = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!;
        Assert.Equal(4m, balance.Physical);
        Assert.Equal(3m, balance.Reserved);
        var (ownReservation, _) = await StockApiSupport.ReservationAsync(operario.Client, own);
        Assert.Equal("Consumed", ownReservation.GetProperty("state").GetString());
        Assert.Equal("4.000", ownReservation.GetProperty("allocations")[0].GetProperty("consumed").GetString());
        var (foreignReservation, _) = await StockApiSupport.ReservationAsync(operario.Client, foreign);
        Assert.Equal("Active", foreignReservation.GetProperty("state").GetString());
        Assert.Equal("3.000", foreignReservation.GetProperty("allocations")[0].GetProperty("remaining").GetString());

        await using var database = fixture.ApplicationDatabase();
        var consumption = await database.Set<ShipmentReservationConsumption>().AsNoTracking()
            .SingleAsync(value => database.Set<ShipmentLine>().Any(line => line.Id == value.ShipmentLineId && line.ShipmentId == id));
        Assert.Equal(4m, consumption.Quantity);
        var movement = await database.Set<StockMovement>().AsNoTracking().Include(value => value.Entries)
            .SingleAsync(value => value.SourceDocumentId == id);
        Assert.Equal(4m, movement.Entries.Single(entry => entry.Effect == StockEffect.ShipReserved).Quantity);
        Assert.Equal(2m, movement.Entries.Single(entry => entry.Effect == StockEffect.ShipAvailable).Quantity);
        await support.AssertCommitmentsMatchBalancesAsync(seed.Article, seed.Location);
    }
}
