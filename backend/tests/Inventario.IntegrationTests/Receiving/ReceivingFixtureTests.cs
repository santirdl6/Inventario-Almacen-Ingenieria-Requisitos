using Xunit;

namespace Inventario.IntegrationTests.Receiving;

public sealed class ReceivingFixtureTests(ReceivingFixture fixture) : IClassFixture<ReceivingFixture>
{
    [Fact]
    public async Task Prepares_an_actual_sent_order_for_an_operator()
    {
        var purchase = await fixture.SentOrderAsync();
        using var operatorSession = await fixture.AsAsync();
        Assert.True((await operatorSession.Client.GetAsync($"/api/v1/purchase-orders/{purchase.Order}")).IsSuccessStatusCode);
        Assert.NotEqual(Guid.Empty, purchase.Line);
    }
}
