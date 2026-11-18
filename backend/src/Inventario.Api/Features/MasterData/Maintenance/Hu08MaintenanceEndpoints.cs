using System.Text.Json;
using Inventario.Api.Infrastructure.Http;

namespace Inventario.Api.Features.MasterData.Maintenance;

internal static class Hu08MaintenanceEndpoints
{
    public static void MapHu08Maintenance(this RouteGroupBuilder api)
    {
        api.MapPost("/maintenance/hu08/family-map", (JsonElement body, HttpContext context,
            PrepareHu08FamilyMapHandler handler, CancellationToken cancellation) =>
        {
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any(value => value.Name != "entries")
                || !body.TryGetProperty("entries", out var array) || array.ValueKind != JsonValueKind.Array)
                throw InventoryProblemException.Validation("entries", "Envía exclusivamente el mapa de asociaciones, sin actor ni otros campos.");
            foreach (var entry in array.EnumerateArray())
                if (entry.ValueKind != JsonValueKind.Object || entry.EnumerateObject().Any(value => value.Name is not ("articleId" or "familyId")))
                    throw InventoryProblemException.Validation("entries", "Cada asociación solo contiene articleId y familyId.");
            FamilyMapEntry[] entries;
            try { entries = array.Deserialize<FamilyMapEntry[]>(OperationResponseOptions)!; }
            catch (JsonException) { throw new InventoryProblemException(400, "InvalidRequest", "Los identificadores del mapa no son válidos."); }
            if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key) || key == Guid.Empty)
                throw new InventoryProblemException(400, "InvalidRequest", "La preparación requiere una clave UUID.");
            return handler.ExecuteAsync(key, entries, cancellation);
        }).RequireAuthorization("MasterData.Manage").WithTags("Maintenance");
    }
    private static readonly JsonSerializerOptions OperationResponseOptions = new(JsonSerializerDefaults.Web);
}
