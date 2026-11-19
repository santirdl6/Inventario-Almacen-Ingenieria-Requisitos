using Inventario.Api.Domain.Locations;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Locations.Queries;

// Consultas de HU-02 (contracts/locations-api.md). Solo lectura: AsNoTracking y, para las
// existencias, StockBalances como única fuente (D-09).
public sealed class LocationQueries(InventoryDbContext database, ILocationUsage usage)
{
    public async Task<PageResult<LocationSummary>> ListAsync(string? search, string? kind, string? status,
        int? page, int? pageSize, CancellationToken cancellation)
    {
        var paging = Paging.From(page, pageSize);
        var query = database.Set<Location>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim().ToUpperInvariant();
            query = query.Where(location => location.Code.StartsWith(prefix) || location.Aisle.StartsWith(prefix));
        }
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<LocationKind>(kind.Trim(), true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(kind, out _))
                throw InventoryProblemException.Validation("kind", "El tipo debe ser Storage o Receiving.");
            query = query.Where(location => location.Kind == parsed);
        }
        query = (status?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" or "" => query.Where(location => location.IsActive),
            "inactive" => query.Where(location => !location.IsActive),
            "all" => query,
            _ => throw InventoryProblemException.Validation("status", "El estado debe ser active, inactive o all.")
        };
        var total = await query.CountAsync(cancellation);
        var items = await query.OrderBy(location => location.Code).ThenBy(location => location.Id)
            .Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellation);
        return new(items.Select(LocationSummary.From).ToList(), paging.Page, paging.PageSize, total);
    }

    public async Task<(LocationDetail Detail, byte[] Version)?> DetailAsync(Guid id, CancellationToken cancellation)
    {
        var location = await database.Set<Location>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation);
        return location is null ? null : (await DetailAsync(database, usage, location, cancellation), location.Version);
    }

    /// <summary>Detalle con el uso de la ubicación; los comandos lo llaman con su propio contexto.</summary>
    public static async Task<LocationDetail> DetailAsync(InventoryDbContext database, ILocationUsage usage,
        Location location, CancellationToken cancellation)
    {
        var hasMovements = await usage.HasMovementsAsync(location.Id, cancellation);
        var totals = await usage.GetTotalsAsync(location.Id, cancellation);
        var inOpenCount = await usage.IsInOpenCountAsync(location.Id, cancellation);
        var lastReceiving = location is { Kind: LocationKind.Receiving, IsActive: true }
            && await ActiveReceivingCountAsync(database, cancellation) == 1;
        return LocationDetail.From(location, hasMovements, totals, inOpenCount, lastReceiving);
    }

    public static Task<int> ActiveReceivingCountAsync(InventoryDbContext database, CancellationToken cancellation) =>
        database.Set<Location>().AsNoTracking()
            .CountAsync(location => location.Kind == LocationKind.Receiving && location.IsActive, cancellation);
}
