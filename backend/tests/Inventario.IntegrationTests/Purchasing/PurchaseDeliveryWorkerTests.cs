using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseDeliveryWorkerTests : IAsyncLifetime
{
    private readonly IdentityFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    internal sealed class ScriptedTransport : IPurchaseMailTransport
    {
        public ControlledPurchaseMailTransport Control { get; } = new();
        public PurchaseMailObservation Result { get; set; } = new(true, false, false, null, null, DateTimeOffset.UtcNow);
        public async Task<PurchaseMailObservation> SendAsync(byte[] message, CancellationToken cancellation)
        { await Control.SendAsync(message, cancellation); return Result with { ObservedAt = DateTimeOffset.UtcNow }; }
    }

    private PurchasingTestSupport Support(ScriptedTransport transport, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null) => new(fixture, interceptor, configure: services =>
    { services.RemoveAll<IPurchaseMailTransport>(); services.AddSingleton<IPurchaseMailTransport>(transport); });

    internal static async Task<(Guid OrderId, Guid DeliveryId, Guid Authorization, string Path)> PrepareAsync(
        PurchasingTestSupport support, MasterDataApi buyer, (Guid SupplierId, Guid ArticleId)? masters = null)
    {
        var (supplier, article) = masters ?? await support.MastersAsync();
        using var draft = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var path = draft.Headers.Location!.ToString();
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        var key = Guid.NewGuid();
        using var send = await buyer.WriteAsync(HttpMethod.Post, path + "/send",
            new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() }, key, draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
        return (Guid.Parse(path.Split('/')[^1]), (await send.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deliveryId").GetGuid(), key, path);
    }

    [Fact]
    public async Task Worker_without_http_session_accepts_the_frozen_message_once_and_preserves_the_initial_result()
    {
        var transport = new ScriptedTransport();
        using var support = Support(transport); using var buyer = await support.SessionAsync();
        var job = await PrepareAsync(support, buyer);
        var email = (await buyer.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("email").GetString();
        using var ended = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        var worker = support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>();
        Assert.True(await worker.RunOnceAsync(CancellationToken.None));
        Assert.False(await worker.RunOnceAsync(CancellationToken.None));
        await using var db = fixture.ApplicationDatabase();
        var delivery = await db.Set<PurchaseOrderDelivery>().SingleAsync(value => value.Id == job.DeliveryId);
        Assert.Equal(delivery.MimeBytes, Assert.Single(transport.Control.Messages));
        var order = await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId);
        Assert.Equal(PurchaseSendStatus.Accepted, order.SendStatus); Assert.Equal(PurchaseOrderState.Sent, order.State);
        var original = await db.OperationResults.SingleAsync(value => value.OperationId == job.Authorization);
        Assert.Equal(202, original.ResultStatus);
        Assert.Equal("Pending", JsonDocument.Parse(original.ResultBody!).RootElement.GetProperty("sendStatus").GetString());
        Assert.Equal(1, await db.Set<PurchaseDeliveryEvent>().CountAsync(value => value.DeliveryId == job.DeliveryId && value.Kind == PurchaseDeliveryEventKind.Accepted));
        var csrf = await buyer.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { email, password = IdentityFixture.Password }) };
        loginRequest.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        using var login = await buyer.Client.SendAsync(loginRequest); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var resumed = new MasterDataApi(buyer.Client, (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString()!);
        var historicalTag = JsonSerializer.Deserialize<Dictionary<string, string>>(original.ResultHeaders)!["ETag"];
        using var replay = await resumed.WriteAsync(HttpMethod.Post, job.Path + "/send", new { kind = "initial", reviewToken = delivery.ReviewedToken }, job.Authorization, historicalTag);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode); Assert.Equal(original.ResultBody, await replay.Content.ReadAsStringAsync());
        Assert.Single(transport.Control.Messages);
    }

    [Fact]
    public async Task Two_claims_do_not_grant_two_owners_and_a_replayed_claim_does_not_increment_attempts()
    {
        using var support = Support(new ScriptedTransport()); using var buyer = await support.SessionAsync();
        var job = await PrepareAsync(support, buyer);
        var one = new PurchaseDeliveryCommand(Guid.NewGuid(), PurchaseDeliveryAction.Claim, job.DeliveryId, job.Authorization,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var two = one with { OperationId = Guid.NewGuid(), WorkerInstanceId = Guid.NewGuid(), AttemptId = Guid.NewGuid(), FinishOperationId = Guid.NewGuid() };
        using var scope = support.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>();
        var results = await Task.WhenAll(executor.ExecuteDeliveryAsync(one), executor.ExecuteDeliveryAsync(two));
        Assert.Equal(1, results.Count(value => JsonDocument.Parse(value.Body!).RootElement.GetProperty("applied").GetBoolean()));
        var replay = await executor.ExecuteDeliveryAsync(one);
        Assert.Equal(results[0].Body, replay.Body);
        await using var db = fixture.ApplicationDatabase();
        var outbox = await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId);
        Assert.Equal(1, outbox.Attempts); Assert.Equal(1, outbox.LeaseGeneration);
    }

    [Fact]
    public async Task An_expired_owner_cannot_finalize_after_a_new_generation()
    {
        using var support = Support(new ScriptedTransport()); using var buyer = await support.SessionAsync();
        var job = await PrepareAsync(support, buyer);
        using var scope = support.Factory.Services.CreateScope(); var executor = scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>();
        var first = new PurchaseDeliveryCommand(Guid.NewGuid(), PurchaseDeliveryAction.Claim, job.DeliveryId, job.Authorization,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await executor.ExecuteDeliveryAsync(first);
        await using var db = fixture.ApplicationDatabase();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET LeaseUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE DeliveryId={job.DeliveryId}");
        var second = first with { OperationId = Guid.NewGuid(), WorkerInstanceId = Guid.NewGuid(), AttemptId = Guid.NewGuid(), FinishOperationId = Guid.NewGuid() };
        var grant = await executor.ExecuteDeliveryAsync(second);
        Assert.Equal(2, JsonDocument.Parse(grant.Body!).RootElement.GetProperty("generation").GetInt64());
        var stale = first with { OperationId = first.FinishOperationId, Action = PurchaseDeliveryAction.Finish, LeaseGeneration = 1,
            Observation = new(true, false, false, null, null, DateTimeOffset.UtcNow) };
        var result = await executor.ExecuteDeliveryAsync(stale);
        Assert.False(JsonDocument.Parse(result.Body!).RootElement.GetProperty("applied").GetBoolean());
        var current = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(value => value.DeliveryId == job.DeliveryId);
        Assert.Equal(second.WorkerInstanceId, current.LeaseOwner); Assert.Equal(PurchaseOutboxState.Processing, current.State);
        Assert.Equal(PurchaseSendStatus.Pending, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId)).SendStatus);
    }

    [Fact]
    public async Task Missing_persisted_authority_is_rejected_without_claim_or_smtp()
    {
        var transport = new ScriptedTransport(); using var support = Support(transport); using var buyer = await support.SessionAsync();
        var job = await PrepareAsync(support, buyer);
        using var scope = support.Factory.Services.CreateScope(); var executor = scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>();
        var command = new PurchaseDeliveryCommand(Guid.NewGuid(), PurchaseDeliveryAction.Claim, job.DeliveryId, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var error = await Assert.ThrowsAsync<InventoryProblemException>(() => executor.ExecuteDeliveryAsync(command));
        Assert.Equal(403, error.Status); Assert.Empty(transport.Control.Messages);
        using var hidden = await buyer.WriteAsync(HttpMethod.Post, $"/api/v1/internal/purchase-deliveries/{job.DeliveryId:D}/claim", new { });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(0, (await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId)).Attempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejection_or_uncertainty_never_marks_the_purchase_sent(bool permanent)
    {
        var transport = new ScriptedTransport { Result = new(false, permanent, !permanent, "SmtpUnavailable",
            "No se pudo confirmar este intento de correo.", DateTimeOffset.UtcNow) };
        using var support = Support(transport); using var buyer = await support.SessionAsync();
        var job = await PrepareAsync(support, buyer);
        Assert.True(await support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        await using var db = fixture.ApplicationDatabase();
        var order = await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId);
        Assert.Equal(PurchaseOrderState.Draft, order.State);
        Assert.Equal(permanent ? PurchaseSendStatus.Failed : PurchaseSendStatus.Pending, order.SendStatus);
        Assert.False(order.IsEditable); Assert.Null(order.SentAt);
        var progress = await buyer.Client.GetFromJsonAsync<JsonElement>(job.Path + "/delivery");
        Assert.Equal(permanent, progress.GetProperty("canRetry").GetBoolean());
        Assert.Equal(!permanent, progress.GetProperty("acceptanceUncertain").GetBoolean());
        Assert.False(progress.TryGetProperty("leaseOwner", out _));
        Assert.Equal(1, progress.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task A_local_finish_failure_repeats_the_same_finish_without_another_smtp_call()
    {
        var failure = new FailAfterSaveInterceptor(); var transport = new ScriptedTransport();
        using var support = Support(transport, failure); using var buyer = await support.SessionAsync();
        var job = await PrepareAsync(support, buyer);
        transport.Control.Enqueue(_ => { failure.Arm(); return Task.CompletedTask; });
        Assert.True(await support.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        Assert.Single(transport.Control.Messages);
        await using var db = fixture.ApplicationDatabase();
        Assert.Equal(PurchaseSendStatus.Accepted, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId)).SendStatus);
        Assert.Equal(1, await db.Set<PurchaseDeliveryEvent>().CountAsync(value => value.DeliveryId == job.DeliveryId && value.Kind == PurchaseDeliveryEventKind.Accepted));
        Assert.Equal(1, (await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId)).Attempts);
    }

    [Fact]
    public async Task A_recreated_application_recovers_an_abandoned_claim_from_persisted_state()
    {
        (Guid OrderId, Guid DeliveryId, Guid Authorization, string Path) job;
        using (var original = Support(new ScriptedTransport()))
        {
            using var buyer = await original.SessionAsync(); job = await PrepareAsync(original, buyer);
            using var scope = original.Factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteDeliveryAsync(new PurchaseDeliveryCommand(
                Guid.NewGuid(), PurchaseDeliveryAction.Claim, job.DeliveryId, job.Authorization, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        }
        await using (var db = fixture.ApplicationDatabase())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.OutboxMessages SET LeaseUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE DeliveryId={job.DeliveryId}");
        var transport = new ScriptedTransport();
        using var recovered = Support(transport);
        Assert.True(await recovered.Factory.Services.GetRequiredService<PurchaseDeliveryWorker>().RunOnceAsync(CancellationToken.None));
        Assert.Single(transport.Control.Messages);
        await using var result = fixture.ApplicationDatabase();
        var outbox = await result.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == job.DeliveryId);
        Assert.Equal(2, outbox.Attempts); Assert.Equal(2, outbox.LeaseGeneration); Assert.True(outbox.AcceptanceUncertain);
        Assert.Equal(PurchaseSendStatus.Accepted, (await result.Set<PurchaseOrder>().SingleAsync(value => value.Id == job.OrderId)).SendStatus);
    }
}
