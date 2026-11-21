using Inventario.Api.Features.MasterData;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Xunit;

namespace Inventario.IntegrationTests;

public sealed class MasterDataValidationTests
{
    [Fact]
    public void Names_codes_and_optional_contact_follow_the_contract()
    {
        Assert.Equal("ART-01", MasterDataText.Code(" art-01 ", "sku"));
        Assert.Equal("UD", MasterDataText.BaseUnit(" ud "));
        Assert.Equal("Artículo", MasterDataText.Name(" Artículo "));
        Assert.Null(MasterDataText.Email(" "));
        Assert.Equal("contacto@example.test", MasterDataText.Email(" contacto@example.test "));
        Assert.Equal(422, Assert.Throws<InventoryProblemException>(() => MasterDataText.Code(new string('a', 65))).Status);
        Assert.Equal(422, Assert.Throws<InventoryProblemException>(() => MasterDataText.Name(" ")).Status);
        Assert.Equal(422, Assert.Throws<InventoryProblemException>(() => MasterDataText.Email("inválido")).Status);
    }

    [Fact]
    public void Search_defaults_and_bounds_do_not_silently_change_the_filter()
    {
        var defaults = MasterDataQueryOptions.Parse(" ", null, null, null);
        Assert.Null(defaults.Query);
        Assert.True(defaults.IsActive);
        Assert.Equal(1, defaults.Page);
        Assert.Equal(25, defaults.PageSize);
        Assert.Null(MasterDataQueryOptions.Parse(" A ", "all", 1, 100).IsActive);
        Assert.False(MasterDataQueryOptions.Parse(null, "inactive", 1, 1).IsActive);
        foreach (var state in new[] { "", "ACTIVE", "unknown" })
            Assert.Equal(422, Assert.Throws<InventoryProblemException>(() =>
                MasterDataQueryOptions.Parse(null, state, 1, 25)).Status);
        Assert.Throws<InventoryProblemException>(() => MasterDataQueryOptions.Parse(null, null, 0, 25));
        Assert.Throws<InventoryProblemException>(() => MasterDataQueryOptions.Parse(null, null, 1, 101));
        Assert.Throws<InventoryProblemException>(() => MasterDataQueryOptions.Parse(null, null, int.MaxValue, 100));
    }
}
