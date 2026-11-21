using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.MasterData.Queries;

public sealed record MasterDataQueryOptions(string? Query, bool? IsActive, int Page, int PageSize)
{
    public static MasterDataQueryOptions Parse(string? q, string? state, int? page, int? pageSize)
    {
        var query = MasterDataText.Optional(q, 200, "q");
        bool? active = state switch
        {
            null or "active" => true,
            "inactive" => false,
            "all" => null,
            _ => throw InventoryProblemException.Validation("state", "Selecciona active, inactive o all.")
        };
        if (page is < 1 || pageSize is < 1 or > 100)
            throw InventoryProblemException.Validation(page is < 1 ? "page" : "pageSize", "La página empieza en 1 y el tamaño debe estar entre 1 y 100.");
        if (((long)(page ?? 1) - 1) * (pageSize ?? 25) > int.MaxValue)
            throw InventoryProblemException.Validation("page", "La página solicitada está fuera de rango.");
        return new(query, active, page ?? 1, pageSize ?? 25);
    }
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
