using System.Data.Common;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Inventario.IntegrationTests.Stock;

/// <summary>
/// Barrera de carreras para HU-05 (T012, T014). Retiene a la primera conexión que lee con EF la
/// fila <see cref="Table"/>/<see cref="Id"/> (p. ej. el artículo o la ubicación). Todos los
/// escritores implicados (reservar, liberar, PUT y DELETE de artículos, PUT de ubicaciones) hacen
/// esa lectura justo después de obtener el bloqueo de aplicación <c>article:{id}</c> o
/// <c>location:{id}</c>, así que la conexión retenida conserva ese bloqueo. La segunda operación, con
/// su propia conexión, llega al mismo bloqueo y espera.
/// <para>No se intercepta <c>sp_getapplock</c> porque InventoryLocks lo ejecuta con un comando
/// ADO.NET propio, fuera de los interceptores de EF. A diferencia de <c>SaveBarrier</c>, sirve
/// también para escritores que rechazan sin guardar (definición bloqueada, borrado con referencias,
/// ubicación no vacía).</para>
/// </summary>
internal sealed class LockBarrier : DbCommandInterceptor
{
    private int entered;

    public string? Table { get; private set; }
    public string? Id { get; private set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Arma la barrera sobre la fila; se llama después de preparar el escenario.</summary>
    public void Hold(string table, Guid id)
    {
        Id = id.ToString("D");
        Table = $"FROM [{table}]";
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Table is { } table && Id is { } id && command.CommandText.Contains(table, StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter =>
                parameter.Value?.ToString()?.Contains(id, StringComparison.OrdinalIgnoreCase) == true)
            && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        return result;
    }

    public static ApiFactory Factory(IdentityFixture fixture, LockBarrier barrier) =>
        new(fixture.Settings, "Development", services => services.ConfigureDbContext<InventoryDbContext>(
            (_, options) => options.AddInterceptors(barrier)));

    /// <summary>
    /// Lanza <paramref name="first"/>, espera a que quede retenida con su bloqueo, lanza
    /// <paramref name="second"/> y espera a que quede bloqueada en SQL Server antes de soltar.
    /// </summary>
    public async Task<(TFirst First, TSecond Second)> RaceAsync<TFirst, TSecond>(IdentityFixture fixture,
        Func<Task<TFirst>> first, Func<Task<TSecond>> second)
    {
        Task<TFirst> running;
        Task<TSecond> waiting;
        try
        {
            running = first();
            await Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            waiting = second();
            await MasterDataConcurrencyTests.WaitForApplicationLockAsync(fixture);
        }
        finally { Release.TrySetResult(); }
        return (await running, await waiting);
    }
}
