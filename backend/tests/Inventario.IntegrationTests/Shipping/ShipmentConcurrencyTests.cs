using System.Net;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Shipping.ShippingApiSupport;

namespace Inventario.IntegrationTests.Shipping;

// HU-03 T011 (H-16, H-17; FR-022, SC-003; US3.3, US5.4–US5.6; INT-HU03-01/02): salidas simultáneas
// con conexiones independientes. V-05 extendido con 20 rondas sin barrera y carreras con LockBarrier
// en ambos órdenes frente a otra salida, reservar, liberar y los escritores del artículo de A.
public sealed class ShipmentConcurrencyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    // H-16 (V-05 extendido): dos salidas de 8 de pedidos distintos sobre físico 10, 20 rondas.
    [Fact]
    public async Task H16_two_shipments_of_8_over_physical_10_only_one_confirms_in_every_round()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var first = await support.SessionAsync("Operario");
        var second = await support.SessionAsync("Operario");
        for (var round = 0; round < 20; round++)
        {
            var (article, _) = await support.ArticleAsync();
            var location = await support.LocationAsync("V" + StockApiSupport.Unique(8));
            await support.ReceiveAsync(article, location, 10m);
            var lot = await UntrackedLotAsync(fixture, article);
            var (a, lineA, _) = await StockApiSupport.CreateOrderAsync(first, article, "8");
            var (b, lineB, _) = await StockApiSupport.CreateOrderAsync(first, article, "8");
            var past = DateTimeOffset.UtcNow.AddHours(-1);
            var (shipA, tagA) = await CreateDraftAsync(first, Body(a, past, (lineA, location, lot, "8")));
            var (shipB, tagB) = await CreateDraftAsync(second, Body(b, past, (lineB, location, lot, "8")));
            var (_, versionA) = await StockApiSupport.OrderAsync(first.Client, a);
            var (_, versionB) = await StockApiSupport.OrderAsync(second.Client, b);

            var responses = await Task.WhenAll(ConfirmAsync(first, shipA, a, tagA, versionA), ConfirmAsync(second, shipB, b, tagB, versionB));
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(response => response.StatusCode).Order());
            await StockApiSupport.ProblemAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                HttpStatusCode.Conflict, "InsufficientStock");
            foreach (var response in responses) response.Dispose();
            Assert.Equal(2m, (await support.Stock.BalanceAsync(article, location))!.Physical);
            await support.Stock.AssertJournalMatchesBalanceAsync(article, location);
        }
    }

    public enum Rival { SameOrderShipment, Reserve, Release, ArticleDeactivate, ArticleDefinition, ArticleDelete }

    // H-17: carreras con LockBarrier y conexiones independientes, en ambos órdenes.
    [Theory]
    [InlineData(Rival.SameOrderShipment, true)]
    [InlineData(Rival.SameOrderShipment, false)]
    [InlineData(Rival.Reserve, true)]
    [InlineData(Rival.Reserve, false)]
    [InlineData(Rival.Release, true)]
    [InlineData(Rival.Release, false)]
    [InlineData(Rival.ArticleDeactivate, true)]
    [InlineData(Rival.ArticleDeactivate, false)]
    [InlineData(Rival.ArticleDefinition, true)]
    [InlineData(Rival.ArticleDefinition, false)]
    [InlineData(Rival.ArticleDelete, true)]
    [InlineData(Rival.ArticleDelete, false)]
    public async Task H17_a_confirmation_and_a_rival_writer_serialize_in_both_orders(Rival rival, bool confirmFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var gestor = await support.SessionAsync("GestorMaestros");
        var seed = await SeedAsync(support, operario, physical: 10m, ordered: "6");
        var reservation = Guid.Empty;
        if (rival == Rival.Release) reservation = await ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "4");
        var (id, etag) = await CreateDraftAsync(operario, Body(seed, "4"));
        var (_, orderVersion) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);

        Func<Task<HttpResponseMessage>> other;
        switch (rival)
        {
            case Rival.SameOrderShipment:
                var (otherShipment, otherTag) = await CreateDraftAsync(operario, Body(seed, "4"));
                other = () => ConfirmAsync(operario, otherShipment, seed.Order, otherTag, orderVersion);
                barrier.Hold("CustomerOrders", seed.Order);
                break;
            case Rival.Reserve:
                other = () => StockApiSupport.PostReservationAsync(responsable, seed.OrderLine, orderVersion, null, (seed.Location, seed.Lot, "2"));
                barrier.Hold("CustomerOrders", seed.Order);
                break;
            case Rival.Release:
                var (view, reservationTag) = await StockApiSupport.ReservationAsync(responsable.Client, reservation);
                var allocation = view.GetProperty("allocations")[0].GetProperty("id").GetGuid();
                other = () => StockApiSupport.PostReleaseAsync(responsable, reservation, reservationTag, null, Guid.NewGuid(),
                    "Liberación simultánea", (allocation, "2"));
                barrier.Hold("CustomerOrders", seed.Order);
                break;
            default:
                var writer = rival switch
                {
                    Rival.ArticleDeactivate => StockWriter.ArticleDeactivate,
                    Rival.ArticleDefinition => StockWriter.ArticleDefinition,
                    _ => StockWriter.ArticleDelete
                };
                other = await StockApiSupport.StockWriterAsync(writer, seed.Article, seed.Sku, Guid.Empty, gestor, operario);
                barrier.Hold("Articles", seed.Article);
                break;
        }

        Task<HttpResponseMessage> Confirm() => ConfirmAsync(operario, id, seed.Order, etag, orderVersion);
        var (confirmed, changed) = confirmFirst
            ? await barrier.RaceAsync(fixture, Confirm, other)
            : Swap(await barrier.RaceAsync(fixture, other, Confirm));
        using var _a = confirmed;
        using var _b = changed;

        switch (rival)
        {
            case Rival.SameOrderShipment:
                // Juntas superan el pendiente (8 > 6): la perdedora ve el pedido cambiado (412).
                Assert.Equal(HttpStatusCode.OK, confirmFirst ? confirmed.StatusCode : changed.StatusCode);
                await StockApiSupport.ProblemAsync(confirmFirst ? changed : confirmed, HttpStatusCode.PreconditionFailed, "VersionConflict");
                break;
            case Rival.Reserve:
            case Rival.Release:
                // Quien llega segundo trae una versión anterior del pedido o de la reserva: 412.
                Assert.Equal(confirmFirst ? HttpStatusCode.OK : rival == Rival.Reserve ? HttpStatusCode.Created : HttpStatusCode.OK,
                    confirmFirst ? confirmed.StatusCode : changed.StatusCode);
                await StockApiSupport.ProblemAsync(confirmFirst ? changed : confirmed, HttpStatusCode.PreconditionFailed, "VersionConflict");
                break;
            case Rival.ArticleDeactivate:
                // INT-HU03-02: ambas terminan; un artículo inactivo se sigue sirviendo.
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
                break;
            case Rival.ArticleDefinition:
                Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
                await StockApiSupport.ProblemAsync(changed, HttpStatusCode.Conflict, "ArticleDefinitionLocked");
                break;
            default:
                Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
                await StockApiSupport.ProblemAsync(changed, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
                break;
        }

        // Nunca servido > pedido, reservado > físico, consumido + liberado > asignado ni físico < 0.
        await using var database = fixture.ApplicationDatabase();
        var line = await database.Set<CustomerOrderLine>().AsNoTracking().SingleAsync(value => value.Id == seed.OrderLine);
        Assert.InRange(line.ShippedQuantity, 0m, line.OrderedQuantity);
        var balance = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!;
        Assert.InRange(balance.Physical, 0m, 10m);
        Assert.InRange(balance.Reserved, 0m, balance.Physical);
        Assert.All(await database.Set<ReservationAllocation>().AsNoTracking()
                .Where(value => value.ArticleId == seed.Article).ToListAsync(),
            allocation => Assert.True(allocation.ConsumedQuantity + allocation.ReleasedQuantity <= allocation.AllocatedQuantity));
        var article = await database.Articles.AsNoTracking().SingleAsync(value => value.Id == seed.Article);
        Assert.Equal("UD", article.BaseUnit);
        await support.AssertCommitmentsMatchBalancesAsync(seed.Article, seed.Location);
        await support.Stock.AssertJournalMatchesBalanceAsync(seed.Article, seed.Location);
        if (confirmed.StatusCode == HttpStatusCode.OK) await AssertShipmentInvariantsAsync(support, id);
    }

    // Regla 1 del contrato HU-03: solo customer-order:{id} serializa una salida del artículo X con una
    // reserva del artículo Y del mismo pedido (otro bucket y otro artículo, sin bloqueos comunes).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task H17_a_confirmation_and_a_reservation_of_another_line_of_the_same_order_serialize_on_the_order(bool confirmFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var (x, _) = await support.ArticleAsync();
        var (y, _) = await support.ArticleAsync();
        var n01 = await support.LocationAsync("N01-" + StockApiSupport.Unique(6));
        var n02 = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await support.ReceiveAsync(x, n01, 5m);
        await support.ReceiveAsync(y, n02, 5m);
        var (order, lines) = await ShippingApiSupport.CreateOrderAsync(operario, (x, "3"), (y, "3"));
        var (id, etag) = await CreateDraftAsync(operario, Body(order, DateTimeOffset.UtcNow.AddHours(-1),
            (lines[x], n01, await UntrackedLotAsync(fixture, x), "3")));
        var lotY = await UntrackedLotAsync(fixture, y);
        var (_, orderVersion) = await StockApiSupport.OrderAsync(operario.Client, order);

        Task<HttpResponseMessage> Confirm() => ConfirmAsync(operario, id, order, etag, orderVersion);
        Task<HttpResponseMessage> Reserve() => StockApiSupport.PostReservationAsync(responsable, lines[y], orderVersion, null, (n02, lotY, "2"));
        barrier.Hold("CustomerOrders", order);
        var (confirmed, reserved) = confirmFirst
            ? await barrier.RaceAsync(fixture, Confirm, Reserve)
            : Swap(await barrier.RaceAsync(fixture, Reserve, Confirm));
        using var _a = confirmed;
        using var _b = reserved;

        if (confirmFirst)
        {
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            await StockApiSupport.ProblemAsync(reserved, HttpStatusCode.PreconditionFailed, "VersionConflict");
        }
        else
        {
            Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
            await StockApiSupport.ProblemAsync(confirmed, HttpStatusCode.PreconditionFailed, "VersionConflict");
        }
    }

    private static (T1, T2) Swap<T2, T1>((T2 First, T1 Second) pair) => (pair.Second, pair.First);
}
