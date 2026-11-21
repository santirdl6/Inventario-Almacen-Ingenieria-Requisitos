using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Domain.Receiving;
using Inventario.IntegrationTests.Fixtures;
using Inventario.IntegrationTests.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingContingencyTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Concurrent_imports_of_same_voucher_leave_one_receipt_and_one_stock_effect()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        var barrier = new MasterDataConcurrencyTests.SaveBarrier();
        using var support = new PurchasingTestSupport(fixture.Database, barrier);
        using var first = await MasterDataApi.LoginAsync(fixture.Database, support.Factory, "Operario", "ResponsableAlmacen");
        using var second = await MasterDataApi.LoginAsync(fixture.Database, support.Factory, "Operario", "ResponsableAlmacen");
        var voucher = "VC-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var body = new { type = "Receive", voucherId = voucher, responsibleReference = "Operario", receipt =
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()) };
        barrier.Predicate = entity => entity is Receipt receipt && receipt.PurchaseOrderId == order && receipt.State == ReceiptState.Confirmed;
        var left = first.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body);
        Task<HttpResponseMessage> right;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            right = second.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body);
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture.Database);
        }
        finally { barrier.Release.TrySetResult(); }
        using var completed = await left; using var rejected = await right;
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.Single(await db.Set<ContingencyReference>().Where(value => value.VoucherId == voucher).ToListAsync());
        Assert.Single(await db.Set<StockEntry>().Where(value => value.ArticleId == article).ToListAsync());
        Assert.Equal(6m, (await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article)).Physical);
    }

    [Fact]
    public async Task Reopening_requires_all_expected_vouchers_evidence_and_resolved_differences()
    {
        var (order, line, _, location) = await fixture.SentOrderAsync();
        using var both = await fixture.AsContingencyAsync(); using var adjuster = await fixture.AsAsync("ResponsableAlmacen");
        using var noAdjust = await fixture.AsAsync("Operario");
        var voucher = "REOPEN-01";
        var scopeBody = new { purchaseOrderIds = new[] { order }, locationIds = new[] { location }, expectedVouchers = new[] { voucher },
            differences = new[] { new { id = Guid.NewGuid(), description = "Diferencia pendiente", resolved = false, resolutionReference = (string?)null } },
            journalEvidence = "DIARIO-1", stockEvidence = "STOCK-1", purchaseEvidence = "PEDIDO-1" };
        using var created = await adjuster.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/receiving-scopes", scopeBody);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var path = created.Headers.Location!.ToString();
        var id = Guid.Parse(path.Split('/')[^1]);
        using var forbidden = await noAdjust.WriteAsync(HttpMethod.Post, path + "/reopen", new { }, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var blocked = await adjuster.WriteAsync(HttpMethod.Post, path + "/reopen", new { }, version: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = new { type = "Receive", voucherId = voucher, responsibleReference = "Operario", receipt =
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()) };
        using var imported = await both.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body);
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        using var stillBlocked = await adjuster.WriteAsync(HttpMethod.Post, path + "/reopen", new { }, version: created.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.Conflict, stillBlocked.StatusCode);
        var corrected = new { purchaseOrderIds = new[] { order }, locationIds = new[] { location }, expectedVouchers = new[] { voucher },
            differences = new[] { new { id = scopeBody.differences[0].id, description = "Diferencia pendiente", resolved = true, resolutionReference = "AJUSTE-1" } },
            journalEvidence = "DIARIO-1", stockEvidence = "STOCK-1", purchaseEvidence = "PEDIDO-1" };
        using var updated = await adjuster.WriteAsync(HttpMethod.Put, path, corrected, version: created.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var obsolete = await adjuster.WriteAsync(HttpMethod.Post, path + "/reopen", new { }, version: created.Headers.ETag.ToString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, obsolete.StatusCode);
        using var authorized = await adjuster.WriteAsync(HttpMethod.Post, path + "/reopen", new { }, version: updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var result = (await authorized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource");
        Assert.Equal("ReopeningAuthorized", result.GetProperty("state").GetString());
        Assert.NotEqual(Guid.Empty, result.GetProperty("authorizedBy").GetGuid());
        Assert.Equal("DIARIO-1", result.GetProperty("journalEvidence").GetString());
        await using var db = fixture.Database.ApplicationDatabase();
        var denied = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.ReceiptContingencyScopes SET JournalEvidence='rewritten' WHERE Id={id}"));
        Assert.Equal(51001, denied.Number);
    }

    [Fact]
    public async Task Voucher_retries_are_idempotent_and_require_both_permissions()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        using var both = await fixture.AsContingencyAsync(); using var onlyReceive = await fixture.AsAsync();
        using var onlyAdjust = await fixture.AsAsync("ResponsableAlmacen");
        var body = new { type = "Receive", voucherId = "  cb-001  ", responsibleReference = "Operario en contingencia",
            receipt = ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()) };
        var key = Guid.NewGuid();
        using var denied1 = await onlyReceive.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body);
        using var denied2 = await onlyAdjust.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body);
        Assert.Equal(HttpStatusCode.Forbidden, denied1.StatusCode); Assert.Equal(HttpStatusCode.Forbidden, denied2.StatusCode);
        using var first = await both.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var replay = await both.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body, key);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var another = await both.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", body);
        Assert.Equal(HttpStatusCode.Conflict, another.StatusCode);
        Assert.Equal("ContingencyAlreadyReconciled", (await another.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var changed = new { type = "Receive", voucherId = "CB-001", responsibleReference = "Otro responsable", receipt = body.receipt };
        using var conflict = await both.WriteAsync(HttpMethod.Post, "/api/v1/contingencies/reconcile", changed);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("ContingencyConflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.Equal(6m, (await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article)).Physical);
        Assert.Single(await db.Set<ContingencyReference>().Where(value => value.VoucherId == "CB-001").ToListAsync());
        Assert.Single(await db.Set<StockEntry>().Where(value => value.ArticleId == article).ToListAsync());
    }
}
