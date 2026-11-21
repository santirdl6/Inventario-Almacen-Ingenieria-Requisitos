using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Features.Stock;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Stock.StockApiSupport;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T014 (US4, US5.1–US5.5; FR-017–FR-019, FR-022–FR-024) e INT-HU05-02 al liberar.
// Extensión de V-04 (S-14), S-15, ETag tras cada liberación, carreras y permisos.
public sealed class ReleaseTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private const string Reason = "El cliente reduce el pedido";

    /// <summary>10 en una ubicación, pedido de 7 y una reserva de 7 con una asignación.</summary>
    internal sealed record Reserved(MasterDataApi Operario, MasterDataApi Responsable, Guid Article, string Sku,
        Guid Location, Guid Order, Guid Reservation, Guid Allocation, string Reference);

    internal static async Task<Reserved> ReservedAsync(StockApiSupport support, string quantity = "7")
    {
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var (article, sku) = await support.ArticleAsync();
        var location = await support.LocationAsync("L" + Unique(8) + "-N01");
        await support.ReceiveAsync(article, location, 10m);
        var (order, line, reference) = await CreateOrderAsync(operario, article, "7");
        var (_, etag) = await OrderAsync(responsable.Client, order);
        using var reserved = await PostReservationAsync(responsable, line, etag, null, (location, null, quantity));
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        var reservation = (await reserved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reservationId").GetGuid();
        var (view, _) = await ReservationAsync(responsable.Client, reservation);
        return new(operario, responsable, article, sku, location, order, reservation,
            view.GetProperty("allocations")[0].GetProperty("id").GetGuid(), reference);
    }

    [Fact]
    public async Task S14_partial_releases_repeat_without_double_effects_and_renew_both_etags()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ReservedAsync(support);
        var (_, reservationTag0) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        var (_, orderTag0) = await OrderAsync(s.Responsable.Client, s.Order);
        var r1 = Guid.NewGuid();
        var k1 = Guid.NewGuid();

        // R1 libera 2 con motivo.
        using var first = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag0, k1, r1, Reason, (s.Allocation, "2"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Null(first.Headers.ETag);
        var firstText = await first.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(firstText).RootElement;
        Assert.Equal(k1, result.GetProperty("operationId").GetGuid());
        Assert.Equal(s.Reservation, result.GetProperty("reservationId").GetGuid());
        Assert.Equal(r1, result.GetProperty("releaseId").GetGuid());
        Assert.NotEqual(Guid.Empty, result.GetProperty("movementId").GetGuid());
        Assert.Equal("Active", result.GetProperty("state").GetString());
        // ETag (D-07): la reserva y el pedido cambian de versión con cada liberación.
        var (_, reservationTag1) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        var (_, orderTag1) = await OrderAsync(s.Responsable.Client, s.Order);
        Assert.NotEqual(reservationTag0, reservationTag1);
        Assert.NotEqual(orderTag0, orderTag1);
        Assert.Equal(orderTag1, (await OrderContextAsync(s.Responsable.Client, s.Order)).GetProperty("orderVersion").GetString());

        // R2 libera otros 2.
        var r2 = Guid.NewGuid();
        using (var second = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag1, null, r2, Reason, (s.Allocation, "2")))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var (_, reservationTag2) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        var (_, orderTag2) = await OrderAsync(s.Responsable.Client, s.Order);
        Assert.NotEqual(reservationTag1, reservationTag2);
        Assert.NotEqual(orderTag1, orderTag2);

        // R1 con su clave: replay idéntico, aunque su If-Match ya sea obsoleto.
        using (var replay = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag0, k1, r1, Reason, (s.Allocation, "2.000")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(firstText, await replay.Content.ReadAsStringAsync());
        }
        // R1 con otra clave y la versión actual: el documento ya se aplicó.
        using (var again = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, null, r1, Reason, (s.Allocation, "2")))
            await ProblemAsync(again, HttpStatusCode.Conflict, "DocumentAlreadyConfirmed");
        // Idempotencia (FR-023): la clave de R1 con otro motivo o cantidad.
        using (var otherReason = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, k1, r1, "Otro motivo", (s.Allocation, "2")))
            await ProblemAsync(otherReason, HttpStatusCode.Conflict, "IdempotencyConflict");
        using (var otherQuantity = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, k1, r1, Reason, (s.Allocation, "1")))
            await ProblemAsync(otherQuantity, HttpStatusCode.Conflict, "IdempotencyConflict");
        // 5 sobre un remanente de 3.
        using (var excess = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, null, Guid.NewGuid(), Reason, (s.Allocation, "5")))
            await ProblemAsync(excess, HttpStatusCode.Conflict, "ReservedStockConflict");
        // Sin motivo, sin releaseId, asignación ajena o repetida, ETag obsoleto o ausente, reserva inexistente.
        using (var noReason = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, null, Guid.NewGuid(), "   ", (s.Allocation, "1")))
            Assert.True((await ProblemAsync(noReason, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("reason", out _));
        using (var noRelease = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, null, Guid.Empty, Reason, (s.Allocation, "1")))
            Assert.True((await ProblemAsync(noRelease, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("releaseId", out _));
        using (var foreign = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, null, Guid.NewGuid(), Reason, (Guid.NewGuid(), "1")))
            Assert.True((await ProblemAsync(foreign, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations[0].allocationId", out _));
        using (var repeated = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag2, null, Guid.NewGuid(), Reason,
                   (s.Allocation, "1"), (s.Allocation, "1")))
            Assert.True((await ProblemAsync(repeated, HttpStatusCode.UnprocessableEntity, "ValidationFailed"))
                .GetProperty("errors").TryGetProperty("allocations[1].allocationId", out _));
        using (var stale = await PostReleaseAsync(s.Responsable, s.Reservation, reservationTag0, null, Guid.NewGuid(), Reason, (s.Allocation, "1")))
            await ProblemAsync(stale, HttpStatusCode.PreconditionFailed, "VersionConflict");
        using (var noVersion = await PostReleaseAsync(s.Responsable, s.Reservation, null, null, Guid.NewGuid(), Reason, (s.Allocation, "1")))
            await ProblemAsync(noVersion, HttpStatusCode.PreconditionRequired, "PreconditionRequired");
        using (var missing = await PostReleaseAsync(s.Responsable, Guid.NewGuid(), reservationTag2, null, Guid.NewGuid(), Reason, (s.Allocation, "1")))
            await ProblemAsync(missing, HttpStatusCode.NotFound, "NotFound");

        // Al final: reservado 3, físico sin cambios y dos movimientos Release completos.
        var balance = (await support.Stock.BalanceAsync(s.Article, s.Location))!;
        Assert.Equal((10m, 3m), (balance.Physical, balance.Reserved));
        Assert.Equal(reservationTag2, (await ReservationAsync(s.Responsable.Client, s.Reservation)).ETag);
        var (view, _) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        Assert.Equal(("4.000", "3.000"), (view.GetProperty("allocations")[0].GetProperty("released").GetString(),
            view.GetProperty("allocations")[0].GetProperty("remaining").GetString()));
        await using var db = fixture.ApplicationDatabase();
        var releases = await db.Set<StockMovement>().AsNoTracking().Include(movement => movement.Entries)
            .Where(movement => movement.Kind == MovementKind.Release && movement.SupportReference == $"reservation:{s.Reservation:D}")
            .ToListAsync();
        Assert.Equal(2, releases.Count);
        Assert.Equal(new[] { r1, r2 }.Order(), releases.Select(movement => movement.SourceDocumentId).Order());
        Assert.All(releases, movement =>
        {
            Assert.Equal(StockDocuments.ReservationRelease, movement.SourceDocumentType);
            Assert.Equal(Reason, movement.Reason);
            Assert.NotEqual(Guid.Empty, movement.ActorId);
            Assert.Equal("Cliente Norte", movement.CounterpartyReference);
            Assert.Null(movement.CounterpartyNotApplicableReason);
            Assert.Equal(2m, Assert.Single(movement.Entries).Quantity);
        });
        await support.AssertCommitmentsMatchBalancesAsync(s.Article, s.Location);
    }

    [Fact]
    public async Task S15_release_is_allowed_with_the_article_deactivated_and_the_article_still_cannot_be_deleted()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ReservedAsync(support);
        var gestor = await support.SessionAsync("GestorMaestros");
        await DeactivateArticleAsync(gestor, s.Article, s.Sku);
        var (_, etag) = await ReservationAsync(s.Responsable.Client, s.Reservation);

        using var released = await PostReleaseAsync(s.Responsable, s.Reservation, etag, null, Guid.NewGuid(), Reason, (s.Allocation, "7"));
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal("Released", (await released.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        Assert.Equal("Released", (await ReservationAsync(s.Responsable.Client, s.Reservation)).Body.GetProperty("state").GetString());
        var (order, _) = await OrderAsync(s.Responsable.Client, s.Order);
        Assert.Equal("0.000", order.GetProperty("lines")[0].GetProperty("reservedRemaining").GetString());
        Assert.Equal(0m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);

        // Tras liberarlo todo, las líneas del pedido siguen siendo referencias (INT-HU05-01).
        var delete = await StockWriterAsync(StockWriter.ArticleDelete, s.Article, s.Sku, s.Location, gestor, s.Operario);
        using var deleted = await delete();
        await ProblemAsync(deleted, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
        await support.AssertCommitmentsMatchBalancesAsync(s.Article, s.Location);
    }

    [Fact]
    public async Task Order_etag_ignores_the_location_deactivated_after_releasing_everything_and_emptying_it()
    {
        // Prueba de T010 que necesitaba liberar (INT-HU05-01): el estado de la ubicación es dato
        // de otro agregado y solo cambia el contexto.
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ReservedAsync(support);
        var (_, etag) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        using (var released = await PostReleaseAsync(s.Responsable, s.Reservation, etag, null, Guid.NewGuid(), Reason, (s.Allocation, "7")))
            Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        await support.ShipAsync(s.Article, s.Location, 10m);
        var (before, orderTag) = await OrderAsync(s.Responsable.Client, s.Order);
        Assert.True((await OrderContextAsync(s.Responsable.Client, s.Order)).GetProperty("locations")[0].GetProperty("isActive").GetBoolean());

        await DeactivateLocationAsync(s.Operario, s.Location);

        var (after, sameTag) = await OrderAsync(s.Responsable.Client, s.Order);
        Assert.Equal(orderTag, sameTag);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        var context = await OrderContextAsync(s.Responsable.Client, s.Order);
        var location = context.GetProperty("locations").EnumerateArray().Single();
        Assert.Equal(s.Location, location.GetProperty("id").GetGuid());
        Assert.False(location.GetProperty("isActive").GetBoolean());
        Assert.Equal(orderTag, context.GetProperty("orderVersion").GetString());
    }

    [Theory]
    [InlineData(StockWriter.ArticleDeactivate, true)]
    [InlineData(StockWriter.ArticleDeactivate, false)]
    [InlineData(StockWriter.ArticleDefinition, true)]
    [InlineData(StockWriter.ArticleDefinition, false)]
    [InlineData(StockWriter.ArticleDelete, true)]
    [InlineData(StockWriter.ArticleDelete, false)]
    [InlineData(StockWriter.LocationDeactivate, true)]
    [InlineData(StockWriter.LocationDeactivate, false)]
    public async Task Release_and_article_or_location_writers_serialize_in_both_orders(StockWriter writer, bool releaseFirst)
    {
        var barrier = new LockBarrier();
        using var factory = LockBarrier.Factory(fixture, barrier);
        using var support = new StockApiSupport(fixture, factory);
        var s = await ReservedAsync(support);
        var gestor = await support.SessionAsync("GestorMaestros");
        var (_, etag) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        var change = await StockWriterAsync(writer, s.Article, s.Sku, s.Location, gestor, s.Operario);

        if (writer == StockWriter.LocationDeactivate) barrier.Hold("Locations", s.Location);
        else barrier.Hold("Articles", s.Article);
        Task<HttpResponseMessage> Release() =>
            PostReleaseAsync(s.Responsable, s.Reservation, etag, null, Guid.NewGuid(), Reason, (s.Allocation, "3"));
        HttpResponseMessage released, changed;
        if (releaseFirst) (released, changed) = await barrier.RaceAsync(fixture, Release, change);
        else (changed, released) = await barrier.RaceAsync(fixture, change, Release);
        using var _r = released;
        using var _c = changed;

        // La liberación se confirma siempre (R-05, INT-HU05-02).
        Assert.True(released.StatusCode == HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        Assert.Equal(4m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);
        switch (writer)
        {
            case StockWriter.ArticleDeactivate:
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                break;
            case StockWriter.ArticleDefinition:
                await ProblemAsync(changed, HttpStatusCode.Conflict, "ArticleDefinitionLocked");
                break;
            case StockWriter.ArticleDelete:
                await ProblemAsync(changed, HttpStatusCode.Conflict, "HistoryPreventsDeletion");
                break;
            default:
                // Con existencias no se desactiva en ningún orden. La revisión de HU-02 cubre las
                // capacidades derivadas, no las cantidades: liberar 3 de 7 no la cambia (a diferencia
                // de la primera reserva en T012), así que llega a la regla y el detalle muestra qué
                // reservado leyó: 4 si la liberación ganó, 7 si ganó la desactivación.
                var problem = await ProblemAsync(changed, HttpStatusCode.Conflict, "LocationNotEmpty");
                Assert.Contains(releaseFirst ? "4.000 reservadas" : "7.000 reservadas", problem.GetProperty("detail").GetString());
                break;
        }
        await support.AssertCommitmentsMatchBalancesAsync(s.Article, s.Location);
    }

    [Fact]
    public async Task Permissions_releasing_needs_reservations_manage_and_antiforgery()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var s = await ReservedAsync(support);
        var (_, etag) = await ReservationAsync(s.Responsable.Client, s.Reservation);
        using (var forbidden = await PostReleaseAsync(s.Operario, s.Reservation, etag, null, Guid.NewGuid(), Reason, (s.Allocation, "1")))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Reservations}/{s.Reservation:D}/release")
        {
            Content = JsonContent.Create(new
            {
                releaseId = Guid.NewGuid(), reason = Reason,
                allocations = new[] { new { allocationId = s.Allocation, quantity = "1" } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("If-Match", etag);
        using var noToken = await s.Responsable.Client.SendAsync(request);
        await ProblemAsync(noToken, HttpStatusCode.BadRequest, "AntiforgeryValidationFailed");
        Assert.Equal(7m, (await support.Stock.BalanceAsync(s.Article, s.Location))!.Reserved);
    }
}
