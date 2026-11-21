using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Infrastructure.Messaging;

internal static class PurchaseDeliveryClock
{
    public static Task<DateTimeOffset> UtcNowAsync(InventoryDbContext database, CancellationToken cancellation) =>
        database.Database.SqlQuery<DateTimeOffset>($"SELECT CONVERT(datetimeoffset,SYSUTCDATETIME()) AS [Value]").SingleAsync(cancellation);
}
