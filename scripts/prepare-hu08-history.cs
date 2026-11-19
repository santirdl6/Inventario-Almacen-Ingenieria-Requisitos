#:project ../backend/tests/Inventario.IntegrationTests/Inventario.IntegrationTests.csproj
#:property PublishAot=false

using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;

if (Environment.GetEnvironmentVariable("HU08_E2E_PROVISION") != "1" || args.Length != 2)
    throw new InvalidOperationException("Este auxiliar requiere el entorno E2E explícito, modo y artículo.");
var accounts = JsonSerializer.Deserialize<E2EAccount[]>(Environment.GetEnvironmentVariable("HU08_E2E_USERS_JSON") ?? "",
    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Faltan cuentas E2E.");
var result = await Hu08HistoryE2EData.RunAsync(args[0], Guid.Parse(args[1]),
    Environment.GetEnvironmentVariable("HU08_E2E_DATABASE") ?? "", Environment.GetEnvironmentVariable("Testing__SqlHost") ?? "",
    Environment.GetEnvironmentVariable("SQL_APP_USER") ?? "inventory_app",
    Environment.GetEnvironmentVariable("SQL_APP_PASSWORD") ?? "LocalSqlApp_2026!", accounts);
Console.WriteLine("HU08_HISTORY_JSON:" + JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
