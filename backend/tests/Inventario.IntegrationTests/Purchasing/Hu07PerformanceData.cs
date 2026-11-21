using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Inventario.Api.Features.Stock.Core;
using Inventario.Api.Infrastructure.Identity;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Inventario.IntegrationTests.Purchasing;

// Explicit synthetic-load fixture, never an HTTP endpoint. All facts use the existing
// commands/executor; administrative access is limited to preparing temporary Identity accounts.
public static class Hu07PerformanceData
{
    private static string Connection(string project, bool administrative)
    {
        if (Environment.GetEnvironmentVariable("HU07_PERFORMANCE") != "7007"
            || !System.Text.RegularExpressions.Regex.IsMatch(project, "^inventario-hu07-e2e-performance-[a-f0-9]+$")
            || Environment.GetEnvironmentVariable("Testing__SqlHost") != "sqlserver")
            throw new InvalidOperationException("El perfil de carga requiere un proyecto HU-07 performance dedicado y seed 7007.");
        return new SqlConnectionStringBuilder { DataSource = "sqlserver", InitialCatalog = "InventoryHu07Performance",
            UserID = administrative ? "sa" : Environment.GetEnvironmentVariable("SQL_APP_USER"),
            Password = Environment.GetEnvironmentVariable(administrative ? "Testing__SqlAdminPassword" : "SQL_APP_PASSWORD")
                ?? throw new InvalidOperationException("Falta la credencial del SQL de pruebas aislado."),
            Encrypt = true, TrustServerCertificate = true }.ConnectionString;
    }

    public static async Task<object> InspectAsync(string project, bool retire, CancellationToken cancellation)
    {
        var connection = Connection(project, retire);
        if (retire)
        {
            var keys = Path.Combine(Path.GetTempPath(), "hu07-performance-retire-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(keys);
            try
            {
                await using var services = MasterDataTestData.BootstrapServices(new ConfigurationBuilder().Build(), connection, keys);
                using var scope = services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var accounts = await users.Users.Where(user => user.Email != null && user.Email.EndsWith("@hu07.performance.test")).ToListAsync(cancellation);
                foreach (var user in accounts)
                {
                    if (!(await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded
                        || !(await users.UpdateSecurityStampAsync(user)).Succeeded)
                        throw new InvalidOperationException("No se pudo retirar una cuenta de carga.");
                }
                return new { retiredAccounts = accounts.Count };
            }
            finally { Directory.Delete(keys, true); }
        }
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync(cancellation);
        await using var command = sql.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT_BIG(*) FROM Articles) articles,
              (SELECT COUNT_BIG(*) FROM Suppliers) suppliers,
              (SELECT COUNT_BIG(*) FROM PurchaseOrders) orders,
              (SELECT COUNT_BIG(*) FROM StockEntries) historyEntries,
              (SELECT COUNT_BIG(*) FROM StockMovements) movements,
              (SELECT COUNT_BIG(*) FROM OperationResults) operationResults,
              (SELECT SUM(Physical) FROM StockBalances) physical,
              (SELECT COUNT_BIG(*) FROM PurchaseOrders WHERE State <> 'Draft' OR SendStatus <> 'None') nonDraftOrders,
              CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) sqlVersion
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
            """;
        return JsonSerializer.Deserialize<JsonElement>((string)(await command.ExecuteScalarAsync(cancellation))!);
    }

    public static async Task<object> PrepareAsync(string project, CancellationToken cancellation)
    {
        _ = Connection(project, false);
        if (Environment.GetEnvironmentVariable("HU07_PERFORMANCE") != "7007"
            || !System.Text.RegularExpressions.Regex.IsMatch(project, "^inventario-hu07-e2e-performance-[a-f0-9]+$")
            || Environment.GetEnvironmentVariable("Testing__SqlHost") != "sqlserver")
            throw new InvalidOperationException("El perfil de carga requiere un proyecto HU-07 performance dedicado y seed 7007.");
        var password = Environment.GetEnvironmentVariable("Testing__SqlAdminPassword")
            ?? throw new InvalidOperationException("Falta la credencial del SQL de pruebas aislado.");
        var runtimeUser = Environment.GetEnvironmentVariable("SQL_APP_USER") ?? "inventory_app";
        var runtimePassword = Environment.GetEnvironmentVariable("SQL_APP_PASSWORD") ?? "LocalSqlApp_2026!";
        const string database = "InventoryHu07Performance";
        var adminConnection = new SqlConnectionStringBuilder { DataSource = "sqlserver", InitialCatalog = database,
            UserID = "sa", Password = password, Encrypt = true, TrustServerCertificate = true }.ConnectionString;
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:Host"] = "sqlserver", ["Database:Name"] = database, ["Database:User"] = runtimeUser,
            ["Database:Password"] = runtimePassword, ["Database:TrustServerCertificate"] = "true"
        }).Build();
        var keys = Path.Combine(Path.GetTempPath(), "hu07-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keys);
        try
        {
            await using var admin = MasterDataTestData.BootstrapServices(settings, adminConnection, keys);
            var run = Guid.NewGuid().ToString("N");
            var accounts = new List<TestAccount>();
            foreach (var (label, role) in new[] { ("master", "GestorMaestros"), ("stock", "Operario") }
                .Concat(Enumerable.Range(0, 25).Select(index => ($"buyer{index:D2}", "Comprador"))))
                accounts.Add(await MasterDataTestData.CreateAccountAsync(admin, $"{run}.{label}@hu07.performance.test", $"Perf_{Guid.NewGuid():N}!aA9", role));
            var masterAccount = accounts[0]; var stockAccount = accounts[1];
            using var master = await LoginAsync(masterAccount, cancellation);
            using var stockHttp = await LoginAsync(stockAccount, cancellation);
            var families = new Guid[25]; var suppliers = new Guid[100]; var articles = new Guid[10_000]; var locations = new Guid[50];
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = 25, CancellationToken = cancellation };
            await Parallel.ForEachAsync(Enumerable.Range(0, families.Length), parallel, async (index, token) => {
                families[index] = (await WriteAsync(master, "/api/v1/families", new { code = $"PF-{run[..8]}-{index:D2}", name = "Familia carga" }, token)).GetProperty("id").GetGuid();
            });
            await Parallel.ForEachAsync(Enumerable.Range(0, suppliers.Length), parallel, async (index, token) => {
                suppliers[index] = (await WriteAsync(master, "/api/v1/suppliers", new { code = $"PS-{run[..8]}-{index:D3}", name = $"Proveedor carga {index:D3}", email = "captura@performance.test" }, token)).GetProperty("id").GetGuid();
            });
            await Parallel.ForEachAsync(Enumerable.Range(0, articles.Length), parallel, async (index, token) => {
                articles[index] = (await WriteAsync(master, "/api/v1/articles", new { sku = $"PA-{run[..8]}-{index:D5}", name = $"Artículo carga {index:D5}",
                    familyId = families[index % families.Length], baseUnit = "UD", allowsFractional = false, tracksLot = false, isActive = true }, token)).GetProperty("id").GetGuid();
            });
            Console.Error.WriteLine("HU07 performance: 10.000 artículos y 100 proveedores preparados por HTTP.");
            await Parallel.ForEachAsync(Enumerable.Range(0, locations.Length), parallel, async (index, token) => {
                locations[index] = (await WriteAsync(stockHttp, "/api/v1/locations", new { code = $"PL-{run[..8]}-{index:D2}", aisle = $"P{index:D2}", rack = "E01", level = "N01", kind = "Storage", isActive = true }, token)).GetProperty("id").GetGuid();
            });
            await using var runtime = MasterDataTestData.BootstrapServices(settings, DatabaseConfiguration.ApplicationConnection(settings), keys, services => {
                services.AddInventoryOperations(); services.AddStockCore(); services.AddScoped<ArticleReferenceTracker>();
            });
            ClaimsPrincipal principal;
            using (var scope = admin.CreateScope())
            {
                var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(stockAccount.Email))!;
                principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
                principal = await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(principal);
            }
            var gates = Enumerable.Range(0, 50).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
            try
            {
                var completed = 0;
                async ValueTask SeedHistoryAsync(int index, CancellationToken token) {
                    var group = index % 50; await gates[group].WaitAsync(token);
                    try
                    {
                        var allocations = Enumerable.Range(group * 200, 200).Select(article => new StockAllocation(articles[article], locations[group], null, 1m)).ToArray();
                        var command = StockCore.Prepare(StockOperation.Receive, new StockCommandContext(new("performance-receipt", Guid.NewGuid()), DateTimeOffset.UtcNow,
                            "Preparación sintética de carga HU-07", SupportNotApplicableReason: "Fixture SC-007", CounterpartyNotApplicableReason: "Datos sintéticos de ensayo"), allocations);
                        var accessor = runtime.GetRequiredService<IHttpContextAccessor>();
                        accessor.HttpContext = new DefaultHttpContext { User = principal };
                        try
                        {
                            using var scope = runtime.CreateScope();
                            await scope.ServiceProvider.GetRequiredService<InventoryOperationExecutor>().ExecuteAsync(new(Guid.NewGuid(), "POST",
                                $"/api/v1/performance-receipts/{command.SourceId:D}", "Receiving.Record", command.NormalizedBody), async (context, ct) =>
                                StockCore.Response(await context.Services.GetRequiredService<StockCore>().ApplyAsync(context, command, ct)), token);
                        }
                        finally { accessor.HttpContext = null; }
                        if (Interlocked.Increment(ref completed) % 500 == 0) Console.Error.WriteLine($"HU07 performance: {completed * 200:N0} entradas auditadas.");
                    }
                    finally { gates[group].Release(); }
                }
                // First use creates lots/balances and locks the article definitions. Seed each
                // group once before parallel updates to avoid contention on initial inserts.
                for (var index = 0; index < gates.Length; index++)
                    await SeedHistoryAsync(index, cancellation);
                await Parallel.ForEachAsync(Enumerable.Range(gates.Length, 5_000 - gates.Length),
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellation }, SeedHistoryAsync);
            }
            finally { foreach (var gate in gates) gate.Dispose(); }
            var buyers = accounts.Skip(2).ToArray(); var clients = new HttpClient[buyers.Length];
            for (var index = 0; index < clients.Length; index++) clients[index] = await LoginAsync(buyers[index], cancellation);
            var orders = new object[10_000];
            try
            {
                // Each client owns one sequential lane, preserving cookies and distributing supplier locks.
                await Parallel.ForEachAsync(Enumerable.Range(0, 25), parallel, async (lane, token) => {
                    for (var index = lane; index < orders.Length; index += 25)
                    {
                        var ordinal = index / 100; var count = ordinal < 40 ? 1 : ordinal < 70 ? 10 : 20;
                        var supplier = suppliers[index % suppliers.Length];
                        var lines = Enumerable.Range(0, count).Select(line => new { id = Guid.NewGuid(), articleId = articles[(index * 17 + line * 31 + 7007) % articles.Length], quantity = "10", unitPrice = "12.50" }).ToArray();
                        var created = await WriteAsync(clients[lane], "/api/v1/purchase-orders", new { supplierId = supplier, expectedDeliveryDate = "2026-12-01", lines }, token);
                        orders[index] = new { id = created.GetProperty("id").GetGuid(), number = created.GetProperty("number").GetString(), supplierId = supplier, lineCount = count };
                    }
                });
            }
            finally { foreach (var client in clients) client.Dispose(); }
            var counts = await InspectAsync(project, false, cancellation);
            var verified = JsonSerializer.SerializeToElement(counts);
            if (verified.GetProperty("articles").GetInt64() != 10_000 || verified.GetProperty("orders").GetInt64() != 10_000
                || verified.GetProperty("historyEntries").GetInt64() != 1_000_000)
                throw new InvalidOperationException("Los recuentos reales no corresponden al perfil SC-007.");
            Console.Error.WriteLine("HU07 performance: perfil completo preparado y recuentos SQL verificados.");
            return new { seed = 7007, database, buyers = buyers.Select(value => new { value.Id, value.Email, value.Password }),
                articles = articles.Select((id, index) => new { id, sku = $"PA-{run[..8]}-{index:D5}" }),
                suppliers = suppliers.Select((id, index) => new { id, code = $"PS-{run[..8]}-{index:D3}" }),
                orders, counts, historyEntries = 1_000_000, articleCount = 10_000, initialOrderCount = 10_000 };
        }
        finally { if (Directory.Exists(keys)) Directory.Delete(keys, true); }
    }

    private static async Task<HttpClient> LoginAsync(TestAccount account, CancellationToken cancellation)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri("http://app:8080"), Timeout = TimeSpan.FromMinutes(2) };
        try
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf", cancellation);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = account.Email, password = account.Password }) };
            request.Headers.Add("X-XSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
            using var response = await client.SendAsync(request, cancellation); response.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", (await response.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("requestToken").GetString());
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task<JsonElement> WriteAsync(HttpClient client, string resource, object body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, resource) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"La preparación HTTP {resource} devolvió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellation)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("resource");
    }
}
