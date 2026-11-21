using System.Data;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Queries;

public sealed class SupplierQueries(InventoryDbContext database)
{
    public Task<PageResult<SupplierDto>> ListAsync(MasterDataQueryOptions options, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var query = database.Set<Supplier>().AsNoTracking();
            if (options.IsActive is { } active) query = query.Where(value => value.IsActive == active);
            if (options.Query is { } text) query = query.Where(value => value.Code.Contains(text)
                || EF.Functions.Collate(value.Name, "Latin1_General_100_CI_AS").Contains(text));
            var count = await query.CountAsync(cancellation);
            var items = await query.OrderBy(value => value.Code).ThenBy(value => value.Id)
                .Skip((options.Page - 1) * options.PageSize).Take(options.PageSize)
                .Select(value => new SupplierDto(value.Id, value.Code, value.Name, value.Email, value.TaxIdentifier, value.IsActive)).ToListAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            return new PageResult<SupplierDto>(items, options.Page, options.PageSize, count);
        });
    public Task<Supplier?> DetailAsync(Guid id, CancellationToken cancellation) => database.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation);
}
