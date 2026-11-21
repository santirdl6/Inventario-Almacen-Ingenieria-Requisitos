using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingCorrectionTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Authorized_correction_preserves_original_and_updates_stock_and_purchase_once()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        using var oper = await fixture.AsAsync(); using var adjuster = await fixture.AsAsync("ResponsableAlmacen");
        var allocationId = Guid.NewGuid();
        using var draft = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts",
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), allocationId));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var path = draft.Headers.Location!.ToString();
        using var confirmed = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var movement = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movementId").GetGuid();
        var correctionId = Guid.NewGuid(); var key = Guid.NewGuid();
        var body = new { correctionId, reason = "Error en registro", reference = "AJUSTE-12", lines = new[] { new { receiptAllocationId = allocationId, quantity = "2" } } };
        using var denied = await oper.WriteAsync(HttpMethod.Post, $"/api/v1/movements/{movement}/corrections", body, version: confirmed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var corrected = await adjuster.WriteAsync(HttpMethod.Post, $"/api/v1/movements/{movement}/corrections", body, key, confirmed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        using var replay = await adjuster.WriteAsync(HttpMethod.Post, $"/api/v1/movements/{movement}/corrections", body, key, confirmed.Headers.ETag!.ToString());
        Assert.Equal(await corrected.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var otherKey = await adjuster.WriteAsync(HttpMethod.Post, $"/api/v1/movements/{movement}/corrections", body, version: corrected.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, otherKey.StatusCode);
        var purchase = await oper.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders/{order}");
        Assert.Equal("4.000", purchase.GetProperty("lines")[0].GetProperty("receivedQuantity").GetString());
        Assert.Equal("6.000", purchase.GetProperty("lines")[0].GetProperty("pendingQuantity").GetString());
        using var document = await oper.Client.GetAsync(path);
        Assert.Equal(corrected.Headers.ETag, document.Headers.ETag);
        var history = (await document.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("corrections");
        Assert.Equal(correctionId, history[0].GetProperty("id").GetGuid());
        Assert.Equal("AJUSTE-12", history[0].GetProperty("reference").GetString());
        Assert.Equal("2.000", history[0].GetProperty("lines")[0].GetProperty("quantity").GetString());
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.Equal(4m, (await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article)).Physical);
        Assert.Equal(2, await db.Set<StockEntry>().CountAsync(value => value.ArticleId == article));
        Assert.True(await db.Set<StockMovement>().AnyAsync(value => value.OriginalMovementId == movement && value.Kind == MovementKind.Correction));
        var denial = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.ReceiptCorrections WHERE Id={correctionId}"));
        Assert.Equal(229, denial.Number);
    }

    [Fact]
    public async Task Rejects_correction_exceeding_original_without_altering_evidence()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        using var oper = await fixture.AsAsync(); using var adjuster = await fixture.AsAsync("ResponsableAlmacen");
        var allocation = Guid.NewGuid();
        using var draft = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts",
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), allocation));
        using var confirmed = await oper.WriteAsync(HttpMethod.Post, draft.Headers.Location + "/confirm", new { }, version: draft.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var movement = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movementId").GetGuid();
        using var rejected = await adjuster.WriteAsync(HttpMethod.Post, $"/api/v1/movements/{movement}/corrections",
            new { correctionId = Guid.NewGuid(), reason = "Exceso", lines = new[] { new { receiptAllocationId = allocation, quantity = "7" } } },
            version: confirmed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var purchase = await oper.Client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders/{order}");
        Assert.Equal("6.000", purchase.GetProperty("lines")[0].GetProperty("receivedQuantity").GetString());
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.Equal(6m, (await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article)).Physical);
        Assert.Single(await db.Set<StockEntry>().Where(value => value.ArticleId == article).ToListAsync());
    }
}
