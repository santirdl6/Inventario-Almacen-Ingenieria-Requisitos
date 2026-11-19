using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Stock;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Locations.LocationsTestSupport;

namespace Inventario.IntegrationTests.Locations;

// US5 — Guardar cambios con autorización y resultado fiable (quickstart L-04, L-09, L-10, L-12, L-15).
public sealed class LocationsReliabilityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task L09_deactivation_and_receipt_at_the_same_time_never_leave_stock_in_an_inactive_location()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var article = await support.ArticleAsync();
        int deactivated = 0, received = 0;
        for (var round = 0; round < 20; round++)
        {
            var location = await CreateLocationAsync(api);
            var deactivate = UpdateAsync(api, location.Id, location.Resource, location.ETag, isActive: false);
            var receive = Task.Run(async () =>
            {
                try { await support.ReceiveAsync(article, location.Id, 1); return (InventoryProblemException?)null; }
                catch (InventoryProblemException problem) { return problem; }
            });
            var response = await deactivate;
            var rejection = await receive;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                deactivated++;
                Assert.NotNull(rejection);
                Assert.Contains("desactivada", rejection!.Message);
            }
            else
            {
                received++;
                Assert.Null(rejection);
                Assert.Equal("LocationNotEmpty", await ProblemCodeAsync(response));
            }
        }
        Assert.Equal(20, deactivated + received);
        await using var database = fixture.ApplicationDatabase();
        Assert.False(await (from balance in database.Set<StockBalance>()
                            join location in database.Set<Location>() on balance.LocationId equals location.Id
                            where !location.IsActive && balance.Physical > 0
                            select balance).AnyAsync());
    }

    [Fact]
    public async Task L10_stale_versions_are_rejected_replays_return_the_saved_result_and_keys_cannot_change_content()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var location = await CreateLocationAsync(api);
        var key = Guid.NewGuid();

        var first = await UpdateAsync(api, location.Id, location.Resource, location.ETag, key, level: "N02");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await UpdateAsync(api, location.Id, location.Resource, location.ETag, level: "N03");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("VersionConflict", await ProblemCodeAsync(stale));
        Assert.Equal("N02", (await GetAsync(api, location.Id)).Body.GetProperty("level").GetString());

        var missing = await api.WriteAsync(HttpMethod.Put, $"/api/v1/locations/{location.Id:D}", new
        {
            code = location.Resource.GetProperty("code").GetString(), aisle = location.Resource.GetProperty("aisle").GetString(),
            rack = "E01", level = "N04", kind = "Storage", isActive = true
        });
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);

        // Replay con la misma clave y el ETag ya obsoleto: devuelve el resultado guardado.
        var replay = await UpdateAsync(api, location.Id, location.Resource, location.ETag, key, level: "N02");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await support.AuditCountAsync(location.Id, MasterDataAction.Update));

        var conflict = await UpdateAsync(api, location.Id, location.Resource, location.ETag, key, level: "N07");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IdempotencyConflict", await ProblemCodeAsync(conflict));
    }

    [Fact]
    public async Task A_held_location_lock_returns_503_LockTimeout_with_RetryAfter_and_changes_nothing()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var location = await CreateLocationAsync(api);

        // Otra transacción (p. ej. una recepción en curso) retiene location:{id} con el mismo recurso.
        await using var holder = fixture.ApplicationDatabase();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await new InventoryLocks(holder).LocationsAsync([location.Id]);

        var blocked = await UpdateAsync(api, location.Id, location.Resource, location.ETag, isActive: false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Equal("LockTimeout", await ProblemCodeAsync(blocked));
        Assert.True(blocked.Headers.RetryAfter is not null, "El 503 debe indicar Retry-After.");
        await transaction.RollbackAsync();

        var (read, _) = await GetAsync(api, location.Id);
        Assert.True(read.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, await support.AuditCountAsync(location.Id, MasterDataAction.Deactivate));
    }

    [Fact]
    public async Task L04_simultaneous_creations_with_the_same_key_create_one_location()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var key = Guid.NewGuid();
        var aisle = "I" + Unique(8);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CreateAsync(api, Body("IDEM-" + aisle, aisle), key)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        var search = await api.Client.GetFromJsonAsync<JsonElement>($"/api/v1/locations?search=IDEM-{aisle}&status=all");
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task L12_mutations_need_a_session_antiforgery_and_an_idempotency_key()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var body = Body("SEC-" + Unique(), "S" + Unique(6));

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/locations", body)).StatusCode);

        using var withoutToken = new HttpRequestMessage(HttpMethod.Post, "/api/v1/locations") { Content = JsonContent.Create(body) };
        withoutToken.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var antiforgery = await api.Client.SendAsync(withoutToken);
        Assert.Equal(HttpStatusCode.BadRequest, antiforgery.StatusCode);
        Assert.Equal("AntiforgeryValidationFailed", await ProblemCodeAsync(antiforgery));

        using var withoutKey = new HttpRequestMessage(HttpMethod.Post, "/api/v1/locations") { Content = JsonContent.Create(body) };
        withoutKey.Headers.Add("X-XSRF-TOKEN", api.Token);
        var noKey = await api.Client.SendAsync(withoutKey);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        var problem = await noKey.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidRequest", problem.GetProperty("code").GetString());
        Assert.Contains("Idempotency-Key", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task L15_a_failure_before_commit_leaves_nothing_and_the_retry_confirms_one_effect()
    {
        var interceptor = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, interceptor);
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var aisle = "F" + Unique(8);
        var body = Body("FAIL-" + aisle, aisle);
        var key = Guid.NewGuid();

        interceptor.Arm();
        Assert.Equal(HttpStatusCode.InternalServerError, (await CreateAsync(api, body, key)).StatusCode);
        await using (var database = fixture.ApplicationDatabase())
        {
            Assert.False(await database.Set<Location>().AnyAsync(location => location.Aisle == aisle));
            Assert.False(await database.OperationResults.AnyAsync(result => result.OperationId == key));
            Assert.False(await database.Set<MasterDataAudit>().AnyAsync(audit => audit.OperationId == key));
        }
        var retried = await CreateAsync(api, body, key);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        var id = (await retried.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        Assert.Equal(1, await support.AuditCountAsync(id));

        var (current, etag) = await GetAsync(api, id);
        var deactivation = Guid.NewGuid();
        interceptor.Arm();
        Assert.Equal(HttpStatusCode.InternalServerError, (await UpdateAsync(api, id, current, etag, deactivation, isActive: false)).StatusCode);
        Assert.True((await GetAsync(api, id)).Body.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, await support.AuditCountAsync(id, MasterDataAction.Deactivate));
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(api, id, current, etag, deactivation, isActive: false)).StatusCode);
        Assert.Equal(1, await support.AuditCountAsync(id, MasterDataAction.Deactivate));
    }
}
