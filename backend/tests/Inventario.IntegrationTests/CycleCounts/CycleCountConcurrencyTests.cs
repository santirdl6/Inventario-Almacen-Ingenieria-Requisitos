using System.Net;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Shipping;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.CycleCounts.CycleCountApiSupport;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T010 (C-13, C-15; FR-017, FR-024, SC-003; research D-03, D-04, D-08; INT-HU04-01/03; H4 de
// analyze-1). Carreras con conexiones independientes. Con LockBarrier, en ambos órdenes: iniciar frente
// a confirmar una salida, reservar y desactivar la ubicación; confirmar frente a liberar; añadir y
// retirar un hallazgo frente a editar la definición y borrar el artículo. Sin barrera, 20 rondas de
// iniciar frente a confirmar una salida. Siempre uno completo primero y teórico = físico tras el ganador.
public sealed class CycleCountConcurrencyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    public enum StartRival { ShipmentConfirm, Reserve, LocationDeactivate }

    [Theory]
    [InlineData(StartRival.ShipmentConfirm, true)]
    [InlineData(StartRival.ShipmentConfirm, false)]
    [InlineData(StartRival.Reserve, true)]
    [InlineData(StartRival.Reserve, false)]
    [InlineData(StartRival.LocationDeactivate, true)]
    [InlineData(StartRival.LocationDeactivate, false)]
    public async Task C13_starting_and_a_location_writer_serialize_in_both_orders(StartRival rival, bool startFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await ShippingApiSupport.SeedAsync(support, operario, 10m, "6");
        var empty = await support.LocationAsync("N09-" + StockApiSupport.Unique(6));
        var scope = rival == StartRival.LocationDeactivate ? empty : seed.Location;
        var (count, countTag) = await CreateAsync(operario, scope);
        var (_, orderVersion) = await StockApiSupport.OrderAsync(operario.Client, seed.Order);

        Func<Task<HttpResponseMessage>> other;
        switch (rival)
        {
            case StartRival.ShipmentConfirm:
                var (shipment, shipmentTag) = await ShippingApiSupport.CreateDraftAsync(operario, ShippingApiSupport.Body(seed, "4"));
                other = () => ShippingApiSupport.ConfirmAsync(operario, shipment, seed.Order, shipmentTag, orderVersion);
                // La salida lee el pedido ya con todos sus bloqueos; el inicio, la ubicación tras location:{id}.
                if (startFirst) barrier.Hold("Locations", scope);
                else barrier.Hold("CustomerOrders", seed.Order);
                break;
            case StartRival.Reserve:
                other = () => StockApiSupport.PostReservationAsync(responsable, seed.OrderLine, orderVersion, null, (seed.Location, seed.Lot, "2"));
                barrier.Hold("Locations", scope);
                break;
            default:
                var deactivate = await StockApiSupport.StockWriterAsync(StockWriter.LocationDeactivate, Guid.Empty, "", empty, operario, operario);
                other = deactivate;
                barrier.Hold("Locations", scope);
                break;
        }

        Task<HttpResponseMessage> Start() => StartAsync(operario, count, countTag);
        var (started, changed) = startFirst
            ? await barrier.RaceAsync(fixture, Start, other)
            : Swap(await barrier.RaceAsync(fixture, other, Start));
        using var _a = started;
        using var _b = changed;

        switch (rival)
        {
            case StartRival.ShipmentConfirm:
            case StartRival.Reserve:
                Assert.True(HttpStatusCode.OK == started.StatusCode, await started.Content.ReadAsStringAsync());
                if (startFirst) await StockApiSupport.ProblemAsync(changed, HttpStatusCode.Conflict, "CountScopeFrozen");
                else Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
                // Teórico = físico tras la operación ganadora: 10, o 6 si la salida confirmó antes.
                var physical = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical;
                Assert.Equal(rival == StartRival.ShipmentConfirm && !startFirst ? 6m : 10m, physical);
                var (detail, _) = await DetailAsync(operario.Client, count);
                Assert.Equal(CycleCountView(physical), Line(detail, seed.Article, seed.Location).GetProperty("theoreticalPhysical").GetString());
                await support.AssertCommitmentsMatchBalancesAsync(seed.Article, seed.Location);
                break;
            default:
                if (startFirst)
                {
                    Assert.True(HttpStatusCode.OK == started.StatusCode, await started.Content.ReadAsStringAsync());
                    // El ETag del detalle de ubicación incluye inOpenCount (HU-02): quien leyó antes de
                    // iniciar trae una vista obsoleta (412). Con la versión actual, CountScopeFrozen.
                    await StockApiSupport.ProblemAsync(changed, HttpStatusCode.PreconditionFailed, "VersionConflict");
                    using var retry = await (await StockApiSupport.StockWriterAsync(StockWriter.LocationDeactivate, Guid.Empty, "", empty,
                        operario, operario))();
                    await StockApiSupport.ProblemAsync(retry, HttpStatusCode.Conflict, "CountScopeFrozen");
                }
                else
                {
                    Assert.True(HttpStatusCode.OK == changed.StatusCode, await changed.Content.ReadAsStringAsync());
                    await StockApiSupport.ProblemAsync(started, HttpStatusCode.UnprocessableEntity, "ValidationFailed");
                }
                break;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task C13_confirming_and_releasing_a_reservation_serialize_in_both_orders(bool confirmFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await ShippingApiSupport.SeedAsync(support, operario, 5m, "6");
        var reservation = await ShippingApiSupport.ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "3");
        var (count, detail, etag) = await AwaitingAsync(operario, seed.Location, (seed.Article, "2"));
        etag = await OkAsync(ReviewAsync(responsable, count, etag, (Line(detail, seed.Article, seed.Location).GetProperty("id").GetGuid(), "Merma")));
        var (view, reservationTag) = await StockApiSupport.ReservationAsync(responsable.Client, reservation);
        var allocation = view.GetProperty("allocations")[0].GetProperty("id").GetGuid();

        Task<HttpResponseMessage> Confirm() => ConfirmAsync(responsable, count, etag);
        Task<HttpResponseMessage> Release() => StockApiSupport.PostReleaseAsync(responsable, reservation, reservationTag, null, Guid.NewGuid(),
            "Merma del conteo", (allocation, "1"));
        // Ambos leen la ubicación ya con location:{id} (la liberación, dentro de StockCore).
        barrier.Hold("Locations", seed.Location);
        var (confirmed, released) = confirmFirst
            ? await barrier.RaceAsync(fixture, Confirm, Release)
            : Swap(await barrier.RaceAsync(fixture, Release, Confirm));
        using var _a = confirmed;
        using var _b = released;

        Assert.True(HttpStatusCode.OK == released.StatusCode, await released.Content.ReadAsStringAsync());
        var balance = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!;
        if (confirmFirst)
        {
            // Contado 2 < reservado 3 al confirmar: el conteo sigue abierto y congelado.
            await StockApiSupport.ProblemAsync(confirmed, HttpStatusCode.Conflict, "ReservedStockConflict");
            Assert.Equal((5m, 2m), (balance.Physical, balance.Reserved));
            var (pending, _) = await DetailAsync(operario.Client, count);
            Assert.Equal("AwaitingApproval", pending.GetProperty("state").GetString());
            Assert.All(pending.GetProperty("locations").EnumerateArray(), location => Assert.True(location.GetProperty("isFrozen").GetBoolean()));
        }
        else
        {
            Assert.True(HttpStatusCode.OK == confirmed.StatusCode, await confirmed.Content.ReadAsStringAsync());
            Assert.Equal((2m, 2m), (balance.Physical, balance.Reserved));
            await AssertConfirmedInvariantsAsync(fixture, support, count);
        }
        await support.AssertCommitmentsMatchBalancesAsync(seed.Article, seed.Location);
    }

    [Fact]
    public async Task C13_starting_and_confirming_a_shipment_without_barrier_never_lose_a_movement_in_20_rounds()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var shipper = await support.SessionAsync("Operario");
        for (var round = 0; round < 20; round++)
        {
            var seed = await ShippingApiSupport.SeedAsync(support, operario, 10m, "6");
            var (count, countTag) = await CreateAsync(operario, seed.Location);
            var (shipment, shipmentTag) = await ShippingApiSupport.CreateDraftAsync(shipper, ShippingApiSupport.Body(seed, "4"));
            var (_, orderVersion) = await StockApiSupport.OrderAsync(shipper.Client, seed.Order);

            var responses = await Task.WhenAll(StartAsync(operario, count, countTag),
                ShippingApiSupport.ConfirmAsync(shipper, shipment, seed.Order, shipmentTag, orderVersion));
            using var started = responses[0];
            using var shipped = responses[1];
            Assert.True(HttpStatusCode.OK == started.StatusCode, await started.Content.ReadAsStringAsync());
            var physical = (await support.Stock.BalanceAsync(seed.Article, seed.Location))!.Physical;
            if (shipped.StatusCode == HttpStatusCode.OK) Assert.Equal(6m, physical);
            else
            {
                await StockApiSupport.ProblemAsync(shipped, HttpStatusCode.Conflict, "CountScopeFrozen");
                Assert.Equal(10m, physical);
            }
            var (detail, _) = await DetailAsync(operario.Client, count);
            Assert.Equal(CycleCountView(physical), Line(detail, seed.Article, seed.Location).GetProperty("theoreticalPhysical").GetString());
            await support.Stock.AssertJournalMatchesBalanceAsync(seed.Article, seed.Location);
        }
    }

    public enum FoundOperation { Add, Remove }

    public enum ArticleRival { Definition, Delete }

    // C-15 (INT-HU04-01, H4): un hallazgo y los escritores del artículo de A se serializan en article:{id}.
    [Theory]
    [InlineData(FoundOperation.Add, ArticleRival.Definition, true)]
    [InlineData(FoundOperation.Add, ArticleRival.Definition, false)]
    [InlineData(FoundOperation.Add, ArticleRival.Delete, true)]
    [InlineData(FoundOperation.Add, ArticleRival.Delete, false)]
    [InlineData(FoundOperation.Remove, ArticleRival.Definition, true)]
    [InlineData(FoundOperation.Remove, ArticleRival.Definition, false)]
    [InlineData(FoundOperation.Remove, ArticleRival.Delete, true)]
    [InlineData(FoundOperation.Remove, ArticleRival.Delete, false)]
    public async Task C15_a_finding_and_an_article_writer_serialize_in_both_orders(FoundOperation operation, ArticleRival rival, bool countFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var gestor = await support.SessionAsync("GestorMaestros");
        var u1 = await support.LocationAsync("U1-" + StockApiSupport.Unique(6));
        var (c, sku) = await support.ArticleAsync();
        var (count, _, etag) = await StartedAsync(operario, u1);
        var addFound = new { counted = Array.Empty<object>(), found = new[] { new { articleId = c, locationId = u1, lotCode = (string?)null, countedPhysical = "1" } } };
        var foundLine = Guid.Empty;
        if (operation == FoundOperation.Remove)
        {
            etag = await OkAsync(LinesAsync(operario, count, etag, addFound));
            foundLine = Line((await DetailAsync(operario.Client, count)).Body, c, u1).GetProperty("id").GetGuid();
        }
        var other = await StockApiSupport.StockWriterAsync(rival == ArticleRival.Definition ? StockWriter.ArticleDefinition : StockWriter.ArticleDelete,
            c, sku, Guid.Empty, gestor, operario);

        Task<HttpResponseMessage> Count() => operation == FoundOperation.Add
            ? LinesAsync(operario, count, etag, addFound)
            : RemoveLineAsync(operario, count, foundLine, etag);
        // El conteo lee el artículo (añadir) o el conteo (retirar) ya con article:{id}; A, el artículo.
        if (countFirst && operation == FoundOperation.Remove) barrier.Hold("CycleCounts", count);
        else barrier.Hold("Articles", c);
        var (counted, changed) = countFirst
            ? await barrier.RaceAsync(fixture, Count, other)
            : Swap(await barrier.RaceAsync(fixture, other, Count));
        using var _a = counted;
        using var _b = changed;

        switch (operation, rival, countFirst)
        {
            case (FoundOperation.Add, ArticleRival.Definition, true):
                // La primera referencia renueva la versión del artículo: el escritor de A trae una vista
                // obsoleta (412); con la versión actual, la definición está bloqueada.
                Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
                await StockApiSupport.ProblemAsync(changed, HttpStatusCode.PreconditionFailed, "VersionConflict");
                using (var retry = await (await StockApiSupport.StockWriterAsync(StockWriter.ArticleDefinition, c, sku, Guid.Empty, gestor, operario))())
                    await StockApiSupport.ProblemAsync(retry, HttpStatusCode.Conflict, "ArticleDefinitionLocked");
                break;
            case (FoundOperation.Add, ArticleRival.Definition, false):
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
                break;
            case (FoundOperation.Add, ArticleRival.Delete, true):
                Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
                await StockApiSupport.ProblemAsync(changed, HttpStatusCode.PreconditionFailed, "VersionConflict");
                using (var retry = await (await StockApiSupport.StockWriterAsync(StockWriter.ArticleDelete, c, sku, Guid.Empty, gestor, operario))())
                    await StockApiSupport.ProblemAsync(retry, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
                break;
            case (FoundOperation.Add, ArticleRival.Delete, false):
                Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
                Assert.Contains("found[0].articleId", ErrorFields(await StockApiSupport.ProblemAsync(counted,
                    HttpStatusCode.UnprocessableEntity, "ValidationFailed")));
                break;
            case (FoundOperation.Remove, ArticleRival.Definition, _):
                // Regla común de A: la primera referencia bloquea la definición aunque después se retire.
                Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);
                await StockApiSupport.ProblemAsync(changed, HttpStatusCode.Conflict, "ArticleDefinitionLocked");
                break;
            case (FoundOperation.Remove, ArticleRival.Delete, true):
                Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
                break;
            default:
                Assert.Equal(HttpStatusCode.NoContent, counted.StatusCode);
                await StockApiSupport.ProblemAsync(changed, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
                // Tras la retirada, sin otras referencias, el borrado se admite.
                using (var retry = await (await StockApiSupport.StockWriterAsync(StockWriter.ArticleDelete, c, sku, Guid.Empty, gestor, operario))())
                    Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
                break;
        }

        // Nunca un artículo borrado con líneas de conteo que lo referencien.
        await using var database = fixture.ApplicationDatabase();
        if (!await database.Articles.AnyAsync(value => value.Id == c))
            Assert.False(await database.Set<CountLine>().AnyAsync(value => value.ArticleId == c));
    }

    private static string CycleCountView(decimal value) => value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    private static (T1, T2) Swap<T2, T1>((T2 First, T1 Second) pair) => (pair.Second, pair.First);
}
