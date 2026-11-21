using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingDiscrepancyTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Preserves_incidents_and_only_confirms_a_valid_complete_distribution()
    {
        var (order, line, article, place) = await fixture.SentOrderAsync();
        using var oper = await fixture.AsAsync();
        var id = Guid.NewGuid(); var incidentId = Guid.NewGuid(); var allocationId = Guid.NewGuid();
        var when = DateTimeOffset.UtcNow;
        var incidents = new[] { new { id = incidentId, kind = "Sustitución", observedDescription = "Pieza ajena al pedido", quantity = "1", observedUnit = "UD", reason = "Mercancía no aceptada" } };
        object Draft(string accepted, string assigned) => new { purchaseOrderId = order, occurredAt = when, supplierDocumentReference = "ALB-X", reason = "Recepción",
            incidents, lines = new[] { new { id, purchaseOrderLineId = line, reportedQuantity = "13", acceptedQuantity = accepted,
                discrepancyReason = "Exceso observado", allocations = new[] { new { id = allocationId, locationId = place, quantity = assigned } } } } };
        using var zero = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts", new { purchaseOrderId = order, occurredAt = when, incidents });
        Assert.Equal(HttpStatusCode.Created, zero.StatusCode);
        var path = zero.Headers.Location!.ToString();
        using var rejected = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: zero.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        using var excess = await oper.WriteAsync(HttpMethod.Put, path, Draft("13", "13"), version: zero.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.OK, excess.StatusCode);
        using var excessConfirmation = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: excess.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, excessConfirmation.StatusCode);
        using var bad = await oper.WriteAsync(HttpMethod.Put, path, Draft("10", "9"), version: excess.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        using var badConfirmation = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: bad.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badConfirmation.StatusCode);
        await using (var db = fixture.Database.ApplicationDatabase()) Assert.False(await db.Set<StockEntry>().AnyAsync(value => value.ArticleId == article));
        using var valid = await oper.WriteAsync(HttpMethod.Put, path, Draft("10", "10"), version: bad.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var confirmed = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: valid.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var receipt = await oper.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(incidentId, receipt.GetProperty("incidents")[0].GetProperty("id").GetGuid());
        Assert.Equal("13.000", receipt.GetProperty("lines")[0].GetProperty("reportedQuantity").GetString());
        await using var after = fixture.Database.ApplicationDatabase();
        Assert.Equal(10m, await after.Set<StockEntry>().Where(value => value.ArticleId == article).SumAsync(value => value.PhysicalDelta));
    }
}
