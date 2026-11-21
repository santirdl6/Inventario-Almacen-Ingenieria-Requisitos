using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Features.Stock.CustomerOrders;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inventario.IntegrationTests.Stock.StockApiSupport;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T012 (US3, US5.1; FR-016, FR-022–FR-025; SC-003–SC-005) e INT-HU05-01/02 al reservar.
// Reservas por HTTP con sesión real; existencias preparadas con StockCore (Receive/Ship).
//
// Conteo abierto (US3.7, FR-016): pendiente de INT-HU04-03. IsInOpenCountAsync devuelve false
// hasta que HU-04 integre los conteos; la prueba de 409 CountScopeFrozen se añadirá entonces.
public sealed class ReservationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    /// <summary>Artículo sin lote con 10 en una ubicación y un pedido de 7 sin reservas.</summary>
    private sealed record Scenario(MasterDataApi Operario, MasterDataApi Responsable, Guid Article, string Sku,
        Guid Location, Guid Lot, Guid Order, Guid Line, string Reference);

    private async Task<Scenario> ScenarioAsync(StockApiSupport support, decimal physical = 10m, string ordered = "7")
    {
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var (article, sku) = await support.ArticleAsync();
        var location = await support.LocationAsync("R" + Unique(8) + "-N01");
        await support.ReceiveAsync(article, location, physical);
        var (order, line, reference) = await CreateOrderAsync(operario, article, ordered);
        return new(operario, responsable, article, sku, location, await support.LotAsync(article), order, line, reference);
    }

    [Fact]
    public async Task S10_reserving_reduces_available_shows_the_commitment_and_renews_the_order_etag()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support);
        var (_, before) = await OrderAsync(s.Responsable.Client, s.Order);

        using var reserved = await PostReservationAsync(s.Responsable, s.Line, before, null, (s.Location, null, "7"));
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        Assert.Null(reserved.Headers.ETag);
        var result = await reserved.Content.ReadFromJsonAsync<JsonElement>();
        var reservation = result.GetProperty("reservationId").GetGuid();
        Assert.Equal(s.Order, result.GetProperty("customerOrderId").GetGuid());
        Assert.NotEqual(Guid.Empty, result.GetProperty("movementId").GetGuid());
        Assert.NotEqual(Guid.Empty, result.GetProperty("operationId").GetGuid());
        Assert.Equal($"{Reservations}/{reservation:D}", reserved.Headers.Location!.ToString());

        // GET /stock: físico 10, reservado 7, disponible 3.
        var (_, stock) = await GetStockAsync(s.Responsable.Client, $"?articleId={s.Article}");
        var totals = stock.GetProperty("totals");
        Assert.Equal(("10.000", "7.000", "3.000"), (totals.GetProperty("physical").GetString(),
            totals.GetProperty("reserved").GetString(), totals.GetProperty("available").GetString()));

        // GET /stock/reservations: el pedido y el bucket, con el total remanente del filtro.
        var commitments = await s.Responsable.Client.GetFromJsonAsync<JsonElement>($"/api/v1/stock/reservations?articleId={s.Article}");
        Assert.Equal("7.000", commitments.GetProperty("totals").GetProperty("remaining").GetString());
        Assert.True(commitments.TryGetProperty("asOf", out _));
        Assert.Equal(1, commitments.GetProperty("totalCount").GetInt32());
        var item = commitments.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(s.Order, item.GetProperty("customerOrder").GetProperty("id").GetGuid());
        Assert.Equal(s.Reference, item.GetProperty("customerOrder").GetProperty("externalReference").GetString());
        Assert.Equal("Open", item.GetProperty("customerOrder").GetProperty("state").GetString());
        Assert.Equal(s.Line, item.GetProperty("customerOrderLineId").GetGuid());
        Assert.Equal(reservation, item.GetProperty("reservationId").GetGuid());
        Assert.Equal("Active", item.GetProperty("reservationState").GetString());
        Assert.Equal(s.Location, item.GetProperty("location").GetProperty("id").GetGuid());
        Assert.True(item.GetProperty("lot").GetProperty("isUntracked").GetBoolean());
        Assert.Equal("7.000", item.GetProperty("allocated").GetString());
        Assert.Equal("0.000", item.GetProperty("released").GetString());
        Assert.Equal("7.000", item.GetProperty("remaining").GetString());

        // El ETag del pedido es distinto tras reservar (D-07) y el contexto lo sigue.
        var (detail, after) = await OrderAsync(s.Responsable.Client, s.Order);
        Assert.NotEqual(before, after);
        var line = detail.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("7.000", line.GetProperty("reservedRemaining").GetString());
        var allocation = line.GetProperty("reservations").EnumerateArray().Single().GetProperty("allocations").EnumerateArray().Single();
        Assert.Equal(item.GetProperty("allocationId").GetGuid(), allocation.GetProperty("id").GetGuid());
        Assert.Equal(s.Lot, allocation.GetProperty("lotId").GetGuid());
        var context = await OrderContextAsync(s.Responsable.Client, s.Order);
        Assert.Equal(after, context.GetProperty("orderVersion").GetString());
        Assert.Equal(s.Location, context.GetProperty("locations").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal(s.Lot, context.GetProperty("lots").EnumerateArray().Single().GetProperty("id").GetGuid());

        // GET /reservations/{id}: solo datos propios, con ETag.
        var (view, reservationTag) = await ReservationAsync(s.Responsable.Client, reservation);
        Assert.False(string.IsNullOrEmpty(reservationTag));
        Assert.Equal("Active", view.GetProperty("state").GetString());
        Assert.Equal(s.Order, view.GetProperty("customerOrderId").GetGuid());
        Assert.Equal(s.Line, view.GetProperty("customerOrderLineId").GetGuid());
        Assert.Equal(s.Article, view.GetProperty("articleId").GetGuid());
        Assert.Equal("7.000", view.GetProperty("allocations")[0].GetProperty("remaining").GetString());

        // Diario: movimiento Reservation con soporte (referencia del pedido) y contraparte (cliente).
        await using var db = fixture.ApplicationDatabase();
        var movement = await db.Set<StockMovement>().AsNoTracking()
            .SingleAsync(value => value.SourceDocumentType == StockDocuments.Reservation && value.SourceDocumentId == reservation);
        Assert.Equal(MovementKind.Reservation, movement.Kind);
        Assert.Equal(s.Reference, movement.SupportReference);
        Assert.Equal("Cliente Norte", movement.CounterpartyReference);
        Assert.Null(movement.CounterpartyNotApplicableReason);
        Assert.Equal($"Reserva para el pedido {s.Reference}", movement.Reason);
        await support.AssertCommitmentsMatchBalancesAsync(s.Article, s.Location);
    }

    [Fact]
    public async Task S10_rejects_without_writing_anything()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support);
        var (_, etag) = await OrderAsync(s.Responsable.Client, s.Order);
        var empty = await support.LocationAsync("R" + Unique(8) + "-VACIA");
        var (otherArticle, _) = await support.ArticleAsync();
        await support.ReceiveAsync(otherArticle, empty, 1m);
        var otherLot = await support.LotAsync(otherArticle);

        // Superar el pendiente: 8 sobre 7.
        using (var over = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "8")))
            Assert.True((await ProblemAsync(over, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations", out _));
        using (var noVersion = await PostReservationAsync(s.Responsable, s.Line, null, null, (s.Location, null, "1")))
            await ProblemAsync(noVersion, HttpStatusCode.PreconditionRequired, "PreconditionRequired");
        using (var stale = await PostReservationAsync(s.Responsable, s.Line, "\"AAAAAAAAAAA=\"", null, (s.Location, null, "1")))
            await ProblemAsync(stale, HttpStatusCode.PreconditionFailed, "VersionConflict");
        // Ubicación sin existencias del artículo (mismo lote SIN-LOTE).
        using (var noStock = await PostReservationAsync(s.Responsable, s.Line, etag, null, (empty, s.Lot, "1")))
            await ProblemAsync(noStock, HttpStatusCode.Conflict, "InsufficientStock");
        // Lote de otro artículo; bucket repetido; sin asignaciones; línea inexistente.
        using (var foreign = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, otherLot, "1")))
            Assert.True((await ProblemAsync(foreign, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations[0].lotId", out _));
        using (var repeated = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "1"), (s.Location, s.Lot, "1")))
            Assert.True((await ProblemAsync(repeated, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations[1].locationId", out _));
        using (var none = await PostReservationAsync(s.Responsable, s.Line, etag, null))
            Assert.True((await ProblemAsync(none, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations", out _));
        using (var missing = await PostReservationAsync(s.Responsable, Guid.NewGuid(), etag, null, (s.Location, null, "1")))
            await ProblemAsync(missing, HttpStatusCode.NotFound, "NotFound");

        // Ubicación desactivada: se vacía con StockCore y se desactiva por HTTP.
        var retired = await support.LocationAsync("R" + Unique(8) + "-BAJA");
        await support.ReceiveAsync(s.Article, retired, 1m);
        await support.ShipAsync(s.Article, retired, 1m);
        await DeactivateLocationAsync(s.Operario, retired);
        using (var inactive = await PostReservationAsync(s.Responsable, s.Line, etag, null, (retired, s.Lot, "1")))
            Assert.True((await ProblemAsync(inactive, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations[0].locationId", out _));

        // Pedido que ya no está Open (lo pasará HU-03 a Fulfilled; aquí se simula el escritor).
        var (closed, closedLine, _) = await CreateOrderAsync(s.Operario, s.Article, "1");
        await using (var db = fixture.ApplicationDatabase())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.CustomerOrders SET State = N'Fulfilled' WHERE Id = {closed}");
        var (_, closedTag) = await OrderAsync(s.Responsable.Client, closed);
        using (var notOpen = await PostReservationAsync(s.Responsable, closedLine, closedTag, null, (s.Location, null, "1")))
            await ProblemAsync(notOpen, HttpStatusCode.Conflict, "CustomerOrderNotOpen");

        // Tras reservar 5, otros 3 superan el pendiente (5 + 3 > 7).
        using (var five = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "5")))
            Assert.Equal(HttpStatusCode.Created, five.StatusCode);
        var (_, current) = await OrderAsync(s.Responsable.Client, s.Order);
        using (var three = await PostReservationAsync(s.Responsable, s.Line, current, null, (s.Location, null, "3")))
            await ProblemAsync(three, HttpStatusCode.UnprocessableEntity, "ValidationFailed");

        await using var check = fixture.ApplicationDatabase();
        Assert.Equal(1, await check.Set<StockMovement>().CountAsync(value => value.Kind == MovementKind.Reservation
            && value.SupportReference == s.Reference));
        Assert.Equal(5m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);
        await support.AssertCommitmentsMatchBalancesAsync(s.Article, s.Location);
    }

    [Fact]
    public async Task S11_two_allocations_with_one_insufficient_create_nothing()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support, physical: 4m);
        var n02 = await support.LocationAsync("R" + Unique(8) + "-N02");
        await support.ReceiveAsync(s.Article, n02, 2m);
        var (_, etag) = await OrderAsync(s.Responsable.Client, s.Order);

        using var response = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "4"), (n02, null, "3"));
        await ProblemAsync(response, HttpStatusCode.Conflict, "InsufficientStock");
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Set<StockMovement>().AnyAsync(value => value.Kind == MovementKind.Reservation
            && value.SupportReference == s.Reference));
        Assert.False(await db.Set<ReservationAllocation>().AnyAsync(value => value.ArticleId == s.Article));
        Assert.Equal(0m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);
        Assert.Equal(etag, (await OrderAsync(s.Responsable.Client, s.Order)).ETag);
    }

    [Fact]
    public async Task Idempotency_replays_the_same_intention_and_rejects_another_body_with_the_same_key()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support);
        var (_, etag) = await OrderAsync(s.Responsable.Client, s.Order);
        var key = Guid.NewGuid();

        using var first = await PostReservationAsync(s.Responsable, s.Line, etag, key, (s.Location, null, "7"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // El If-Match ya es obsoleto, pero el replay se resuelve antes (contrato, «Huella idempotente»).
        using var replay = await PostReservationAsync(s.Responsable, s.Line, etag, key, (s.Location, null, "7.000"));
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        using var conflict = await PostReservationAsync(s.Responsable, s.Line, etag, key, (s.Location, null, "6"));
        await ProblemAsync(conflict, HttpStatusCode.Conflict, "IdempotencyConflict");

        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(1, await db.Set<StockMovement>().CountAsync(value => value.Kind == MovementKind.Reservation
            && value.SupportReference == s.Reference));
        Assert.Equal(7m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);
    }

    [Fact]
    public async Task Permissions_reserving_needs_reservations_manage_and_reading_inventory_read()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support);
        var (_, etag) = await OrderAsync(s.Responsable.Client, s.Order);
        using (var forbidden = await PostReservationAsync(s.Operario, s.Line, etag, null, (s.Location, null, "1")))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var created = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "1"));
        var reservation = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reservationId").GetGuid();

        var gestor = await support.SessionAsync("GestorMaestros");
        Assert.Equal(HttpStatusCode.OK, (await gestor.Client.GetAsync($"{Reservations}/{reservation:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestor.Client.GetAsync($"/api/v1/stock/reservations?articleId={s.Article}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestor.Client.GetAsync($"{Reservations}/{Guid.NewGuid():D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestor.Client.GetAsync($"/api/v1/stock/reservations?articleId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await gestor.Client.GetAsync("/api/v1/stock/reservations")).StatusCode);
        var admin = await support.SessionAsync("Administrador");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.GetAsync($"{Reservations}/{reservation:D}")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_article_rejects_a_new_reservation_without_changes()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support);
        var gestor = await support.SessionAsync("GestorMaestros");
        await DeactivateArticleAsync(gestor, s.Article, s.Sku);
        var (_, etag) = await OrderAsync(s.Responsable.Client, s.Order);

        using var response = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "1"));
        await ProblemAsync(response, HttpStatusCode.Conflict, "ArticleInactive");
        Assert.Equal(0m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);
        Assert.Equal(etag, (await OrderAsync(s.Responsable.Client, s.Order)).ETag);
    }

    public enum Writer { ArticleDeactivate, ArticleDefinition, ArticleDelete, LocationDeactivate }

    [Theory]
    [InlineData(Writer.ArticleDeactivate, true)]
    [InlineData(Writer.ArticleDeactivate, false)]
    [InlineData(Writer.ArticleDefinition, true)]
    [InlineData(Writer.ArticleDefinition, false)]
    [InlineData(Writer.ArticleDelete, true)]
    [InlineData(Writer.ArticleDelete, false)]
    [InlineData(Writer.LocationDeactivate, true)]
    [InlineData(Writer.LocationDeactivate, false)]
    public async Task Reservation_and_article_or_location_writers_serialize_in_both_orders(Writer writer, bool reserveFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support);
        var gestor = await support.SessionAsync("GestorMaestros");
        var (_, orderTag) = await OrderAsync(s.Responsable.Client, s.Order);
        var change = await WriterAsync(writer, s, gestor);

        if (writer == Writer.LocationDeactivate) barrier.Hold("Locations", s.Location);
        else barrier.Hold("Articles", s.Article);
        Task<HttpResponseMessage> Reserve() => PostReservationAsync(s.Responsable, s.Line, orderTag, null, (s.Location, null, "7"));
        var (reserved, changed) = reserveFirst
            ? await barrier.RaceAsync(fixture, Reserve, change)
            : Swap(await barrier.RaceAsync(fixture, change, Reserve));
        using var _r = reserved;
        using var _c = changed;

        var reservedAmount = (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved;
        switch (writer)
        {
            case Writer.ArticleDeactivate:
                // La desactivación siempre se confirma: respeta el compromiso que gana; si gana ella,
                // la reserva se rechaza (R-05).
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                if (reserveFirst) Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
                else await ProblemAsync(reserved, HttpStatusCode.Conflict, "ArticleInactive");
                Assert.Equal(reserveFirst ? 7m : 0m, reservedAmount);
                break;
            case Writer.ArticleDefinition:
                // La línea del pedido ya fijó la definición: el PUT se rechaza en ambos órdenes.
                await ProblemAsync(changed, HttpStatusCode.Conflict, "ArticleDefinitionLocked");
                Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
                Assert.Equal(7m, reservedAmount);
                break;
            case Writer.ArticleDelete:
                await ProblemAsync(changed, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
                Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
                Assert.Equal(7m, reservedAmount);
                break;
            default:
                // La ubicación tiene existencias: no se desactiva. Si la reserva gana, la ubicación
                // pasa a tener reservas y cambia su revisión (HU-02: capacidades derivadas): 412.
                if (reserveFirst) await ProblemAsync(changed, HttpStatusCode.PreconditionFailed, "VersionConflict");
                else Assert.Contains("0.000 reservadas",
                    (await ProblemAsync(changed, HttpStatusCode.Conflict, "LocationNotEmpty")).GetProperty("detail").GetString());
                Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
                Assert.Equal(7m, reservedAmount);
                break;
        }
        await support.AssertCommitmentsMatchBalancesAsync(s.Article, s.Location);
    }

    [Fact]
    public async Task V05_two_reservations_of_the_last_available_for_different_orders_never_overbook()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var first = await support.SessionAsync("ResponsableAlmacen");
        var second = await support.SessionAsync("ResponsableAlmacen");
        for (var round = 0; round < 20; round++)
        {
            var (article, _) = await support.ArticleAsync();
            var location = await support.LocationAsync("V" + Unique(8));
            await support.ReceiveAsync(article, location, 10m);
            var (a, lineA, _) = await CreateOrderAsync(operario, article, "7");
            var (b, lineB, _) = await CreateOrderAsync(operario, article, "7");
            var (_, tagA) = await OrderAsync(first.Client, a);
            var (_, tagB) = await OrderAsync(second.Client, b);

            var responses = await Task.WhenAll(
                PostReservationAsync(first, lineA, tagA, null, (location, null, "7")),
                PostReservationAsync(second, lineB, tagB, null, (location, null, "7")));
            var statuses = responses.Select(response => response.StatusCode).Order().ToList();
            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);
            await ProblemAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                HttpStatusCode.Conflict, "InsufficientStock");
            foreach (var response in responses) response.Dispose();
            Assert.Equal(7m, (await support.Stock.BalanceAsync(article, location))!.Reserved);
            await support.AssertCommitmentsMatchBalancesAsync(article, location);
        }
    }

    [Fact]
    public async Task S18_reader_inside_the_executor_matches_the_order_detail_per_bucket()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ScenarioAsync(support, physical: 4m);
        var n02 = await support.LocationAsync("R" + Unique(8) + "-N02");
        await support.ReceiveAsync(s.Article, n02, 5m);
        var (_, etag) = await OrderAsync(s.Responsable.Client, s.Order);
        using (var reserved = await PostReservationAsync(s.Responsable, s.Line, etag, null, (s.Location, null, "4"), (n02, null, "2")))
            Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        var (detail, current) = await OrderAsync(s.Responsable.Client, s.Order);

        var (_, other) = await support.Stock.SeedAsync();
        var (receiving, _) = await support.ArticleAsync();
        var (_, principal) = await support.Stock.ActorAsync();
        CustomerOrderSnapshot? snapshot = null;
        await support.Stock.RunAsync(principal, Guid.NewGuid(),
            StockCore.Prepare(StockOperation.Receive, StockTestHarness.Context("receipt"), [new(receiving, other.Id, null, 1m)]),
            async (context, token) =>
            {
                await context.Locks.DocumentsAsync([new DocumentLock(StockDocuments.CustomerOrder, s.Order)], token);
                snapshot = await context.Services.GetRequiredService<ICustomerOrderReader>().ReadAsync(context, s.Order, token);
            });

        Assert.NotNull(snapshot);
        Assert.Equal(current, Inventario.Api.Infrastructure.Http.EntityTags.Format(snapshot.Version));
        var line = Assert.Single(snapshot.Lines);
        var viewLine = detail.GetProperty("lines")[0];
        Assert.Equal(viewLine.GetProperty("pending").GetString(), Format(line.Pending));
        var byBucket = line.ActiveAllocations.ToDictionary(value => value.LocationId, value => value.Remaining);
        Assert.Equal(new Dictionary<Guid, decimal> { [s.Location] = 4m, [n02] = 2m }, byBucket);
        Assert.All(line.ActiveAllocations, value => Assert.Equal(Lot.UntrackedCode, value.LotCode));
        var viewBuckets = viewLine.GetProperty("reservations").EnumerateArray()
            .SelectMany(reservation => reservation.GetProperty("allocations").EnumerateArray())
            .ToDictionary(value => value.GetProperty("locationId").GetGuid(), value => value.GetProperty("remaining").GetString());
        Assert.Equal(byBucket.ToDictionary(pair => pair.Key, pair => (string?)Format(pair.Value)), viewBuckets);
    }

    private static string Format(decimal value) => value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    private static (HttpResponseMessage Reserved, HttpResponseMessage Changed) Swap((HttpResponseMessage Changed, HttpResponseMessage Reserved) value) =>
        (value.Reserved, value.Changed);

    /// <summary>Prepara el escritor con la versión leída antes de la carrera.</summary>
    private static Task<Func<Task<HttpResponseMessage>>> WriterAsync(Writer writer, Scenario s, MasterDataApi gestor) =>
        StockWriterAsync((StockWriter)(int)writer, s.Article, s.Sku, s.Location, gestor, s.Operario);
}
