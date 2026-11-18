using Inventario.Api.Infrastructure.Http;
using Xunit;

namespace Inventario.IntegrationTests.Operations;

public sealed class EntityTagTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public void A_matching_strong_version_is_accepted_and_a_stale_one_is_412()
    {
        EntityTags.RequireMatch(EntityTags.Format(Version), Version);
        var error = Assert.Throws<InventoryProblemException>(() =>
            EntityTags.RequireMatch(EntityTags.Format(new byte[8]), Version));
        Assert.Equal(412, error.Status);
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("", 428)]
    [InlineData("*", 400)]
    [InlineData("W/\"AQIDBAUGBwg=\"", 400)]
    [InlineData("\"AQIDBAUGBwg=\",\"AQIDBAUGBwg=\"", 400)]
    [InlineData("\"not base64\"", 400)]
    [InlineData("\"AQID\"", 400)]
    public void Missing_or_non_strong_preconditions_are_rejected(string? header, int status)
    {
        var error = Assert.Throws<InventoryProblemException>(() => EntityTags.RequireMatch(header, Version));
        Assert.Equal(status, error.Status);
    }
}
