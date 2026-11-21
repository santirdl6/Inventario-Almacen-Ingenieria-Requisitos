using System.Net;
using System.Net.Sockets;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseDeliveryRecoveryTests : IAsyncLifetime
{
    private readonly IdentityFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Automatic_attempts_are_bounded_and_keep_one_purchase_and_message_identity()
    {
        var transport = new PurchaseDeliveryWorkerTests.ScriptedTransport { Result = new(false, false, true, "SmtpAcceptanceUncertain", "Acuse no comprobado", DateTimeOffset.UtcNow) };
        using var support = new PurchasingTestSupport(fixture, configure: services => { services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport); });
        using var buyer = await support.SessionAsync(); var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
        var worker = support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(await worker.RunOnceAsync(CancellationToken.None));
            await using var db = fixture.ApplicationDatabase();
            var outbox = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(value => value.DeliveryId == job.DeliveryId);
            Assert.Equal(attempt, outbox.Attempts); Assert.True(outbox.AcceptanceUncertain);
            if (attempt < 5)
            {
                Assert.Equal(PurchaseOutboxState.Pending, outbox.State);
                var recorded = await db.Set<PurchaseDeliveryEvent>().Where(value => value.DeliveryId == job.DeliveryId && value.Kind == PurchaseDeliveryEventKind.Uncertain)
                    .OrderByDescending(value => value.RecordedAt).Select(value => value.RecordedAt).FirstAsync();
                Assert.InRange((outbox.NextAttemptAt - recorded).TotalSeconds, new[] { 5, 30, 120, 300 }[attempt - 1] - 2, new[] { 5, 30, 120, 300 }[attempt - 1] + 1);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET NextAttemptAt=DATEADD(second,-1,SYSUTCDATETIME()) WHERE DeliveryId={job.DeliveryId}");
            }
            else Assert.Equal(PurchaseOutboxState.Failed, outbox.State);
        }
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(5, transport.Control.Messages.Count);
        Assert.All(transport.Control.Messages, bytes => Assert.Equal(transport.Control.Messages.First(), bytes));
        await using var result = fixture.ApplicationDatabase();
        Assert.Equal(PurchaseSendStatus.Failed, (await result.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId)).SendStatus);
        Assert.Equal(1, await result.Set<PurchaseOrderDelivery>().CountAsync(value => value.PurchaseOrderId == job.OrderId));
    }

    [Fact]
    public async Task Loss_of_the_final_smtp_ack_is_recorded_as_uncertain_not_sent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var server = PurchaseOrderDocumentTests.AcceptAndCloseAsync(listener, timeout.Token, acknowledge: false);
        try
        {
            using var support = new PurchasingTestSupport(fixture, settingsOverrides: new Dictionary<string, string?> {
                ["Smtp:Host"] = "127.0.0.1", ["Smtp:Port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Smtp:Security"] = "None", ["PurchaseDelivery:MaxAttempts"] = "1"
            });
            using var buyer = await support.SessionAsync(); var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
            Assert.True(await support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(timeout.Token)); await server;
            await using var db = fixture.ApplicationDatabase();
            var outbox = await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId);
            Assert.Equal(PurchaseOutboxState.Failed, outbox.State); Assert.True(outbox.AcceptanceUncertain);
            Assert.Equal(PurchaseOrderState.Draft, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId)).State);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Repeated_abandoned_claims_exhaust_the_budget_without_smtp_and_leave_a_public_incident()
    {
        var transport = new PurchaseDeliveryWorkerTests.ScriptedTransport();
        using var support = new PurchasingTestSupport(fixture, configure: services => { services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport); });
        using var buyer = await support.SessionAsync(); var job = await PurchaseDeliveryWorkerTests.PrepareAsync(support, buyer);
        using var scope = support.Factory.Services.CreateScope(); var executor = scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>();
        for (var index = 0; index < 6; index++)
        {
            var result = await executor.ExecuteDeliveryAsync(new PurchaseDeliveryCommand(Guid.NewGuid(), PurchaseDeliveryAction.Claim,
                job.DeliveryId, job.Authorization, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
            Assert.Equal(index < 5, System.Text.Json.JsonDocument.Parse(result.Body!).RootElement.GetProperty("applied").GetBoolean());
            if (index < 5)
            {
                await using var db = fixture.ApplicationDatabase();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET LeaseUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE DeliveryId={job.DeliveryId}");
            }
        }
        Assert.Empty(transport.Control.Messages);
        await using var final = fixture.ApplicationDatabase();
        var outbox = await final.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId);
        Assert.Equal(5, outbox.Attempts); Assert.Equal(PurchaseOutboxState.Failed, outbox.State);
        Assert.True(outbox.AcceptanceUncertain); Assert.False(string.IsNullOrWhiteSpace(outbox.LastErrorSummary));
    }

    [Fact]
    public async Task Disabled_host_preserves_the_queue_and_a_restarted_enabled_host_resumes_it()
    {
        var transport = new PurchaseDeliveryWorkerTests.ScriptedTransport();
        void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        { services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport); }
        (Guid OrderId, Guid DeliveryId, Guid Authorization, string Path) job;
        using (var paused = new PurchasingTestSupport(fixture, configure: Configure))
        {
            using var buyer = await paused.SessionAsync(); job = await PurchaseDeliveryWorkerTests.PrepareAsync(paused, buyer);
            Assert.Empty(transport.Control.Messages);
            await using var db = fixture.ApplicationDatabase();
            var order = await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId);
            Assert.Equal(PurchaseSendStatus.Pending, order.SendStatus); Assert.False(order.IsEditable);
        }
        using var resumed = new PurchasingTestSupport(fixture, configure: Configure, settingsOverrides: new Dictionary<string, string?> { ["PurchaseDelivery:Enabled"] = "true", ["PurchaseDelivery:PollSeconds"] = "1" });
        using var client = resumed.Factory.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await transport.Control.Entered.Task.WaitAsync(deadline.Token);
        using var polling = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (true)
        {
            await using var db = fixture.ApplicationDatabase();
            if ((await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId, deadline.Token)).SendStatus == PurchaseSendStatus.Accepted) break;
            await polling.WaitForNextTickAsync(deadline.Token);
        }
        Assert.Single(transport.Control.Messages);
    }
}
