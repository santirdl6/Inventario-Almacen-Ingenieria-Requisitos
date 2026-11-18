using Microsoft.OpenApi;

namespace Inventario.Api.Features.MasterData;

internal static class MasterDataOpenApi
{
    public static void Configure(Microsoft.AspNetCore.OpenApi.OpenApiOptions options) => options.AddOperationTransformer((operation, context, _) =>
    {
        var path = context.Description.RelativePath ?? "";
        var method = context.Description.HttpMethod ?? "";
        if (!path.StartsWith("api/v1/families", StringComparison.Ordinal) && !path.StartsWith("api/v1/suppliers", StringComparison.Ordinal)
            && !path.StartsWith("api/v1/articles", StringComparison.Ordinal)) return Task.CompletedTask;
        operation.Parameters ??= [];
        var write = method is "POST" or "PUT" or "DELETE";
        if (write)
        {
            operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true, Description = "UUID estable de la intención; conservarlo para comprobar o repetir la misma petición.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
            operation.Parameters.Add(new OpenApiParameter { Name = "X-XSRF-TOKEN", In = ParameterLocation.Header, Required = true, Description = "Token antiforgery de la sesión actual.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
        }
        if (method is "PUT" or "DELETE") operation.Parameters.Add(new OpenApiParameter { Name = "If-Match", In = ParameterLocation.Header, Required = true, Description = "ETag fuerte de la revisión consultada.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
        foreach (var status in new[] { "400", "401", "403", "503" }.Concat(write ? new[] { "409", "422" } : []).Concat(method is "PUT" or "DELETE" ? new[] { "404", "412", "428" } : []))
            operation.Responses!.TryAdd(status, new OpenApiResponse { Description = "ProblemDetails con code, traceId y errores por campo cuando correspondan.", Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.Object } } } });
        var detail = method == "GET" && path.EndsWith("{id}", StringComparison.Ordinal);
        foreach (var entry in operation.Responses!)
        {
            if (entry.Value is not OpenApiResponse response || entry.Key is not ("200" or "201")) continue;
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            if (detail || method is "POST" or "PUT") response.Headers["ETag"] = new OpenApiHeader { Description = "Versión fuerte opaca de la fila persistida.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
            if (entry.Key == "201") response.Headers["Location"] = new OpenApiHeader { Description = "Ruta de consulta del recurso creado.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        }
        return Task.CompletedTask;
    });
}
