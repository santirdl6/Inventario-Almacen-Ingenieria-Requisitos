using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Inventario.IntegrationTests.Stock;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchaseOrderSendTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Initial_send_persists_one_frozen_message_and_replays_the_pending_result()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync();
        var draft = PurchasingTestSupport.Draft(supplier, article);
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", draft);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = created.Headers.Location!.ToString();
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        Assert.Equal("compras@proveedor.test", preview.GetProperty("recipientEmail").GetString());
        Assert.Empty(preview.GetProperty("blockReasons").EnumerateArray());
        var body = new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() };
        var key = Guid.NewGuid();
        using var sent = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, created.Headers.ETag!.ToString());
        Assert.True(sent.StatusCode == HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());
        var response = await sent.Content.ReadAsStringAsync();
        Assert.Equal("Pending", JsonDocument.Parse(response).RootElement.GetProperty("sendStatus").GetString());
        using var replay = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, created.Headers.ETag!.ToString());
        Assert.Equal(response, await replay.Content.ReadAsStringAsync());
        Assert.Equal(sent.Headers.ETag, replay.Headers.ETag);
        using var edited = await buyer.WriteAsync(HttpMethod.Put, path, draft, version: sent.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
        await using var db = fixture.ApplicationDatabase();
        var delivery = await db.Set<PurchaseOrderDelivery>().SingleAsync(value => value.RequestOperationId == key);
        Assert.Equal(SHA256.HashData(delivery.DocumentBytes), delivery.DocumentHash);
        Assert.Equal(SHA256.HashData(delivery.MimeBytes), delivery.MessageHash);
        Assert.NotEmpty(delivery.MimeBytes);
        Assert.Equal(PurchaseOutboxState.Pending, (await db.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == delivery.Id)).State);
        Assert.Equal(1, await db.Set<PurchaseOrderDelivery>().CountAsync(value => value.PurchaseOrderId == delivery.PurchaseOrderId));
        using var document = await buyer.Client.GetAsync(path + "/delivery/document");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(delivery.DocumentBytes, await document.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", document.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", document.Headers.GetValues("X-Content-Type-Options").Single());
        using var manager = await support.SessionAsync("GestorMaestros");
        using var currentSupplier = await manager.Client.GetAsync($"/api/v1/suppliers/{supplier:D}");
        var master = await currentSupplier.Content.ReadFromJsonAsync<JsonElement>();
        using var newContact = await manager.WriteAsync(HttpMethod.Put, $"/api/v1/suppliers/{supplier:D}", new {
            code = master.GetProperty("code").GetString(), name = master.GetProperty("name").GetString(), email = "posterior@proveedor.test", isActive = true
        }, version: currentSupplier.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, newContact.StatusCode);
        var progress = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/delivery");
        Assert.Equal("compras@proveedor.test", progress.GetProperty("recipientEmail").GetString());
        using var historical = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, created.Headers.ETag!.ToString());
        Assert.Equal(response, await historical.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_changed_contact_requires_a_new_review_and_does_not_create_an_outbox_message()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); using var manager = await support.SessionAsync("GestorMaestros");
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        var path = created.Headers.Location!.ToString();
        var old = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        Assert.True(old.TryGetProperty("reviewToken", out var token));
        using var current = await manager.Client.GetAsync($"/api/v1/suppliers/{supplier:D}");
        var value = await current.Content.ReadFromJsonAsync<JsonElement>();
        using var changed = await manager.WriteAsync(HttpMethod.Put, $"/api/v1/suppliers/{supplier:D}", new {
            code = value.GetProperty("code").GetString(), name = value.GetProperty("name").GetString(), email = "cambiado@proveedor.test", isActive = true
        }, version: current.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var rejected = await buyer.WriteAsync(HttpMethod.Post, path + "/send", new { kind = "initial", reviewToken = token.GetString() }, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("SendReviewChanged", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var db = fixture.ApplicationDatabase();
        var orderId = OrderId(created);
        Assert.False(await db.Set<PurchaseOrderDelivery>().AnyAsync(delivery => delivery.PurchaseOrderId == orderId));
    }

    [Fact]
    public async Task Supplier_without_email_allows_a_draft_but_not_an_initial_request()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync(email: null);
        using var buyer = await support.SessionAsync();
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = created.Headers.Location!.ToString();
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        Assert.NotEmpty(preview.GetProperty("blockReasons").EnumerateArray());
        using var rejected = await buyer.WriteAsync(HttpMethod.Post, path + "/send",
            new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() }, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var id = OrderId(created);
        await using var db = fixture.ApplicationDatabase();
        Assert.False(await db.Set<PurchaseOrderDelivery>().AnyAsync(value => value.PurchaseOrderId == id));
        Assert.Equal(PurchaseSendStatus.None, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == id)).SendStatus);
    }

    private static Guid OrderId(HttpResponseMessage response) => Guid.Parse(response.Headers.Location!.ToString().Split('/')[^1]);

    [Fact]
    public async Task Send_failure_before_commit_does_not_leave_a_frozen_order_delivery_or_success_result()
    {
        var failure = new FailAfterSaveInterceptor();
        using var support = new PurchasingTestSupport(fixture, failure);
        var (supplier, article) = await support.MastersAsync(); using var buyer = await support.SessionAsync();
        using var created = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article));
        var path = created.Headers.Location!.ToString(); var id = OrderId(created);
        var preview = await buyer.Client.GetFromJsonAsync<JsonElement>(path + "/send-preview");
        var body = new { kind = "initial", reviewToken = preview.GetProperty("reviewToken").GetString() }; var key = Guid.NewGuid();
        failure.Arm();
        using var failed = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = fixture.ApplicationDatabase())
        {
            Assert.Equal(PurchaseSendStatus.None, (await db.Set<PurchaseOrder>().SingleAsync(value => value.Id == id)).SendStatus);
            Assert.False(await db.Set<PurchaseOrderDelivery>().AnyAsync(value => value.PurchaseOrderId == id));
            Assert.False(await db.OperationResults.AnyAsync(value => value.OperationId == key));
        }
        using var retried = await buyer.WriteAsync(HttpMethod.Post, path + "/send", body, key, created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Accepted, retried.StatusCode);
    }
}
