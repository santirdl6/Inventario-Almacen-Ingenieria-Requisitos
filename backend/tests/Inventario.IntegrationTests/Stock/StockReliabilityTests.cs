using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inventario.IntegrationTests.Stock.StockApiSupport;

namespace Inventario.IntegrationTests.Stock;

// HU-05 T014, V-08 (S-16; US5.2–US5.5, FR-022–FR-024): un fallo tras escribir y antes del commit
// no deja pedido, reserva, movimiento ni OperationResult; repetir la misma intención con la misma
// clave produce un solo efecto. Usa StockTestHarness.FailingFactory sin modificarlo.
public sealed class StockReliabilityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task V08_failure_before_commit_leaves_nothing_and_the_same_key_applies_once()
    {
        var failure = new FailAfterSaveInterceptor();
        using var factory = StockTestHarness.FailingFactory(fixture, failure);
        using var support = new StockApiSupport(fixture, factory);
        var operario = await support.SessionAsync("Operario");
        var responsable = await support.SessionAsync("ResponsableAlmacen");
        var (article, _) = await support.ArticleAsync();
        var location = await support.LocationAsync("F" + Unique(8));
        await support.ReceiveAsync(article, location, 10m);

        // Alta.
        var reference = "PED-" + Unique(8);
        var orderBody = new { externalReference = reference, customerReference = "Cliente Norte",
            lines = new[] { new { articleId = article, quantity = "7" } } };
        var createKey = Guid.NewGuid();
        failure.Arm();
        using (var failed = await operario.WriteAsync(HttpMethod.Post, Orders, orderBody, createKey))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(createKey, db => db.Set<CustomerOrder>().AnyAsync(order => order.ExternalReference == reference));
        Guid orderId;
        using (var created = await operario.WriteAsync(HttpMethod.Post, Orders, orderBody, createKey))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            orderId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        }
        var (order, orderTag) = await OrderAsync(responsable.Client, orderId);
        var line = order.GetProperty("lines")[0].GetProperty("id").GetGuid();

        // Reserva.
        var reserveKey = Guid.NewGuid();
        failure.Arm();
        using (var failed = await PostReservationAsync(responsable, line, orderTag, reserveKey, (location, null, "7")))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(reserveKey, db => db.Set<Reservation>().AnyAsync(value => value.CustomerOrderLineId == line));
        Assert.Equal(0m, (await support.Stock.BalanceAsync(article, location))!.Reserved);
        Assert.Equal(orderTag, (await OrderAsync(responsable.Client, orderId)).ETag);
        Guid reservation;
        using (var reserved = await PostReservationAsync(responsable, line, orderTag, reserveKey, (location, null, "7")))
        {
            Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
            reservation = (await reserved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reservationId").GetGuid();
        }
        var (view, reservationTag) = await ReservationAsync(responsable.Client, reservation);
        var allocation = view.GetProperty("allocations")[0].GetProperty("id").GetGuid();

        // Liberación.
        var releaseKey = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        failure.Arm();
        using (var failed = await PostReleaseAsync(responsable, reservation, reservationTag, releaseKey, releaseId, "Fallo inducido", (allocation, "2")))
            Assert.False(failed.IsSuccessStatusCode);
        await AssertNothingAsync(releaseKey, db => db.Set<StockMovement>().AnyAsync(value => value.SourceDocumentId == releaseId));
        Assert.Equal(7m, (await support.Stock.BalanceAsync(article, location))!.Reserved);
        Assert.Equal(reservationTag, (await ReservationAsync(responsable.Client, reservation)).ETag);
        using (var released = await PostReleaseAsync(responsable, reservation, reservationTag, releaseKey, releaseId, "Fallo inducido", (allocation, "2")))
            Assert.Equal(HttpStatusCode.OK, released.StatusCode);

        // Un solo efecto de cada intención.
        await using var check = fixture.ApplicationDatabase();
        Assert.Equal(1, await check.Set<CustomerOrder>().CountAsync(value => value.ExternalReference == reference));
        Assert.Equal(1, await check.Set<Reservation>().CountAsync(value => value.CustomerOrderLineId == line));
        Assert.Equal(1, await check.Set<StockMovement>().CountAsync(value => value.Kind == MovementKind.Reservation
            && value.SourceDocumentId == reservation));
        Assert.Equal(1, await check.Set<StockMovement>().CountAsync(value => value.Kind == MovementKind.Release
            && value.SourceDocumentId == releaseId));
        foreach (var key in new[] { createKey, reserveKey, releaseKey })
            Assert.Equal(1, await check.OperationResults.CountAsync(value => value.OperationId == key));
        Assert.Equal(5m, (await support.Stock.BalanceAsync(article, location))!.Reserved);
        await support.AssertCommitmentsMatchBalancesAsync(article, location);
    }

    private async Task AssertNothingAsync(Guid key, Func<Inventario.Api.Infrastructure.Persistence.InventoryDbContext, Task<bool>> effect)
    {
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await effect(db));
        Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
    }
}
