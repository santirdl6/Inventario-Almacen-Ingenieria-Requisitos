using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Locations;

// Uso de una ubicación por el inventario, leído de StockBalances (HU-02 D-03, D-09, D-10).
// Dentro de un comando se resuelve desde OperationContext.Services, de modo que comparte el
// DbContext y la transacción del ejecutor de A y se consulta después de location:{id}.
public interface ILocationUsage
{
    // Existe saldo ⇔ existe alguna entrada confirmada (el saldo nace con la primera y no se borra).
    Task<bool> HasMovementsAsync(Guid locationId, CancellationToken cancellationToken);

    Task<LocationStockTotals> GetTotalsAsync(Guid locationId, CancellationToken cancellationToken);

    // Conteo abierto sobre la ubicación: alguna fila de CountLocations con IsFrozen = 1 (HU-04 D-04,
    // INT-HU04-03). Se consulta tras location:{id}, en el DbContext y la transacción del comando.
    Task<bool> IsInOpenCountAsync(Guid locationId, CancellationToken cancellationToken);
}

public sealed record LocationStockTotals(decimal Physical, decimal Reserved)
{
    public bool IsEmpty => Physical == 0m && Reserved == 0m;
    public bool HasReservations => Reserved > 0m;
}

internal sealed class StockBalanceLocationUsage(InventoryDbContext database) : ILocationUsage
{
    public Task<bool> HasMovementsAsync(Guid locationId, CancellationToken cancellationToken) =>
        Balances(locationId).AnyAsync(cancellationToken);

    public async Task<LocationStockTotals> GetTotalsAsync(Guid locationId, CancellationToken cancellationToken)
    {
        var totals = await Balances(locationId)
            .GroupBy(balance => balance.LocationId)
            .Select(group => new { Physical = group.Sum(balance => balance.Physical), Reserved = group.Sum(balance => balance.Reserved) })
            .SingleOrDefaultAsync(cancellationToken);
        return totals is null ? new LocationStockTotals(0m, 0m) : new LocationStockTotals(totals.Physical, totals.Reserved);
    }

    public Task<bool> IsInOpenCountAsync(Guid locationId, CancellationToken cancellationToken) =>
        database.Set<CountLocation>().AsNoTracking()
            .AnyAsync(location => location.LocationId == locationId && location.IsFrozen, cancellationToken);

    private IQueryable<StockBalance> Balances(Guid locationId) =>
        database.Set<StockBalance>().AsNoTracking().Where(balance => balance.LocationId == locationId);
}
