using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
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

    // --- T010 ---

    // H-09: parcial y dos orígenes; después el resto.
    [Fact]
    public async Task H09_partial_from_two_origins_keeps_the_order_open_until_the_rest_is_shipped()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "10");
        var n02 = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, n02, 5m);
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var (first, _) = await CreateDraftAsync(operario, Body(seed.Order, past,
            (seed.OrderLine, seed.Location, seed.Lot, "4"), (seed.OrderLine, n02, seed.Lot, "2")));

        using (var confirmed = await ConfirmAsync(operario, first, seed.Order))
            Assert.Equal("Open", (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderState").GetString());
        Assert.Equal(6m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
        Assert.Equal(3m, (await support.Stock.BalanceAsync(seed.Article, n02))!.Physical);
        var (order, _) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        Assert.Equal("4.000", order.GetProperty("lines")[0].GetProperty("pending").GetString());
        await AssertShipmentInvariantsAsync(support, first);

        var (second, _) = await CreateDraftAsync(operario, Body(seed, "4"));
        using (var confirmed = await ConfirmAsync(operario, second, seed.Order))
            Assert.Equal("Fulfilled", (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderState").GetString());
        await AssertShipmentInvariantsAsync(support, second);
    }

    // H-10 (V-04), continuación: lo que queda solo admite el posible, nunca la reserva ajena.
    [Fact]
    public async Task H10_afterwards_neither_order_can_ship_beyond_its_possible()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "10");
        var (orderB, lineB, _) = await StockApiSupport.CreateOrderAsync(operario, seed.Article, "5");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        await ReserveAsync(responsable, orderB, lineB, seed.Location, seed.Lot, "3");
        var (first, _) = await CreateDraftAsync(operario, Body(seed, "6"));
        using (var confirmed = await ConfirmAsync(operario, first, seed.Order))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        // A: físico 4, reservado 3 ajeno, sin reserva propia → posible 1.
        var (moreA, _) = await CreateDraftAsync(operario, Body(seed, "4"));
        using (var rejected = await ConfirmAsync(operario, moreA, seed.Order))
        {
            var problem = await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "InsufficientStock");
            Assert.Contains("disponible 1.000", problem.GetProperty("detail").GetString());
        }
        // B: propia 3 + disponible 1 → posible 4; 5 se rechaza.
        var (moreB, _) = await CreateDraftAsync(operario, Body(orderB, DateTimeOffset.UtcNow.AddHours(-1), (lineB, seed.Location, seed.Lot, "5")));
        using (var rejected = await ConfirmAsync(operario, moreB, orderB))
            await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "InsufficientStock");
        var balance = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!;
        Assert.Equal(4m, balance.Physical);
        Assert.Equal(3m, balance.Reserved);
    }

    // H-11 (regla 5): servir desde otro bucket no libera en silencio la reserva sobrante.
    [Fact]
    public async Task H11_a_leftover_reservation_is_rejected_until_it_is_released()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "4");
        var n02 = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, n02, 5m);
        var reservation = await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        var (id, etag) = await CreateDraftAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            (seed.OrderLine, n02, seed.Lot, "4")));
        var (_, orderVersion) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);

        using (var rejected = await ConfirmAsync(operario, id, seed.Order, etag, orderVersion))
        {
            var problem = await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "ReservedStockConflict");
            Assert.Contains("liberar 4.000", problem.GetProperty("detail").GetString());
        }
        Assert.Equal(10m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
        Assert.Equal(orderVersion, (await StockApiSupport.OrderAsync(operario.Client, seed.Order)).ETag);

        var (view, reservationTag) = await StockApiSupport.ReservationAsync(responsable.Client, reservation);
        var allocation = view.GetProperty("allocations")[0].GetProperty("id").GetGuid();
        using (var released = await StockApiSupport.PostReleaseAsync(responsable, reservation, reservationTag, null, Guid.NewGuid(),
                   "El cliente recoge desde otra ubicación", (allocation, "4")))
            Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        await AssertShipmentInvariantsAsync(support, id);
    }

    // Regla 3: dos reservas de la misma línea en el mismo bucket, consumidas por Id de asignación.
    [Fact]
    public async Task Rule3_consumes_allocations_in_id_order_and_derives_each_reservation_state()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var r1 = await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "2");
        var r2 = await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "2");
        var allocations = new List<(Guid Reservation, Guid Allocation)>();
        foreach (var reservation in new[] { r1, r2 })
            allocations.Add((reservation, (await StockApiSupport.ReservationAsync(operario.Client, reservation)).Body
                .GetProperty("allocations")[0].GetProperty("id").GetGuid()));
        var ordered = allocations.OrderBy(value => value.Allocation).ToList();
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "3"));

        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
            Assert.Equal("3.000", (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines")[0]
                .GetProperty("ownReservationConsumed").GetString());

        var (firstView, _) = await StockApiSupport.ReservationAsync(operario.Client, ordered[0].Reservation);
        var (secondView, _) = await StockApiSupport.ReservationAsync(operario.Client, ordered[1].Reservation);
        Assert.Equal("Consumed", firstView.GetProperty("state").GetString());
        Assert.Equal("2.000", firstView.GetProperty("allocations")[0].GetProperty("consumed").GetString());
        Assert.Equal("Active", secondView.GetProperty("state").GetString());
        Assert.Equal("1.000", secondView.GetProperty("allocations")[0].GetProperty("consumed").GetString());
        var (detail, _) = await DetailAsync(operario.Client, id);
        var consumptions = detail.GetProperty("lines")[0].GetProperty("consumptions").EnumerateArray().ToList();
        Assert.Equal(2, consumptions.Count);
        Assert.Equal(new[] { "2.000", "1.000" }, ordered.Select(value => consumptions
            .Single(item => item.GetProperty("reservationAllocationId").GetGuid() == value.Allocation).GetProperty("quantity").GetString()));
        Assert.Equal(1m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Reserved);
        await AssertShipmentInvariantsAsync(support, id);

        // ConsumedQuantity solo crece: otra salida consume el resto sin tocar lo ya consumido.
        var (rest, _) = await CreateDraftAsync(operario, Body(seed, "1"));
        using (var confirmed = await ConfirmAsync(operario, rest, seed.Order))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Consumed", (await StockApiSupport.ReservationAsync(operario.Client, ordered[1].Reservation)).Body.GetProperty("state").GetString());
        Assert.Equal("2.000", (await StockApiSupport.ReservationAsync(operario.Client, ordered[0].Reservation)).Body
            .GetProperty("allocations")[0].GetProperty("consumed").GetString());
        await AssertShipmentInvariantsAsync(support, rest);
    }

    // H-12 (regla 4, FR-012, SC-005): versiones renovadas; los escritores de HU-05 con datos viejos, 412.
    [Fact]
    public async Task H12_every_shipment_renews_the_order_and_each_consumed_reservation()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "8");
        var n02 = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, n02, 5m);
        var consumed = await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "2");
        var untouched = await ReserveAsync(responsable, seed.Order, seed.OrderLine, n02, seed.Lot, "1");

        // Salida con reserva: cambia el pedido y la reserva consumida, no la otra.
        var (orderBefore, orderTag) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        var (_, consumedTag) = await StockApiSupport.ReservationAsync(operario.Client, consumed);
        var (untouchedView, untouchedTag) = await StockApiSupport.ReservationAsync(operario.Client, untouched);
        var (withReservation, _) = await CreateDraftAsync(operario, Body(seed, "2"));
        using (var confirmed = await ConfirmAsync(operario, withReservation, seed.Order, orderVersion: orderTag))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var (_, afterFirst) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        Assert.NotEqual(orderTag, afterFirst);
        Assert.NotEqual(consumedTag, (await StockApiSupport.ReservationAsync(operario.Client, consumed)).ETag);
        Assert.Equal(untouchedTag, (await StockApiSupport.ReservationAsync(operario.Client, untouched)).ETag);

        // Reservar y liberar con los ETag anteriores a la salida: 412.
        using (var stale = await StockApiSupport.PostReservationAsync(responsable, seed.OrderLine, orderTag, null, (seed.Location, seed.Lot, "1")))
            await StockApiSupport.ProblemAsync(stale, HttpStatusCode.PreconditionFailed, "VersionConflict");
        using (var stale = await StockApiSupport.PostReleaseAsync(responsable, consumed, consumedTag, null, Guid.NewGuid(), "Motivo",
                   (Guid.NewGuid(), "1")))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // Salida sin reserva (desde un bucket sin reserva propia): el pedido también cambia.
        var n03 = await support.LocationAsync("N03-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, n03, 3m);
        var (withoutReservation, shipmentTag) = await CreateDraftAsync(operario, Body(seed.Order, DateTimeOffset.UtcNow.AddHours(-1),
            (seed.OrderLine, n03, seed.Lot, "1")));
        // Confirmar con una orderVersion vieja: 412 y nada cambia.
        using (var stale = await ConfirmAsync(operario, withoutReservation, seed.Order, shipmentTag, orderTag))
            await StockApiSupport.ProblemAsync(stale, HttpStatusCode.PreconditionFailed, "VersionConflict");
        using (var confirmed = await ConfirmAsync(operario, withoutReservation, seed.Order, shipmentTag, afterFirst))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.NotEqual(afterFirst, (await StockApiSupport.OrderAsync(operario.Client, seed.Order)).ETag);
        Assert.Equal(untouchedTag, (await StockApiSupport.ReservationAsync(operario.Client, untouched)).ETag);
        Assert.Equal("1.000", untouchedView.GetProperty("allocations")[0].GetProperty("remaining").GetString());
        Assert.Equal("8.000", orderBefore.GetProperty("lines")[0].GetProperty("pending").GetString());
        await AssertShipmentInvariantsAsync(support, withoutReservation);
    }

    // H-13 (S-18 completo): el lector del pedido dentro del ejecutor, antes y después de servir.
    [Fact]
    public async Task H13_the_order_reader_matches_the_order_detail_before_and_after_shipping()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "8");
        var n02 = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(seed.Article, n02, 5m);
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "2");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "1");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, n02, seed.Lot, "2");

        await AssertReaderMatchesDetailAsync(support, operario, seed.Order);
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "2"));
        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var after = await AssertReaderMatchesDetailAsync(support, operario, seed.Order);
        var line = Assert.Single(after.Lines);
        Assert.Equal(6m, line.Pending);
        // Consumo por Id ascendente: de las dos del bucket N01 queda 1 en total.
        Assert.Equal(1m, line.ActiveAllocations.Where(value => value.LocationId == seed.Location).Sum(value => value.Remaining));
        Assert.Equal(2m, line.ActiveAllocations.Where(value => value.LocationId == n02).Sum(value => value.Remaining));
    }

    // H-15 (INT-HU03-02): servir un compromiso de un artículo desactivado después.
    [Fact]
    public async Task H15_an_article_deactivated_after_the_order_is_still_shipped_with_the_same_limits()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var gestor = await support.SessionAsync("GestorMaestros");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        await StockApiSupport.DeactivateArticleAsync(gestor, seed.Article, seed.Sku);

        // Preparar con el artículo ya inactivo también se admite (FR-014).
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "6"));
        // Los mismos límites: no puede superar el pendiente.
        var (tooMuch, _) = await CreateDraftAsync(operario, Body(seed, "6"));
        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using (var rejected = await ConfirmAsync(operario, tooMuch, seed.Order))
            await StockApiSupport.ProblemAsync(rejected, HttpStatusCode.Conflict, "CustomerOrderNotOpen");
        Assert.Equal(4m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
        await AssertShipmentInvariantsAsync(support, id);

        // El alta de un pedido nuevo del artículo inactivo sigue rechazándose (HU-05).
        using var created = await operario.WriteAsync(HttpMethod.Post, StockApiSupport.Orders, new
        {
            externalReference = "PED-" + StockApiSupport.Unique(8), customerReference = "Cliente",
            lines = new[] { new { articleId = seed.Article, quantity = "1" } }
        });
        await StockApiSupport.ProblemAsync(created, HttpStatusCode.Conflict, "ArticleInactive");
    }

    // H-18 (V-07, FR-021): repetir la intención, cambiar su contenido u otra intención sobre una confirmada.
    [Fact]
    public async Task H18_a_confirmation_replays_rejects_another_content_and_cannot_be_repeated()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var (id, etag) = await CreateDraftAsync(operario, Body(seed, "2"));
        var (_, orderVersion) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);
        var key = Guid.NewGuid();

        using var first = await ConfirmAsync(operario, id, seed.Order, etag, orderVersion, key);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var replay = await ConfirmAsync(operario, id, seed.Order, etag, orderVersion, key))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(text, await replay.Content.ReadAsStringAsync());
        }
        using (var conflict = await ConfirmAsync(operario, id, seed.Order, etag, "\"AAAAAAAAAAA=\"", key))
            await StockApiSupport.ProblemAsync(conflict, HttpStatusCode.Conflict, "IdempotencyConflict");
        using (var again = await ConfirmAsync(operario, id, seed.Order, key: Guid.NewGuid()))
            await StockApiSupport.ProblemAsync(again, HttpStatusCode.Conflict, "DocumentAlreadyConfirmed");

        // Tras confirmar, el borrador ya no se edita ni se elimina (H-06, FR-013).
        var (_, current) = await DetailAsync(operario.Client, id);
        using (var edit = await operario.WriteAsync(HttpMethod.Put, $"{Shipments}/{id:D}", Body(seed, "1"), version: current))
            await StockApiSupport.ProblemAsync(edit, HttpStatusCode.Conflict, "DocumentAlreadyConfirmed");
        using (var delete = await operario.WriteAsync(HttpMethod.Delete, $"{Shipments}/{id:D}", new { }, version: current))
            await StockApiSupport.ProblemAsync(delete, HttpStatusCode.Conflict, "DocumentAlreadyConfirmed");

        await using var database = fixture.ApplicationDatabase();
        Assert.Equal(1, await database.Set<StockMovement>().CountAsync(value => value.SourceDocumentId == id));
        Assert.Equal(8m, (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical);
    }

    // H-20 (US5.1, FR-019): confirmar exige Shipping.Record y no Reservations.Manage.
    [Fact]
    public async Task H20_confirming_needs_shipping_record_but_not_reservations_manage()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        var (id, _) = await CreateDraftAsync(operario, Body(seed, "4"));

        foreach (var role in new[] { "GestorMaestros", "Comprador", "ResponsableAlmacen" })
        {
            var session = await support.SessionAsync(role);
            using var forbidden = await ConfirmAsync(session, id, seed.Order);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        // El Operario no tiene Reservations.Manage y consume la reserva al confirmar.
        using (var confirmed = await ConfirmAsync(operario, id, seed.Order))
            Assert.Equal("4.000", (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines")[0]
                .GetProperty("ownReservationConsumed").GetString());
        await AssertShipmentInvariantsAsync(support, id);
    }

    private static async Task<CustomerOrderSnapshot> AssertReaderMatchesDetailAsync(StockApiSupport support, MasterDataApi session, Guid order)
    {
        var (detail, current) = await StockApiSupport.OrderAsync(session.Client, order);
        var (receiving, _) = await support.ArticleAsync();
        var (_, other) = await support.Stock.SeedAsync();
        var (_, principal) = await support.Stock.ActorAsync();
        CustomerOrderSnapshot? snapshot = null;
        await support.Stock.RunAsync(principal, Guid.NewGuid(),
            StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(receiving, other.Id, null, 1m)]),
            async (context, token) =>
            {
                await context.Locks.DocumentsAsync([new DocumentLock(StockDocuments.CustomerOrder, order)], token);
                snapshot = await context.Services.GetRequiredService<ICustomerOrderReader>().ReadAsync(context, order, token);
            });
        Assert.NotNull(snapshot);
        Assert.Equal(current, EntityTags.Format(snapshot.Version));
        var line = Assert.Single(snapshot.Lines);
        var view = detail.GetProperty("lines")[0];
        Assert.Equal(view.GetProperty("pending").GetString(), ShipmentQuantity(line.Pending));
        Assert.Equal(view.GetProperty("shipped").GetString(), ShipmentQuantity(line.Shipped));
        var fromView = view.GetProperty("reservations").EnumerateArray()
            .Where(reservation => reservation.GetProperty("state").GetString() == "Active")
            .SelectMany(reservation => reservation.GetProperty("allocations").EnumerateArray())
            .Where(allocation => allocation.GetProperty("remaining").GetString() != "0.000")
            .ToDictionary(allocation => allocation.GetProperty("id").GetGuid(), allocation => allocation.GetProperty("remaining").GetString());
        Assert.Equal(fromView, line.ActiveAllocations.ToDictionary(value => value.AllocationId, value => (string?)ShipmentQuantity(value.Remaining)));
        return snapshot;
    }

    private static string ShipmentQuantity(decimal value) => value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
}
