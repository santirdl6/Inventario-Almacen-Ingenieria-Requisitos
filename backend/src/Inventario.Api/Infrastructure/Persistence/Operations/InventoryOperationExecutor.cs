using Inventario.Api.Domain.Identity;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Infrastructure.Persistence.Operations;

public sealed record OperationContext(InventoryDbContext Database, InventoryLocks Locks, Guid ActorId,
    Guid OperationId, IServiceProvider Services);

public sealed class InventoryOperationExecutor(IServiceScopeFactory scopes, IHttpContextAccessor accessor,
    ILogger<InventoryOperationExecutor> logger)
{
    public async Task<OperationResponse> ExecuteAsync(OperationRequest request,
        Func<OperationContext, CancellationToken, Task<OperationResponse>> change,
        CancellationToken cancellation = default)
    {
        using var strategyScope = scopes.CreateScope();
        var strategyDatabase = strategyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var strategy = new SqlServerRetryingExecutionStrategy(strategyDatabase, 2, TimeSpan.FromSeconds(1), null);
        return await strategy.ExecuteAsync(async () =>
        {
            using var scope = scopes.CreateScope();
            var services = scope.ServiceProvider;
            var database = services.GetRequiredService<InventoryDbContext>();
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
                throw new UnauthenticatedActorException();
            var actor = await services.GetRequiredService<ICurrentActor>().GetRequiredActorIdAsync(cancellation);
            if (!PermissionCatalog.All.Contains(request.RequiredPermission)
                || !(await services.GetRequiredService<IAuthorizationService>()
                    .AuthorizeAsync(principal, null, request.RequiredPermission)).Succeeded)
                throw new InventoryProblemException(403, "Forbidden", "Tu cuenta no tiene permiso para esta operación.");
            if (request.OperationId == Guid.Empty || request.Method.ToUpperInvariant() is not ("POST" or "PUT" or "PATCH" or "DELETE"))
                throw new InventoryProblemException(400, "InvalidRequest", "La intención requiere una clave UUID y un método de escritura válidos.");
            var hash = RequestFingerprint.Create(request.Method, request.Resource, request.NormalizedBody);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellation);
            try
            {
                var locks = new InventoryLocks(database);
                await locks.OperationAsync(request.OperationId, cancellation);
                var existing = await database.OperationResults.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.OperationId == request.OperationId, cancellation);
                if (existing is not null)
                {
                    if (existing.ActorId != actor || existing.RequestHash != hash
                        || existing.Method != request.Method.ToUpperInvariant()
                        || existing.Resource != RequestFingerprint.CanonicalResource(request.Resource)
                        || existing.RequiredPermission != request.RequiredPermission)
                        throw new InventoryProblemException(409, "IdempotencyConflict", "La clave ya identifica una intención diferente.");
                    await transaction.RollbackAsync(cancellation);
                    return existing.Response();
                }

                var response = await change(new OperationContext(database, locks, actor, request.OperationId, services), cancellation);
                database.OperationResults.Add(OperationResult.Confirmed(request, actor, hash, response));
                await database.SaveChangesAsync(cancellation);
                await transaction.CommitAsync(cancellation);
                return response;
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (Exception rollback) { logger.LogWarning(rollback, "La conexión no permitió confirmar el rollback de {OperationId}.", request.OperationId); }
                throw;
            }
        });
    }
}

public static class InventoryOperationsConfiguration
{
    public static IServiceCollection AddInventoryOperations(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<InventoryOperationExecutor>();
        return services;
    }
}
