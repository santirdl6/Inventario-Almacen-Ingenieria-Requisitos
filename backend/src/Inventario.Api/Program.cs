using Inventario.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Inventory")
    ?? DatabaseConfiguration.ApplicationConnection(builder.Configuration);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
builder.Services.AddProblemDetails();
builder.Services.AddDataProtection()
    .SetApplicationName("Inventario")
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["DataProtection:KeyDirectory"] ?? "/app/keys"));

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("initialize-database"))
{
    await DatabaseBootstrapper.InitializeAsync(builder.Configuration);
    return;
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/v1/health", async (InventoryDbContext database, CancellationToken cancellation) =>
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
});

app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
await app.RunAsync();
