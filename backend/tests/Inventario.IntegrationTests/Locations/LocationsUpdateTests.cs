using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Xunit;
using static Inventario.IntegrationTests.Locations.LocationsTestSupport;

namespace Inventario.IntegrationTests.Locations;

// US4 — Corregir y retirar ubicaciones sin perder la trazabilidad (quickstart L-07, L-08).
public sealed class LocationsUpdateTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task L07_corrects_a_location_without_movements_and_locks_identity_after_the_first_one()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var fresh = await CreateLocationAsync(api);

        var corrected = await UpdateAsync(api, fresh.Id, fresh.Resource, fresh.ETag, level: "n02");
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var (read, etag) = await GetAsync(api, fresh.Id);
        Assert.Equal("N02", read.GetProperty("level").GetString());
        Assert.Equal(1, await support.AuditCountAsync(fresh.Id, MasterDataAction.Update));

        // El tipo cambia estando vacía.
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(api, fresh.Id, read, etag, kind: "Receiving")).StatusCode);

        // Con un movimiento confirmado, y aunque vuelva a cero, la identificación queda bloqueada.
        var used = await CreateLocationAsync(api);
        var article = await support.ArticleAsync();
        await support.ReceiveAsync(article, used.Id, 3);
        (read, etag) = await GetAsync(api, used.Id);
        var locked = await UpdateAsync(api, used.Id, read, etag, level: "N09");
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        var problem = await locked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LocationIdentityLocked", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("level", out _));

        await support.ShipAsync(article, used.Id, 3);
        (read, etag) = await GetAsync(api, used.Id);
        Assert.True(read.GetProperty("isEmpty").GetBoolean());
        Assert.Equal("LocationIdentityLocked", await ProblemCodeAsync(await UpdateAsync(api, used.Id, read, etag, code: "NUEVO-" + Unique())));
        Assert.Equal("N01", (await GetAsync(api, used.Id)).Body.GetProperty("level").GetString());
    }

    [Fact]
    public async Task L08_only_an_empty_location_without_reservations_can_be_deactivated_and_it_can_be_reactivated()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var location = await CreateLocationAsync(api);
        var article = await support.ArticleAsync();
        await support.ReceiveAsync(article, location.Id, 5);

        var (read, etag) = await GetAsync(api, location.Id);
        var full = await UpdateAsync(api, location.Id, read, etag, isActive: false);
        Assert.Equal("LocationNotEmpty", await ProblemCodeAsync(full));
        Assert.Equal("LocationNotEmpty", await ProblemCodeAsync(await UpdateAsync(api, location.Id, read, etag, kind: "Receiving")));

        // Con existencias a cero pero una reserva viva tampoco está vacía.
        await support.ReserveAsync(article, location.Id, 2);
        await support.ShipAsync(article, location.Id, 3);
        (read, etag) = await GetAsync(api, location.Id);
        Assert.Equal("LocationNotEmpty", await ProblemCodeAsync(await UpdateAsync(api, location.Id, read, etag, isActive: false)));

        await support.ShipAsync(article, location.Id, 2, ownReserved: 2);
        (read, etag) = await GetAsync(api, location.Id);
        var deactivated = await UpdateAsync(api, location.Id, read, etag, isActive: false);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal("Inactiva", (await deactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource")
            .GetProperty("statusLabel").GetString());
        Assert.Equal(1, await support.AuditCountAsync(location.Id, MasterDataAction.Deactivate));

        // Una ubicación inactiva no admite recepciones ni reservas y sigue en las consultas.
        var rejected = await Assert.ThrowsAsync<InventoryProblemException>(() => support.ReceiveAsync(article, location.Id, 1));
        Assert.Equal(422, rejected.Status);
        Assert.Contains("desactivada", rejected.Message);
        await Assert.ThrowsAsync<InventoryProblemException>(() => support.ReserveAsync(article, location.Id, 1));
        var history = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search={read.GetProperty("code").GetString()}&status=inactive");
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());

        (read, etag) = await GetAsync(api, location.Id);
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(api, location.Id, read, etag, isActive: true)).StatusCode);
        Assert.Equal(1, await support.AuditCountAsync(location.Id, MasterDataAction.Reactivate));
        await support.ReceiveAsync(article, location.Id, 1);
    }

    [Fact]
    public async Task State_changes_are_separate_unchanged_puts_are_not_audited_and_locations_are_never_deleted()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var location = await CreateLocationAsync(api);

        var mixed = await UpdateAsync(api, location.Id, location.Resource, location.ETag, level: "N05", isActive: false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mixed.StatusCode);
        Assert.Equal("StateChangeMustBeSeparate", await ProblemCodeAsync(mixed));

        var unchanged = await UpdateAsync(api, location.Id, location.Resource, location.ETag);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(0, await support.AuditCountAsync(location.Id, MasterDataAction.Update));

        var delete = await api.WriteAsync(HttpMethod.Delete, $"/api/v1/locations/{location.Id:D}", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Client.GetAsync($"/api/v1/locations/{location.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task Read_only_accounts_cannot_change_locations()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var manager = await support.SessionAsync();
        var reader = await support.SessionAsync("GestorMaestros");
        var location = await CreateLocationAsync(manager);

        var rejected = await UpdateAsync(reader, location.Id, location.Resource, location.ETag, isActive: false);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.True((await GetAsync(manager, location.Id)).Body.GetProperty("isActive").GetBoolean());
    }
}

// L-11 en su propia base (IClassFixture por clase): depende del número de zonas de recepción.
public sealed class LastReceivingZoneTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task L11_the_last_active_receiving_zone_cannot_be_retired_even_concurrently()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var only = await CreateLocationAsync(api, "Receiving");
        Assert.True(only.Resource.GetProperty("isLastActiveReceiving").GetBoolean());

        foreach (var attempt in new[]
                 {
                     UpdateAsync(api, only.Id, only.Resource, only.ETag, isActive: false),
                     UpdateAsync(api, only.Id, only.Resource, only.ETag, kind: "Storage")
                 })
        {
            var response = await attempt;
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("NoActiveReceivingLeft", problem.GetProperty("code").GetString());
            Assert.Contains("otra zona de recepción", problem.GetProperty("detail").GetString());
        }
        var (read, _) = await GetAsync(api, only.Id);
        Assert.Equal(("Receiving", true), (read.GetProperty("kind").GetString(), read.GetProperty("isActive").GetBoolean()));
        Assert.Equal(0, await support.AuditCountAsync(only.Id, MasterDataAction.Deactivate));

        // Con dos zonas, dos retiradas simultáneas: exactamente una pasa.
        for (var round = 0; round < 3; round++)
        {
            var second = await CreateLocationAsync(api, "Receiving");
            var (first, firstTag) = await GetAsync(api, only.Id);
            var responses = await Task.WhenAll(
                UpdateAsync(api, only.Id, first, firstTag, isActive: false),
                UpdateAsync(api, second.Id, second.Resource, second.ETag, isActive: false));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("NoActiveReceivingLeft", await ProblemCodeAsync(rejected));
            // Deja «only» como única zona activa para la siguiente ronda.
            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                var (back, backTag) = await GetAsync(api, only.Id);
                Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(api, only.Id, back, backTag, isActive: true)).StatusCode);
                var (other, otherTag) = await GetAsync(api, second.Id);
                Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(api, second.Id, other, otherTag, isActive: false)).StatusCode);
            }
        }
    }

    [Fact]
    public async Task Receiving_zones_lock_goes_after_location_locks_in_the_same_phase()
    {
        await using var database = fixture.ApplicationDatabase();
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            var locks = new InventoryLocks(database);
            await locks.LocationsAsync([Guid.NewGuid()]);
            await locks.ReceivingZonesAsync();
            await transaction.RollbackAsync();
        }
        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            var locks = new InventoryLocks(database);
            await locks.ReceivingZonesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => locks.LocationsAsync([Guid.NewGuid()]));
            await transaction.RollbackAsync();
        }
    }
}
