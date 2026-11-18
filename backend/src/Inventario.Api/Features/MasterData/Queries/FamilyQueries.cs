using System.Data;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Queries;

public sealed class FamilyQueries(InventoryDbContext database)
{
    public Task<PageResult<FamilyDto>> ListAsync(MasterDataQueryOptions options, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            var query = database.Set<Family>().AsNoTracking();
            if (options.IsActive is { } active) query = query.Where(value => value.IsActive == active);
            if (options.Query is { } text) query = query.Where(value => value.Code.Contains(text)
                || EF.Functions.Collate(value.Name, "Latin1_General_100_CI_AS").Contains(text));
            var count = await query.CountAsync(cancellation);
            var items = await query.OrderBy(value => value.Code).ThenBy(value => value.Id)
                .Skip((options.Page - 1) * options.PageSize).Take(options.PageSize)
                .Select(value => new FamilyDto(value.Id, value.Code, value.Name, value.IsActive)).ToListAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            return new PageResult<FamilyDto>(items, options.Page, options.PageSize, count);
        });

    public Task<Family?> DetailAsync(Guid id, CancellationToken cancellation) =>
        database.Set<Family>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellation);
}
