using System.Text.Json;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventario.Api.Infrastructure.Messaging;

internal enum PurchaseDeliveryAction { Claim, Finish, LateObservation }
internal sealed record PurchaseDeliveryCommand(Guid OperationId, PurchaseDeliveryAction Action, Guid DeliveryId,
    Guid AuthorizationOperationId, Guid WorkerInstanceId, Guid AttemptId, Guid FinishOperationId,
    long? LeaseGeneration = null, PurchaseMailObservation? Observation = null)
{
    internal OperationRequest Request() => new(OperationId, "POST", $"/api/v1/internal/purchase-deliveries/{DeliveryId:D}/{(Action == PurchaseDeliveryAction.LateObservation ? "late-observation" : Action.ToString().ToLowerInvariant())}",
        "Purchasing.Send", JsonSerializer.SerializeToElement(new { action = Action.ToString(), DeliveryId, AuthorizationOperationId,
            WorkerInstanceId, AttemptId, FinishOperationId, LeaseGeneration, Observation }, OperationResponse.JsonOptions));
}

internal sealed class PurchaseDeliveryCommands(IOptions<PurchaseDeliveryOptions> options)
{
    internal static async Task<Guid> AuthorizeAsync(InventoryDbContext database, PurchaseDeliveryCommand command, CancellationToken cancellation)
    {
        var delivery = await database.Set<PurchaseOrderDelivery>().AsNoTracking().SingleOrDefaultAsync(value => value.Id == command.DeliveryId, cancellation);
        var proof = await database.OperationResults.AsNoTracking().SingleOrDefaultAsync(value => value.OperationId == command.AuthorizationOperationId, cancellation);
        if (delivery is null || proof is null || proof.RequiredPermission != "Purchasing.Send" || proof.Method != "POST"
            || proof.Resource != $"/api/v1/purchase-orders/{delivery.PurchaseOrderId:D}/send" || proof.SourceDocumentId != delivery.PurchaseOrderId
            || proof.ResultStatus != 202 || proof.ResultBody is null)
            throw Forbidden();
        using var result = JsonDocument.Parse(proof.ResultBody);
        if (!result.RootElement.TryGetProperty("deliveryId", out var requested) || !requested.TryGetGuid(out var id) || id != delivery.Id
            || !await database.Users.AnyAsync(value => value.Id == proof.ActorId, cancellation)) throw Forbidden();
        return proof.ActorId;
    }

    private static InventoryProblemException Forbidden() => new(403, "Forbidden", "La entrega no tiene una autorización persistida válida.");

    internal async Task<OperationResponse> ApplyAsync(OperationContext context, PurchaseDeliveryCommand command, CancellationToken cancellation)
    {
        var database = context.Database;
        var delivery = await database.Set<PurchaseOrderDelivery>().AsNoTracking().SingleAsync(value => value.Id == command.DeliveryId, cancellation);
        // One call sorts complete canonical resources: purchase-order-delivery precedes purchase-order ("-" < ":").
        await context.Locks.DocumentsAsync([new("purchase-order", delivery.PurchaseOrderId), new("purchase-order-delivery", delivery.Id)], cancellation);
        var actor = await AuthorizeAsync(database, command, cancellation);
        if (actor != context.ActorId) throw Forbidden();
        var outbox = await database.Set<OutboxMessage>().SingleAsync(value => value.DeliveryId == delivery.Id, cancellation);
        var order = await database.Set<PurchaseOrder>().Include(value => value.Lines).SingleAsync(value => value.Id == delivery.PurchaseOrderId, cancellation);
        var now = await PurchaseDeliveryClock.UtcNowAsync(database, cancellation);
        var applied = false;
        void Event(PurchaseDeliveryEventKind kind, object outcome, Guid? attempt, long? generation, DateTimeOffset? observed = null) =>
            database.Add(new PurchaseDeliveryEvent(delivery.Id, kind, actor, command.AuthorizationOperationId, context.OperationId,
                PurchaseExecutionKind.Worker, JsonSerializer.Serialize(outcome, OperationResponse.JsonOptions), attempt, generation,
                command.WorkerInstanceId, observed));

        if (command.Action == PurchaseDeliveryAction.Claim)
        {
            if (outbox.ActiveAuthorizationOperationId == command.AuthorizationOperationId && outbox.AuthorizedBy == actor && outbox.CanClaim(now))
            {
                if (outbox.State == PurchaseOutboxState.Processing)
                    Event(PurchaseDeliveryEventKind.LeaseExpired, new { acceptanceUncertain = true }, outbox.CurrentAttemptId, outbox.LeaseGeneration);
                applied = outbox.Claim(command.WorkerInstanceId, command.AttemptId, command.FinishOperationId, now, options.Value.LeaseSeconds, options.Value.MaxAttempts);
                if (applied) Event(PurchaseDeliveryEventKind.Claimed, new { outbox.LeaseUntil, outbox.Attempts, outbox.Cycle }, command.AttemptId, outbox.LeaseGeneration);
                else if (outbox.State == PurchaseOutboxState.Failed)
                {
                    order.FailSend();
                    Event(PurchaseDeliveryEventKind.PermanentFailure, new { code = "AttemptBudgetExhausted", outbox.AcceptanceUncertain }, command.AttemptId, outbox.LeaseGeneration);
                }
            }
        }
        else if (command.Action is PurchaseDeliveryAction.Finish or PurchaseDeliveryAction.LateObservation)
        {
            var observation = command.Observation ?? throw new InventoryProblemException(400, "InvalidRequest", "Falta el resultado conocido del intento.");
            if (command.Action == PurchaseDeliveryAction.Finish && command.OperationId == outbox.FinishOperationId
                && outbox.ActiveAuthorizationOperationId == command.AuthorizationOperationId
                && outbox.Owns(command.WorkerInstanceId, command.AttemptId, command.LeaseGeneration ?? -1, now))
            {
                outbox.Finish(observation.Accepted, observation.Permanent, observation.Uncertain, observation.Code, observation.Summary, now, options.Value.MaxAttempts);
                if (observation.Accepted) order.AcceptSend(observation.ObservedAt);
                else if (outbox.State == PurchaseOutboxState.Failed) order.FailSend();
                var kind = observation.Accepted ? PurchaseDeliveryEventKind.Accepted : observation.Uncertain ? PurchaseDeliveryEventKind.Uncertain
                    : outbox.State == PurchaseOutboxState.Failed ? PurchaseDeliveryEventKind.PermanentFailure : PurchaseDeliveryEventKind.TransientFailure;
                Event(kind, new { observation.Accepted, observation.Code, observation.Summary, observation.Uncertain }, command.AttemptId, command.LeaseGeneration, observation.ObservedAt);
                applied = true;
            }
            else Event(PurchaseDeliveryEventKind.LateObservation, new { observation.Accepted, observation.Uncertain, applied = false },
                command.AttemptId, command.LeaseGeneration, observation.ObservedAt);
        }
        else throw new InventoryProblemException(400, "InvalidRequest", "Acción interna de entrega desconocida.");

        return OperationResponse.Json(200, new { operationId = context.OperationId, deliveryId = delivery.Id, applied,
            generation = outbox.LeaseGeneration, leaseUntil = outbox.LeaseUntil, attemptId = outbox.CurrentAttemptId,
            finishOperationId = outbox.FinishOperationId, authorizationOperationId = command.AuthorizationOperationId,
            owner = outbox.LeaseOwner, state = outbox.State.ToString() }, order.Id);
    }
}
