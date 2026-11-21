using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Domain.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace Inventario.Api.Infrastructure.Identity;

internal static class SessionEndpoints
{
    public static void MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Session");
        auth.MapGet("/csrf", IssueToken).AllowAnonymous()
            .WithSummary("Emite el token de petición antiforgery para la identidad actual");
        auth.MapPost("/login", LoginAsync).AllowAnonymous()
            .Produces<SessionToken>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(422)
            .WithSummary("Inicia una sesión mediante cookie y renueva el token antiforgery");
        auth.MapPost("/logout", async (HttpContext context, SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            context.Response.Cookies.Delete("XSRF-TOKEN");
            context.Response.Headers.CacheControl = "no-store";
            return Results.NoContent();
        }).RequireAuthorization().WithSummary("Cierra la sesión actual");
        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            HttpContext context, ICurrentActor actor, CancellationToken cancellation) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var actorId = await actor.GetRequiredActorIdAsync(cancellation);
            var user = await users.GetUserAsync(principal);
            if (user is null)
                return ApiProblems.Status(401);
            var roles = await users.GetRolesAsync(user);
            var permissions = principal.FindAll("permission").Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return Results.Ok(new { id = actorId, email = user.Email, roles, permissions });
        }).RequireAuthorization().WithSummary("Consulta el usuario y sus permisos actuales");
    }

    private static IResult IssueToken(HttpContext context, IAntiforgery antiforgery,
        IHostEnvironment environment)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken!;
        context.Response.Cookies.Append("XSRF-TOKEN", token, new CookieOptions
        {
            HttpOnly = false,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true
        });
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new SessionToken(token));
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context,
        UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
        IAntiforgery antiforgery, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !new EmailAddressAttribute().IsValid(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
            return ApiProblems.Create(422, "ValidationFailed", "Introduce un correo y una contraseña válidos.",
                new Dictionary<string, object?>
                {
                    ["errors"] = new Dictionary<string, string[]>
                    { ["credentials"] = ["El correo y la contraseña son obligatorios."] }
                });

        context.Response.Headers.CacheControl = "no-store";
        var user = await users.FindByEmailAsync(request.Email.Trim());
        var result = user is null ? SignInResult.Failed
            : await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
            return ApiProblems.Create(401, "InvalidCredentials", "El correo o la contraseña no son correctos.");

        // El nuevo token se vincula al usuario autenticado, no al principal anónimo del login.
        context.User = await signIn.CreateUserPrincipalAsync(user!);
        return IssueToken(context, antiforgery, environment);
    }

    internal sealed record LoginRequest(string? Email, string? Password);
    internal sealed record SessionToken(string RequestToken);
}
