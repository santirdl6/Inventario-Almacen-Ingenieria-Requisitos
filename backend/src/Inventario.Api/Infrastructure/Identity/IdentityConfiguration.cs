using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Domain.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Inventario.Api.Infrastructure.Identity;

internal static class IdentityConfiguration
{
    public static IServiceCollection AddInventoryIdentity(this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.SignIn.RequireConfirmedAccount = true;
        }).AddEntityFrameworkStores<InventoryDbContext>().AddDefaultTokenProviders();

        var secure = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "Inventario.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = secure;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
                ApiProblems.Status(401).ExecuteAsync(context.HttpContext);
            options.Events.OnRedirectToAccessDenied = context =>
                ApiProblems.Status(403).ExecuteAsync(context.HttpContext);
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var permission in PermissionCatalog.All)
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
        });
        services.AddTransient<IClaimsTransformation, PermissionClaimsTransformation>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, IdentityCurrentActor>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "Inventario.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = secure;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }
}
