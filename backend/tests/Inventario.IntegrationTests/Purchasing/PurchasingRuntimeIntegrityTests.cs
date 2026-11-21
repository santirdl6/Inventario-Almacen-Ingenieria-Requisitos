using Inventario.Api.Domain.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingRuntimeIntegrityTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Runtime_cannot_persist_invalid_outbox_counters_or_partial_leases()
    {
        using var support = new PurchasingTestSupport(fixture); using var buyer = await support.SessionAsync();
        var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer); await using var db = fixture.ApplicationDatabase();
        var cycle = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET Cycle=0 WHERE DeliveryId={job.DeliveryId}")); Assert.Equal(547, cycle.Number);
        var attempts = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET CycleAttempts=6 WHERE DeliveryId={job.DeliveryId}")); Assert.Equal(547, attempts.Number);
        var lease = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET State='Processing' WHERE DeliveryId={job.DeliveryId}")); Assert.Equal(547, lease.Number);
        Assert.Equal(PurchaseOutboxState.Pending, (await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId)).State);
        // The additive migration suite separately checks DENY on every immutable table with this runtime login.
    }
}
