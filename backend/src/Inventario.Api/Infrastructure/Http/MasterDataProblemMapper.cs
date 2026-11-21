using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventario.Api.Infrastructure.Http;

internal static class MasterDataProblemMapper
{
    public static InventoryProblemException? Map(Exception error, string method)
    {
        if (error is InventoryProblemException problem) return problem;
        if (error is DbUpdateConcurrencyException)
            return new(412, "VersionConflict", "El registro cambió. Consulta la versión actual y revisa tus cambios.");
        if (error is RetryLimitExceededException)
            return new(503, "ServiceUnavailable", "La base no pudo confirmar la operación. Comprueba su resultado antes de repetir.");
        if (error is DbUpdateException { InnerException: SqlException sql })
        {
            // HU-02: carrera entre dos altas del mismo lugar físico (el caso habitual lo
            // resuelve antes el comando, nombrando la ubicación existente).
            if (sql.Number is 2601 or 2627 && sql.Message.Contains("UX_Locations_Aisle_Rack_Level", StringComparison.Ordinal))
            {
                const string place = "Ese pasillo, estantería y nivel ya están registrados.";
                return new(409, "DuplicateLocationPlace",
                    "Ya existe una ubicación con ese pasillo, estantería y nivel, también puede estar inactiva.",
                    new Dictionary<string, string[]> { ["aisle"] = [place], ["rack"] = [place], ["level"] = [place] });
            }
            // HU-05 (D-10): carrera entre dos altas de pedido con la misma referencia externa; el
            // comando la detecta antes bajo el bloqueo de la referencia. Va antes de la rama de _Code.
            if (sql.Number is 2601 or 2627 && sql.Message.Contains("UX_CustomerOrders_ExternalReference", StringComparison.Ordinal))
                return new(409, "DuplicateCode", "Esa referencia de pedido ya está utilizada.",
                    new Dictionary<string, string[]> { ["externalReference"] = ["Introduce una referencia que no esté usada."] });
            if (sql.Number is 2601 or 2627 && (sql.Message.Contains("_Code", StringComparison.Ordinal)
                || sql.Message.Contains("_Sku", StringComparison.Ordinal)))
            {
                var field = sql.Message.Contains("_Sku", StringComparison.Ordinal) ? "sku" : "code";
                return new(409, "DuplicateCode", "Ese código ya está utilizado, también puede pertenecer a un registro inactivo.",
                    new Dictionary<string, string[]> { [field] = ["Introduce un código libre."] });
            }
            if (sql.Number == 547 && method == "DELETE")
                return new(409, "HistoryPreventsDeletion", "Existen referencias que impiden borrar el artículo. Puedes desactivarlo.");
        }
        return null;
    }
}
