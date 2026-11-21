using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingRecoveryTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Recovering_a_lost_confirmation_does_not_reapply_stock()
    {
        var (order, line, article, location) = await fixture.SentOrderAsync();
        using var oper = await fixture.AsAsync(); using var reader = await fixture.AsAsync("Comprador");
        using var saved = await oper.WriteAsync(HttpMethod.Post, "/api/v1/receipts",
            ReceivingContractTests.Draft(order, line, location, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var key = Guid.NewGuid(); var path = saved.Headers.Location!.ToString(); var originalTag = saved.Headers.ETag!.ToString();
        using (var ignored = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, key, originalTag))
            Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);

        var operationPath = $"/api/v1/operations/{key}";
        using var denied = await reader.Client.GetAsync(operationPath);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var recovered = await oper.Client.GetFromJsonAsync<JsonElement>(operationPath);
        Assert.Equal(200, recovered.GetProperty("status").GetInt32());
        Assert.Equal("Confirmed", recovered.GetProperty("body").GetProperty("resource").GetProperty("state").GetString());
        using var replay = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, key, originalTag);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(recovered.GetProperty("body").GetProperty("movementId").GetGuid(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movementId").GetGuid());
        using var again = await oper.WriteAsync(HttpMethod.Post, path + "/confirm", new { }, version: replay.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await using var db = fixture.Database.ApplicationDatabase();
        Assert.Equal(6m, (await db.Set<StockBalance>().SingleAsync(value => value.ArticleId == article)).Physical);
        Assert.Equal(1, await db.Set<StockEntry>().CountAsync(value => value.ArticleId == article));
    }
}
