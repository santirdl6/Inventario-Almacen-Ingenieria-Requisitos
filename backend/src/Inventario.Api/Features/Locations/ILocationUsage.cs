using Inventario.Api.Domain.Locations;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
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

    // La misma comprobación, con el código de la ubicación y el del conteo que la ocupa, para que el 409
    // CountScopeFrozen los nombre (HU-04 US3.1, Convergence C1). null si no está en un conteo abierto.
    Task<OpenCountScope?> FindOpenCountAsync(Guid locationId, CancellationToken cancellationToken);
}

/// <summary>Ubicación congelada y conteo abierto que la ocupa.</summary>
public sealed record OpenCountScope(string LocationCode, string CountCode)
{
    /// <summary>409 CountScopeFrozen común a salidas, reservas y ubicaciones (US3.1).</summary>
    public InventoryProblemException Problem() => new(409, "CountScopeFrozen",
        $"La ubicación {LocationCode} está en el conteo abierto {CountCode}. Espera a que termine.");
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

    public Task<OpenCountScope?> FindOpenCountAsync(Guid locationId, CancellationToken cancellationToken) =>
        (from frozen in database.Set<CountLocation>().AsNoTracking()
         join count in database.Set<CycleCount>().AsNoTracking() on frozen.CountId equals count.Id
         join location in database.Set<Location>().AsNoTracking() on frozen.LocationId equals location.Id
         where frozen.LocationId == locationId && frozen.IsFrozen
         select new OpenCountScope(location.Code, count.CountCode))
        .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<StockBalance> Balances(Guid locationId) =>
        database.Set<StockBalance>().AsNoTracking().Where(balance => balance.LocationId == locationId);
}
