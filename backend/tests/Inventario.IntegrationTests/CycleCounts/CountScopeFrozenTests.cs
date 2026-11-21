using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Shipping;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Locations;
using Inventario.IntegrationTests.Shipping;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.CycleCounts;

// HU-04 T005 (C-12, primera mitad; research D-04, INT-HU04-03, H2 de analyze-1). Con una ubicación
// congelada por un conteo abierto, sembrado directamente: confirmar una salida, reservar, desactivar
// la ubicación y cambiar su tipo, vacía u ocupada, responden 409 CountScopeFrozen sin cambios; liberar
// una reserva sigue admitido (Q1); inOpenCount es real en el detalle y en /stock. La segunda mitad
// (tras confirmar y cancelar todo vuelve a admitirse) la cierra T009.
public sealed class CountScopeFrozenTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Writers_are_rejected_on_a_frozen_scope_and_releasing_a_reservation_is_still_allowed()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var seed = await ShippingApiSupport.SeedAsync(support, operario, 10m, "6");
        var reservation = await ShippingApiSupport.ReserveAsync(responsable, seed.Order, seed.OrderLine, seed.Location, seed.Lot, "2");
        var (shipment, _) = await ShippingApiSupport.CreateDraftAsync(operario, ShippingApiSupport.Body(seed, "3"));
        var empty = await support.LocationAsync("N02-" + StockApiSupport.Unique(6));
        await CountScopeSeed.FreezeAsync(fixture, seed.Location, empty);

        // Confirmar una salida desde la ubicación congelada.
        using (var confirm = await ShippingApiSupport.ConfirmAsync(operario, shipment, seed.Order))
            await StockApiSupport.ProblemAsync(confirm, HttpStatusCode.Conflict, "CountScopeFrozen");
        // Reservar en la ubicación congelada.
        var (_, orderTag) = await StockApiSupport.OrderAsync(responsable.Client, seed.Order);
        using (var reserve = await StockApiSupport.PostReservationAsync(responsable, seed.OrderLine, orderTag, null,
                   (seed.Location, seed.Lot, "1")))
            await StockApiSupport.ProblemAsync(reserve, HttpStatusCode.Conflict, "CountScopeFrozen");

        // Desactivar y cambiar el tipo, ocupada y vacía: la congelación va antes que LocationNotEmpty (H2).
        foreach (var location in new[] { seed.Location, empty })
        {
            var (current, etag) = await LocationsTestSupport.GetAsync(operario, location);
            using (var deactivate = await LocationsTestSupport.UpdateAsync(operario, location, current, etag, isActive: false))
                await StockApiSupport.ProblemAsync(deactivate, HttpStatusCode.Conflict, "CountScopeFrozen");
            using (var kind = await LocationsTestSupport.UpdateAsync(operario, location, current, etag, kind: "Receiving"))
                await StockApiSupport.ProblemAsync(kind, HttpStatusCode.Conflict, "CountScopeFrozen");
            Assert.True(current.GetProperty("inOpenCount").GetBoolean());
            var stock = await operario.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations/{location:D}/stock");
            Assert.True(stock.GetProperty("inOpenCount").GetBoolean());
        }

        // Liberar sigue admitido (Q1, FR-015): es la vía para resolver un contado inferior al reservado.
        var (_, reservationTag) = await StockApiSupport.ReservationAsync(responsable.Client, reservation);
        using (var release = await StockApiSupport.PostReleaseAsync(responsable, reservation, reservationTag, null, Guid.NewGuid(),
                   "Conteo en curso", (await AllocationAsync(reservation), "1")))
            Assert.True(release.StatusCode == HttpStatusCode.OK, await release.Content.ReadAsStringAsync());

        // Sin cambios: salida en borrador, físico intacto, reservado 1 tras la liberación, ubicaciones iguales.
        await using var database = fixture.ApplicationDatabase();
        Assert.Equal(ShipmentState.Draft, (await database.Set<Shipment>().AsNoTracking().SingleAsync(value => value.Id == shipment)).State);
        var balance = await database.Set<StockBalance>().AsNoTracking()
            .SingleAsync(value => value.ArticleId == seed.Article && value.LocationId == seed.Location);
        Assert.Equal((10m, 1m), (balance.Physical, balance.Reserved));
        foreach (var stored in await database.Set<Location>().AsNoTracking().Where(value => value.Id == seed.Location || value.Id == empty)
                     .ToListAsync())
            Assert.Equal((true, LocationKind.Storage), (stored.IsActive, stored.Kind));
        await support.AssertCommitmentsMatchBalancesAsync(seed.Article, seed.Location);
    }

    [Fact]
    public async Task A_draft_count_does_not_freeze_its_scope()
    {
        using var factory = fixture.Factory();
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var location = await support.LocationAsync("N03-" + StockApiSupport.Unique(6));
        await CountScopeSeed.DraftAsync(fixture, location);

        var (current, etag) = await LocationsTestSupport.GetAsync(operario, location);
        Assert.False(current.GetProperty("inOpenCount").GetBoolean());
        using var kind = await LocationsTestSupport.UpdateAsync(operario, location, current, etag, kind: "Receiving");
        Assert.True(kind.StatusCode == HttpStatusCode.OK, await kind.Content.ReadAsStringAsync());
    }

    private async Task<Guid> AllocationAsync(Guid reservation)
    {
        await using var database = fixture.ApplicationDatabase();
        return await database.Set<ReservationAllocation>().Where(value => value.ReservationId == reservation)
            .Select(value => value.Id).SingleAsync();
    }
}

/// <summary>
/// Conteos sembrados directamente con el dominio para las pruebas de congelación, sin los endpoints
/// de HU-04 (C-12: «con una fila congelada sembrada directamente»).
/// </summary>
internal static class CountScopeSeed
{
    public static async Task<Guid> DraftAsync(IdentityFixture fixture, params Guid[] locations)
    {
        var actor = await ActorAsync(fixture);
        var count = CycleCount.CreateDraft(Guid.NewGuid(), locations, actor, DateTimeOffset.UtcNow);
        await using var database = fixture.ApplicationDatabase();
        database.Add(count);
        await database.SaveChangesAsync();
        return count.Id;
    }

    /// <summary>Conteo en Counting sin líneas: congela el alcance.</summary>
    public static async Task<Guid> FreezeAsync(IdentityFixture fixture, params Guid[] locations)
    {
        var id = await DraftAsync(fixture, locations);
        await using var database = fixture.ApplicationDatabase();
        var count = await database.Set<CycleCount>().Include(value => value.Locations).SingleAsync(value => value.Id == id);
        count.Start([], count.CreatedBy, DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> ActorAsync(IdentityFixture fixture)
    {
        var email = await fixture.CreateUserAsync("Operario");
        await using var database = fixture.ApplicationDatabase();
        return await database.Users.Where(value => value.Email == email).Select(value => value.Id).SingleAsync();
    }
}
