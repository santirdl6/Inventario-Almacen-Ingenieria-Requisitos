#:project ../backend/tests/Inventario.IntegrationTests/Inventario.IntegrationTests.csproj
#:property PublishAot=false

using System.Text.Json;
using Inventario.IntegrationTests.Purchasing;

if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] is not ("inspect" or "retire"))
    throw new ArgumentException("Indica el proyecto de carga dedicado y opcionalmente inspect/retire.");
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(args.Length == 1 ? 60 : 2));
var prepared = args.Length == 1 ? await Hu07PerformanceData.PrepareAsync(args[0], cancellation.Token)
    : await Hu07PerformanceData.InspectAsync(args[0], args[1] == "retire", cancellation.Token);
Console.WriteLine("HU07_PERFORMANCE_JSON:" + JsonSerializer.Serialize(prepared, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
