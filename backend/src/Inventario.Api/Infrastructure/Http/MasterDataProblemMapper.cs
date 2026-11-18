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
