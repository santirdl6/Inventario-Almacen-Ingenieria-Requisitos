using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Shipping;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T012 (C-19, C-20; FR-018–FR-020, US4, SC-005; research D-09; P-07). Una salida confirmada de 6
// cuando salieron 4 se corrige contando la ubicación (+2) y vinculando la línea a la salida en la
// revisión. Limitación P-07 (aprobada por A el 2026-11-21): el ajuste corrige solo el físico; la salida,
// lo servido del pedido, su estado y los consumos y remanentes de reservas no cambian (invariante 6).
public sealed class ShipmentCorrectionTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task C19_a_linked_count_corrects_the_physical_and_leaves_the_shipment_and_the_order_unchanged()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await ShippingApiSupport.SeedAsync(support, operario, 10m, "8");
        await ShippingApiSupport.ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        // Salida confirmada de 6 (4 de reserva propia + 2 disponibles) cuando en realidad salieron 4.
        var (shipment, _) = await ShippingApiSupport.CreateDraftAsync(operario, ShippingApiSupport.Body(seed, "6"));
        using (var confirmed = await ShippingApiSupport.ConfirmAsync(operario, shipment, seed.Order))
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        var before = await SnapshotAsync(seed, shipment);
        Assert.Equal(4m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);

        var (id, detail, etag) = await AwaitingAsync(operario, seed.Location, (seed.Article, "6"));
        var line = Line(detail, seed.Article, seed.Location).GetProperty("id").GetGuid();

        // Candidatas: solo salidas confirmadas que tocaron el bucket, con su pedido y sin vínculos aún.
        var candidates = await CandidatesAsync(responsable, id, line);
        var candidate = Assert.Single(candidates.GetProperty("items").EnumerateArray());
        Assert.Equal(shipment, candidate.GetProperty("shipmentId").GetGuid());
        Assert.Equal(seed.Order, candidate.GetProperty("customerOrderId").GetGuid());
        Assert.Equal("6.000", candidate.GetProperty("quantity").GetString());
        Assert.Empty(candidate.GetProperty("linkedCountLineIds").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(candidate.GetProperty("externalReference").GetString()));

        etag = await OkAsync(LinkAsync(responsable, id, etag, line, "Salida registrada de más", shipment));
        var (reviewed, _) = await DetailAsync(operario.Client, id);
        var linked = Line(reviewed, seed.Article, seed.Location);
        Assert.Equal(shipment, linked.GetProperty("correctsShipmentId").GetGuid());
        // P-07 visible en la respuesta: el vínculo corrige solo existencias.
        Assert.Contains("no cambia lo servido", linked.GetProperty("shipmentCorrectionNotice").GetString());
        var context = await ContextAsync(operario.Client, id);
        Assert.Equal(shipment, context.GetProperty("shipments")[0].GetProperty("shipmentId").GetGuid());
        Assert.Equal(line, (await CandidatesAsync(responsable, id, line)).GetProperty("items")[0]
            .GetProperty("linkedCountLineIds")[0].GetGuid());

        using var confirmedCount = await ConfirmAsync(responsable, id, etag);
        var text = await confirmedCount.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == confirmedCount.StatusCode, text);
        var correction = Assert.Single(JsonDocument.Parse(text).RootElement.GetProperty("shipmentCorrections").EnumerateArray());
        Assert.Equal((line, shipment, seed.Order), (correction.GetProperty("lineId").GetGuid(), correction.GetProperty("shipmentId").GetGuid(),
            correction.GetProperty("customerOrderId").GetGuid()));
        Assert.False(correction.GetProperty("orderChanged").GetBoolean());
        Assert.Contains("no cambia lo servido", correction.GetProperty("notice").GetString());

        // Físico +2; salida, servido, estado del pedido, consumos y remanentes idénticos (invariante 6).
        Assert.Equal(6m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
        Assert.Equal(before, await SnapshotAsync(seed, shipment));
        await AssertConfirmedInvariantsAsync(fixture, support, id);
        var (closed, _) = await DetailAsync(operario.Client, id);
        Assert.Equal(shipment, Line(closed, seed.Article, seed.Location).GetProperty("correctsShipmentId").GetGuid());
    }

    [Fact]
    public async Task C20_links_to_unconfirmed_missing_or_unrelated_shipments_or_without_difference_are_rejected()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await ShippingApiSupport.SeedAsync(support, operario, 10m, "10");
        var u2 = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, u2, 5m);
        var (b, _) = await support.ArticleAsync();
        await support.ReceiveAsync(b, seed.Location, 3m);
        // Confirmada desde otra ubicación; borrador desde la misma.
        var (elsewhere, _) = await ShippingApiSupport.CreateDraftAsync(operario, ShippingApiSupport.Body(seed.Order,
            DateTimeOffset.UtcNow.AddHours(-1), (seed.OrderLine, u2, seed.Lot, "2")));
        using (var confirmed = await ShippingApiSupport.ConfirmAsync(operario, elsewhere, seed.Order))
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
        var (draft, _) = await ShippingApiSupport.CreateDraftAsync(operario, ShippingApiSupport.Body(seed, "1"));

        var (id, detail, etag) = await AwaitingAsync(operario, seed.Location, (seed.Article, "9"), (b, "3"));
        var line = Line(detail, seed.Article, seed.Location).GetProperty("id").GetGuid();
        var equal = Line(detail, b, seed.Location).GetProperty("id").GetGuid();

        foreach (var (target, why) in new[] { (draft, "borrador"), (Guid.NewGuid(), "inexistente"), (elsewhere, "otro bucket") })
            using (var rejected = await LinkAsync(responsable, id, etag, line, "Motivo", target))
                Assert.True(ErrorFields(await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                    .Contains("lines[0].correctsShipmentId"), why);
        using (var noDifference = await LinkAsync(responsable, id, etag, equal, null, elsewhere))
            Assert.Contains("lines[0].correctsShipmentId", ErrorFields(await StockApiSupport.ProblemAsync(noDifference,
                HttpStatusCode.UnprocessableEntity, "ValidationFailed")));
        Assert.Equal(etag, (await DetailAsync(operario.Client, id)).ETag);

        // Candidatas: ni el borrador ni la salida de otra ubicación; línea inexistente → 404.
        Assert.Empty((await CandidatesAsync(responsable, id, line)).GetProperty("items").EnumerateArray());
        using (var missing = await responsable.Client.GetAsync($"{Counts}/{id:D}/lines/{Guid.NewGuid():D}/shipment-candidates"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var page = await responsable.Client.GetAsync($"{Counts}/{id:D}/lines/{line:D}/shipment-candidates?page=0"))
            await StockApiSupport.ProblemAsync(page, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
        await using var database = fixture.ApplicationDatabase();
        Assert.Equal(ShipmentState.Draft, (await database.Set<Shipment>().SingleAsync(value => value.Id == draft)).State);
    }

    private static Task<HttpResponseMessage> LinkAsync(MasterDataApi session, Guid id, string etag, Guid line, string? reason, Guid shipment) =>
        session.WriteAsync(HttpMethod.Put, $"{Counts}/{id:D}/review",
            new { lines = new[] { new { lineId = line, reason, correctsShipmentId = shipment } } }, version: etag);

    private static async Task<JsonElement> CandidatesAsync(MasterDataApi session, Guid id, Guid line) =>
        (await session.Client.GetFromJsonAsync<JsonElement>($"{Counts}/{id:D}/lines/{line:D}/shipment-candidates")).Clone();

    /// <summary>Salida (versión y líneas), servido y estado del pedido, y asignaciones de reserva.</summary>
    private async Task<string> SnapshotAsync(ShippingSeed seed, Guid shipment)
    {
        await using var database = fixture.ApplicationDatabase();
        var stored = await database.Set<Shipment>().AsNoTracking().Include(value => value.Lines).SingleAsync(value => value.Id == shipment);
        var order = await database.Set<CustomerOrder>().AsNoTracking().Include(value => value.Lines).SingleAsync(value => value.Id == seed.Order);
        var allocations = await database.Set<ReservationAllocation>().AsNoTracking().Where(value => value.ArticleId == seed.Article)
            .OrderBy(value => value.Id).Select(value => new { value.Id, value.AllocatedQuantity, value.ConsumedQuantity, value.ReleasedQuantity })
            .ToListAsync();
        var consumptions = await database.Set<ShipmentReservationConsumption>().AsNoTracking()
            .Where(value => stored.Lines.Select(line => line.Id).Contains(value.ShipmentLineId)).OrderBy(value => value.Id)
            .Select(value => new { value.Id, value.Quantity }).ToListAsync();
        return JsonSerializer.Serialize(new
        {
            version = Convert.ToBase64String(stored.Version), stored.State, lines = stored.Lines.Select(line => line.Quantity),
            orderState = order.State, shipped = order.Lines.Select(line => line.ShippedQuantity), allocations, consumptions
        });
    }
}
