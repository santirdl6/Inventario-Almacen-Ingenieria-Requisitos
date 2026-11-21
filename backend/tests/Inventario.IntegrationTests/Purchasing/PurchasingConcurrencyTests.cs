using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingConcurrencyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_stale_reassignment_cannot_overwrite_a_committed_line_change()
    {
        var barrier = new MasterDataConcurrencyTests.SaveBarrier(); using var support = new PurchasingTestSupport(fixture, barrier);
        var (supplier, article) = await support.MastersAsync(); var (otherSupplier, otherArticle) = await support.MastersAsync();
        using var buyer = await support.SessionAsync(); var lineId = Guid.NewGuid();
        using var draft = await buyer.WriteAsync(HttpMethod.Post, "/api/v1/purchase-orders", PurchasingTestSupport.Draft(supplier, article, lineId: lineId));
        var path = draft.Headers.Location!.ToString(); var id = Guid.Parse(path.Split('/')[^1]);
        barrier.Predicate = value => value is PurchaseOrder order && order.Id == id;
        var first = buyer.WriteAsync(HttpMethod.Put, path, PurchasingTestSupport.Draft(supplier, article, "6", lineId), version: draft.Headers.ETag!.ToString());
        Task<HttpResponseMessage> second;
        try { await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); second = buyer.WriteAsync(HttpMethod.Put, path,
            PurchasingTestSupport.Draft(otherSupplier, otherArticle), version: draft.Headers.ETag!.ToString()); await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture); }
        finally { barrier.Release.TrySetResult(); }
        using var committed = await first; using var rejected = await second;
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode); Assert.Equal(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        var current = await buyer.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(supplier, current.GetProperty("supplierId").GetGuid()); Assert.Equal("6.000", current.GetProperty("lines")[0].GetProperty("orderedQuantity").GetString());
    }
}
