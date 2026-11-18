namespace Inventario.Api.Features.MasterData;

internal static class MasterDataEndpoints
{
    public static void MapMasterDataEndpoints(this RouteGroupBuilder api)
    {
        api.MapFamilyEndpoints();
        api.MapSupplierEndpoints();
        api.MapArticleEndpoints();
    }
}
