using Microsoft.AspNetCore.Antiforgery;

namespace Inventario.Api.Infrastructure.Http;

internal sealed class AntiforgeryFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            try
            {
                await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return ApiProblems.Create(400, "AntiforgeryValidationFailed",
                    "Actualiza el token de la sesión e inténtalo de nuevo.");
            }
        }
        return await next(context);
    }
}
