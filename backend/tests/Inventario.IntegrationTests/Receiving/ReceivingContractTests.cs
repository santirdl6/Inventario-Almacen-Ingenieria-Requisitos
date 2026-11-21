using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingContractTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    internal static object Draft(Guid order, Guid purchaseLine, Guid location, Guid line, Guid allocation, string amount = "6") => new
    {
        purchaseOrderId = order, occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), supplierDocumentReference = "ALB-6", reason = "Entrada del proveedor",
        lines = new[] { new { id = line, purchaseOrderLineId = purchaseLine, reportedQuantity = amount, acceptedQuantity = amount,
            allocations = new[] { new { id = allocation, locationId = location, quantity = amount } } } }
    };

    [Fact]
    public async Task Draft_has_own_etag_and_only_an_operator_can_change_it()
    {
        var (order, purchaseLine, _, location) = await fixture.SentOrderAsync();
        using var oper = await fixture.AsAsync(); using var reader = await fixture.AsAsync("Comprador");
        var line = Guid.NewGuid(); var allocation = Guid.NewGuid();
        var body = Draft(order, purchaseLine, location, line, allocation);
        using var noToken = await oper.Client.PostAsJsonAsync("/api/v1/receipts", body);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var invalid = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", Draft(order, purchaseLine, location, line, allocation, "1.0001"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        using var denied = await reader.WriteAsync(HttpMethod.Post, "/api/v1/receipts", body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var saved = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", body);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var path = saved.Headers.Location!.ToString(); var original = saved.Headers.ETag!.ToString();
        using var updated = await oper.WriteAsync(HttpMethod.Put, path, Draft(order, purchaseLine, location, line, allocation, "5"), version: original);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(original, updated.Headers.ETag!.ToString());
        using var detail = await reader.Client.GetAsync(path);
        Assert.Equal(updated.Headers.ETag, detail.Headers.ETag);
        using var context = await reader.Client.GetAsync(path + "/context");
        Assert.Null(context.Headers.ETag);
        Assert.Equal(updated.Headers.ETag.ToString(), (await context.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptVersion").GetString());
        using var stale = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: original);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }
}
