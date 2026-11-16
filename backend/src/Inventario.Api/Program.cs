using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var initialize = builder.Configuration.GetValue<bool>("initialize-database");
builder.Services.AddDbContext<InventoryDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connection = initialize ? DatabaseConfiguration.InitializationConnection(configuration)
        : configuration.GetConnectionString("Inventory") ?? DatabaseConfiguration.ApplicationConnection(configuration);
    options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure());
});
builder.Services.AddInventoryIdentity(builder.Environment);
builder.Services.AddStockCore();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
    context.ProblemDetails.Extensions.TryAdd("code",
        ApiProblems.DefaultCode(context.ProblemDetails.Status ?? 500));
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Inventario — base API H0";
    document.Info.Description = "Sesión, antiforgery y disponibilidad. Los recursos de negocio se incorporan en sus specs.";
    document.Info.Version = "v1";
    return Task.CompletedTask;
}));
builder.Services.AddDataProtection()
    .SetApplicationName("Inventario")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyDirectory"]
        ?? Path.Combine(builder.Environment.ContentRootPath, ".keys")));

var app = builder.Build();

if (initialize)
{
    await DatabaseBootstrapper.InitializeAsync(builder.Configuration, app.Services);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
    await ApiProblems.Status(context.HttpContext.Response.StatusCode)
        .ExecuteAsync(context.HttpContext));
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/auth"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1").AddEndpointFilter<AntiforgeryFilter>();
api.MapGet("/health", async (InventoryDbContext database, CancellationToken cancellation) =>
{
    try
    {
        var connected = await database.Database.CanConnectAsync(cancellation);
        var pending = connected
            && (await database.Database.GetPendingMigrationsAsync(cancellation)).Any();
        return connected && !pending
            ? Results.Ok(new { status = "healthy", database = "ready" })
            : Results.Json(new { status = "unhealthy", database = "unavailable" }, statusCode: 503);
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        app.Logger.LogWarning(error, "La comprobación de disponibilidad de SQL Server ha fallado.");
        return Results.Json(new { status = "unhealthy", database = "unavailable" }, statusCode: 503);
    }
}).AllowAnonymous().WithTags("Infrastructure").WithSummary("Comprueba SQL y migraciones pendientes");
api.MapSessionEndpoints();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapFallback("/api/{**path}", () => ApiProblems.Status(404)).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();
await app.RunAsync();

public partial class Program;
