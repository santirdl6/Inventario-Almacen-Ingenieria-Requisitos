#:project ../backend/tests/Inventario.IntegrationTests/Inventario.IntegrationTests.csproj
#:property PublishAot=false

using System.Text.Json;
using Inventario.IntegrationTests.Fixtures;

try
{
    if (Environment.GetEnvironmentVariable("HU08_E2E_PROVISION") != "1"
        || args.Length != 1 || args[0] is not ("provision" or "retire"))
        throw new InvalidOperationException("Este auxiliar requiere el modo E2E explícito y provision/retire.");

    var accounts = JsonSerializer.Deserialize<E2EAccount[]>(
        Environment.GetEnvironmentVariable("HU08_E2E_USERS_JSON") ?? "",
        new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Falta el lote de cuentas E2E.");
    await MasterDataTestData.PrepareE2EAsync(
        Environment.GetEnvironmentVariable("HU08_E2E_DATABASE") ?? "",
        Environment.GetEnvironmentVariable("Testing__SqlHost") ?? "",
        Environment.GetEnvironmentVariable("Testing__SqlAdminPassword") ?? "",
        accounts, retire: args[0] == "retire", profile: Environment.GetEnvironmentVariable("INVENTORY_E2E_PROFILE") ?? "hu08");
    Console.WriteLine(args[0] == "retire" ? "Cuentas E2E retiradas sin borrar actores." : "Cuentas E2E preparadas.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Preparación E2E rechazada ({error.GetType().Name}): {error.Message}");
    return 1;
}
