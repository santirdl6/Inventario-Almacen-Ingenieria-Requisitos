using Microsoft.AspNetCore.Diagnostics;
using Inventario.Api.Domain.Identity;
using Inventario.Api.Domain.Stock;

namespace Inventario.Api.Infrastructure.Http;

internal static class ApiProblems
{
    public static string DefaultCode(int status) => status switch
    {
        400 => "InvalidRequest",
        401 => "Unauthenticated",
        403 => "Forbidden",
        404 => "NotFound",
        405 => "MethodNotAllowed",
        422 => "ValidationFailed",
        503 => "ServiceUnavailable",
        _ => "UnexpectedError"
    };

    public static IResult Status(int status) => Create(status, DefaultCode(status), status switch
    {
        400 => "La solicitud no es válida.",
        401 => "Inicia sesión para acceder a este recurso.",
        403 => "Tu cuenta no tiene permiso para esta operación.",
        404 => "El recurso solicitado no existe.",
        405 => "El método no está permitido en este recurso.",
        _ => "No se pudo completar la solicitud."
    });

    public static IResult Create(int status, string code, string detail,
        IDictionary<string, object?>? additional = null)
    {
        var extensions = additional ?? new Dictionary<string, object?>();
        extensions["code"] = code;
        return Results.Problem(statusCode: status, title: status switch
        {
            400 => "Solicitud no válida",
            401 => "Autenticación requerida",
            403 => "Acceso denegado",
            404 => "Recurso no encontrado",
            422 => "Datos no válidos",
            _ => "Error al procesar la solicitud"
        }, detail: detail, extensions: extensions);
    }
}

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error,
        CancellationToken cancellationToken)
    {
        if (error is UnauthenticatedActorException)
        {
            await ApiProblems.Status(401).ExecuteAsync(context);
            return true;
        }

        if (error is StockOperationException stock)
        {
            var extensions = new Dictionary<string, object?>();
            if (stock.Errors.Count > 0) extensions["errors"] = stock.Errors;
            if (stock.OperationId is { } operationId) extensions["operationId"] = operationId;
            if (stock.Status == 503) context.Response.Headers.RetryAfter = "1";
            await ApiProblems.Create(stock.Status, stock.Code, stock.Message, extensions).ExecuteAsync(context);
            return true;
        }

        logger.LogError(error, "Error no controlado en la API. TraceId: {TraceId}", context.TraceIdentifier);
        await ApiProblems.Create(500, "UnexpectedError", "No se pudo completar la solicitud.")
            .ExecuteAsync(context);
        return true;
    }
}
