using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Inventario.IntegrationTests.Purchasing;

public sealed class PurchasingSupportTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Support_creates_real_masters_and_a_separate_buyer_session()
    {
        using var support = new PurchasingTestSupport(fixture);
        var (supplier, article) = await support.MastersAsync();
        Assert.NotEqual(Guid.Empty, supplier);
        Assert.NotEqual(Guid.Empty, article);
        using var buyer = await support.SessionAsync();
        var me = await buyer.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Contains(me.GetProperty("permissions").EnumerateArray(), permission => permission.GetString() == "Purchasing.Send");
        Assert.DoesNotContain(me.GetProperty("permissions").EnumerateArray(), permission => permission.GetString() == "MasterData.Manage");
    }

    [Fact]
    public async Task Controlled_transport_observes_copies_and_coordinates_without_sleeps()
    {
        var transport = new ControlledPurchaseMailTransport();
        transport.PauseNext();
        var bytes = new byte[] { 1, 2, 3 };
        var sending = transport.SendAsync(bytes, CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(sending.IsCompleted);
        bytes[0] = 9;
        transport.Release.TrySetResult();
        await sending;
        Assert.Equal((byte)1, Assert.Single(transport.Messages)[0]);
    }
}
