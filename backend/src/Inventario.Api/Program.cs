using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Features.MasterData;
using Inventario.Api.Features.Locations;
using Inventario.Api.Features.Purchasing;
using Inventario.Api.Features.Receiving;
using Inventario.Api.Features.Replenishment;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Features.MasterData.Maintenance;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.Api.Infrastructure.Messaging;
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
builder.Services.AddInventoryOperations();
builder.Services.AddMasterData();
builder.Services.AddStockCore();
builder.Services.AddLocations();
builder.Services.AddPurchasing();
builder.Services.AddReceiving();
builder.Services.AddScoped<StockAlertEvaluator>();
builder.Services.AddScoped<IArticleStateEffects, ArticleAlertStateEffects>();
if (!initialize && !builder.Configuration.GetValue<bool>("Maintenance:Hu08FamilyPreparation"))
    builder.Services.AddHostedService(provider => provider.GetRequiredService<PurchaseDeliveryWorker>());
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
    context.ProblemDetails.Extensions.TryAdd("code",
        ApiProblems.DefaultCode(context.ProblemDetails.Status ?? 500));
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    MasterDataOpenApi.Configure(options);
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Inventario — H0 y datos maestros";
        document.Info.Description = "Sesión, disponibilidad, resultados confirmados, catálogos de familias, proveedores y artículos; retirada e historial con disponibilidad explícita del diario compartido.";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});
builder.Services.AddDataProtection()
    .SetApplicationName("Inventario")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyDirectory"]
        ?? Path.Combine(builder.Environment.ContentRootPath, ".keys")));

var app = builder.Build();
var maintenance = app.Configuration.GetValue<bool>("Maintenance:Hu08FamilyPreparation");

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
    if (context.Request.Path.StartsWithSegments("/api/v1"))
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
        var connected = !maintenance && await database.Database.CanConnectAsync(cancellation);
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
api.MapOperationEndpoints();
if (maintenance)
{
    using var scope = app.Services.CreateScope();
    if (await ArticleSchema.IsExpandedAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>()))
        api.MapHu08Maintenance();
}
else
{
    api.MapMasterDataEndpoints();
    api.MapLocationEndpoints();
    api.MapPurchasingEndpoints();
    api.MapReceivingEndpoints();
    api.MapStockPolicyEndpoints();
    api.MapStockAlertEndpoints();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapFallback("/api/{**path}", () => ApiProblems.Status(404)).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();
await app.RunAsync();

public partial class Program;
