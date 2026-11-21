using System.Security.Cryptography;
using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventario.Api.Infrastructure.Messaging;

public sealed class PurchaseDeliveryWorker(IServiceScopeFactory scopes, IPurchaseMailTransport transport,
    IOptions<PurchaseDeliveryOptions> options, IConfiguration configuration, ILogger<PurchaseDeliveryWorker> logger) : BackgroundService
{
    private readonly Guid instance = Guid.NewGuid();
    private readonly SemaphoreSlim serial = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || configuration.GetValue<bool>("initialize-database")
            || configuration.GetValue<bool>("Maintenance:Hu08FamilyPreparation")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunOnceAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("No se pudo completar el ciclo de entrega. Tipo: {ErrorType}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task<bool> RunOnceAsync(CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation);
        try
        {
            using var scope = scopes.CreateScope(); var services = scope.ServiceProvider;
            var database = services.GetRequiredService<InventoryDbContext>();
            var now = await PurchaseDeliveryClock.UtcNowAsync(database, cancellation);
            var candidate = await database.Set<OutboxMessage>().AsNoTracking()
                .Where(value => value.State == PurchaseOutboxState.Pending && value.NextAttemptAt <= now
                    || value.State == PurchaseOutboxState.Processing && value.LeaseUntil <= now)
                .OrderBy(value => value.NextAttemptAt).ThenBy(value => value.Id).FirstOrDefaultAsync(cancellation);
            if (candidate is null) return false;
            var executor = services.GetRequiredService<InventoryOperationExecutor>();
            var claim = new PurchaseDeliveryCommand(Guid.NewGuid(), PurchaseDeliveryAction.Claim, candidate.DeliveryId,
                candidate.ActiveAuthorizationOperationId, instance, Guid.NewGuid(), Guid.NewGuid());
            var result = await executor.ExecuteDeliveryAsync(claim, cancellation);
            using var grant = JsonDocument.Parse(result.Body!);
            if (!grant.RootElement.GetProperty("applied").GetBoolean()) return false;
            var generation = grant.RootElement.GetProperty("generation").GetInt64();
            // A replayed grant is historical. Check the live owner/fence using SQL's clock before SMTP.
            var live = await database.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.OutboxMessages WHERE DeliveryId={candidate.DeliveryId} AND State='Processing' AND LeaseOwner={instance} AND CurrentAttemptId={claim.AttemptId} AND LeaseGeneration={generation} AND LeaseUntil>CONVERT(datetimeoffset,SYSUTCDATETIME())").SingleAsync(cancellation);
            if (live != 1) return false;
            var delivery = await database.Set<PurchaseOrderDelivery>().AsNoTracking().SingleAsync(value => value.Id == candidate.DeliveryId, cancellation);
            PurchaseMailObservation observation;
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(delivery.MimeBytes), delivery.MessageHash))
                observation = new(false, true, false, "StoredMessageInvalid", "El documento conservado no supera su comprobación de integridad.", DateTimeOffset.UtcNow);
            else observation = await transport.SendAsync(delivery.MimeBytes, cancellation);
            var finish = claim with { OperationId = claim.FinishOperationId, Action = PurchaseDeliveryAction.Finish,
                LeaseGeneration = generation, Observation = observation };
            // Never repeat SMTP because a local finish failed. Recover the same finish operation instead.
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { await executor.ExecuteDeliveryAsync(finish, budget.Token); return true; }
                catch (Exception error) when (attempt == 0 && !budget.IsCancellationRequested)
                { logger.LogWarning("Se comprobará de nuevo la finalización local {OperationId}; no se repite SMTP. Tipo: {ErrorType}", finish.OperationId, error.GetType().Name); }
            }
            return true;
        }
        finally { serial.Release(); }
    }
}
