using Xunit;

namespace Inventario.IntegrationTests.Locations;

public sealed class LocationReviewTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_strong_etag_cannot_stay_equal_when_the_detail_representation_changes()
    {
        using var factory = fixture.Factory();
        using var support = new LocationsTestSupport(fixture, factory);
        var api = await support.SessionAsync();
        var created = await LocationsTestSupport.CreateLocationAsync(api);
        var before = await LocationsTestSupport.GetAsync(api, created.Id);
        Assert.Equal(before.ETag, (await LocationsTestSupport.GetAsync(api, created.Id)).ETag);
        await support.ReceiveAsync(await support.ArticleAsync(), created.Id, 2m);
        var after = await LocationsTestSupport.GetAsync(api, created.Id);
        Assert.False(before.Body.GetProperty("hasMovements").GetBoolean());
        Assert.True(after.Body.GetProperty("hasMovements").GetBoolean());
        Assert.NotEqual(before.ETag, after.ETag);
        using var stale = await LocationsTestSupport.UpdateAsync(api, created.Id, before.Body, before.ETag, level: "N02");
        Assert.Equal(System.Net.HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(1, await support.AuditCountAsync(created.Id));
    }
}
