using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingAtomicityAuditTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_failure_after_the_complete_result_graph_rolls_back_every_fact_and_the_claim_can_be_recovered()
    {
        var failure = new FinalSaveFault(); using var support = new PurchasingTestSupport(fixture, failure);
        var (supplier, article) = await support.MastersAsync(); using var buyer = await support.SessionAsync();
        using var draft = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        var path = draft.Headers.Location!.ToString(); var id = Guid.Parse(path.Split('/')[^1]);
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        var key = Guid.NewGuid(); failure.Target = key;
        var body = new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() };
        using var failed = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = fixture.ApplicationDatabase())
        {
            Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
            Assert.False(await db.Set<PurchaseOrderDelivery>().AnyAsync(value => value.PurchaseOrderId == id));
            Assert.False(await db.Set<PurchaseDeliveryEvent>().AnyAsync(value => value.OperationId == key));
            Assert.False(await db.Set<PurchaseOrderAudit>().AnyAsync(value => value.OperationId == key));
            Assert.Equal(PurchaseSendStatus.None, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == id)).SendStatus);
        }
        using var confirmed = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, draft.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
        var delivery = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deliveryId").GetGuid();
        var claim = new PurchaseDeliveryCommand(Guid.NewGuid(), PurchaseDeliveryAction.Claim, delivery, key, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        failure.Target = claim.OperationId;
        using var scope = support.Factory.Services.CreateScope(); var executor = scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteDeliveryAsync(claim));
        await using (var db = fixture.ApplicationDatabase())
        {
            Assert.Equal(0, (await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == delivery)).Attempts);
            Assert.False(await db.Set<PurchaseDeliveryEvent>().AnyAsync(value => value.OperationId == claim.OperationId));
            Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == claim.OperationId));
        }
        var recovered = await executor.ExecuteDeliveryAsync(claim); Assert.True(JsonDocument.Parse(recovered.Body!).RootElement.GetProperty("applied").GetBoolean());
    }

    private sealed class FinalSaveFault : SaveChangesInterceptor
    {
        internal Guid Target { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Target != Guid.Empty && eventData.Context!.ChangeTracker.Entries<OperationResult>().Any(value => value.Entity.OperationId == Target))
            { Target = Guid.Empty; throw new InvalidOperationException("Fallo controlado después del grafo SQL completo, antes del commit."); }
            return ValueTask.FromResult(result);
        }
    }
}
