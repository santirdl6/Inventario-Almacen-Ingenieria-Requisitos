using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class StockAlertQueryTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Only_alert_permission_can_list_a_new_alert_with_correct_suggestion()
    {
        using var factory = fixture.Factory();
        using var owner = await MasterDataApi.LoginAsync(fixture, factory, "GestorMaestros", "ResponsableAlmacen");
        using var reader = await MasterDataApi.LoginAsync(fixture, factory, "Comprador");
        using var inventoryOnly = await MasterDataApi.LoginAsync(fixture, factory, "Operario");
        var family = await owner.FamilyAsync();
        var created = await owner.WriteAsync(HttpMethod.Post, "/api/v1/articles", MasterDataArticleTests.Body(family, "ALERT-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resource").GetProperty("id").GetGuid();
        var policyPath = $"/api/v1/articles/{id:D}/stock-policy";
        var policy = await owner.Client.GetAsync(policyPath);
        var updated = await owner.WriteAsync(HttpMethod.Put, policyPath, new { minimumAvailable = "5.000" }, version: policy.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var path = $"/api/v1/stock-alerts?articleId={id:D}";
        Assert.Equal(HttpStatusCode.Forbidden, (await inventoryOnly.Client.GetAsync(path)).StatusCode);
        var data = await reader.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var alert = data.GetProperty("items")[0];
        Assert.Equal("5.000", alert.GetProperty("suggestedQuantity").GetString());
        Assert.Equal("Active", alert.GetProperty("state").GetString());
        var detail = await reader.Client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-alerts/{alert.GetProperty("id").GetGuid():D}");
        Assert.Equal(alert.GetProperty("id").GetGuid(), detail.GetProperty("id").GetGuid());
        var term = Uri.EscapeDataString(alert.GetProperty("articleSku").GetString()!);
        var matching = await reader.Client.GetFromJsonAsync<JsonElement>(path + $"&q={term}&pageSize=1");
        Assert.Single(matching.GetProperty("items").EnumerateArray());
        var beyond = await reader.Client.GetFromJsonAsync<JsonElement>(path + "&pageSize=1&page=2");
        Assert.Equal(1, beyond.GetProperty("totalCount").GetInt32());
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        var resolved = await reader.Client.GetFromJsonAsync<JsonElement>(path + "&state=Resolved");
        Assert.Empty(resolved.GetProperty("items").EnumerateArray());
    }
}
