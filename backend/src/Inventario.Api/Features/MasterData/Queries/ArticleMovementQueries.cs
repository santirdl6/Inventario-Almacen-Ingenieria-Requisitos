using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Queries;

public sealed record MovementActor(Guid Id, string Name);
public sealed record MovementDocument(string Type, Guid Id);
public sealed record MovementLocation(Guid Id, string Code, string Description);
public sealed record MovementLot(Guid Id, string Code);
public sealed record MovementFilterOption(Guid Id, string Label);
public sealed record ArticleMovementDto(Guid Id, Guid MovementId, string Kind, string Quantity, string BaseUnit,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, MovementActor Actor, string Reason,
    MovementDocument Document, string? SupportReference, string? SupportNotApplicableReason,
    string? CounterpartyReference, string? CounterpartyNotApplicableReason,
    MovementLocation? Origin, MovementLocation? Destination, MovementLot? Lot);
public sealed partial record ArticleMovementOptions(DateTimeOffset? From, DateTimeOffset? To, Guid? ActorId,
    Guid? LocationId, Guid? LotId, int Page, int PageSize)
{
    public static ArticleMovementOptions Parse(string? from, string? to, Guid? actorId, Guid? locationId, Guid? lotId, int? page, int? pageSize)
    {
        var pagination = MasterDataQueryOptions.Parse(null, null, page, pageSize);
        static DateTimeOffset? Date(string? text, string field)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!text.Contains('T') || !Zone().IsMatch(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                throw InventoryProblemException.Validation(field, "Indica fecha y hora ISO 8601 con zona horaria.");
            return value.ToUniversalTime();
        }
        var start = Date(from, "from"); var end = Date(to, "to");
        if (start is not null && end is not null && start >= end)
            throw InventoryProblemException.Validation("to", "El fin debe ser posterior al inicio; se consulta [inicio, fin).");
        return new(start, end, actorId, locationId, lotId, pagination.Page, pagination.PageSize);
    }
    [GeneratedRegex(@"(?:[zZ]|[+-]\d{2}:\d{2})$")]
    private static partial Regex Zone();
}

// El núcleo aporta la proyección de su único diario, usando el contexto/snapshot recibido.
// No se registra un proveedor vacío en H1 ni se crea un diario paralelo de maestros.
public interface IArticleMovementReader
{
    Task<PageResult<ArticleMovementDto>> ReadAsync(InventoryDbContext database, Guid articleId, ArticleMovementOptions options, CancellationToken cancellation);
    Task<PageResult<MovementFilterOption>> FiltersAsync(InventoryDbContext database, Guid articleId, string field,
        MasterDataQueryOptions options, CancellationToken cancellation);
}

public sealed class ArticleMovementQueries(InventoryDbContext database, IEnumerable<IArticleMovementReader> readers)
{
    public Task<PageResult<MovementFilterOption>> FiltersAsync(Guid id, string field, MasterDataQueryOptions options, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (field is not ("actor" or "location" or "lot"))
                throw InventoryProblemException.Validation("field", "Selecciona responsable, ubicación o lote.");
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            if (!await database.Articles.AnyAsync(value => value.Id == id, cancellation))
                throw new InventoryProblemException(404, "NotFound", "El artículo no existe.");
            var providers = readers.ToArray();
            if (providers.Length != 1)
                throw new InventoryProblemException(503, "DependencyUnavailable", "Falta el lector del diario compartido.");
            var result = await providers[0].FiltersAsync(database, id, field, options, cancellation);
            await transaction.CommitAsync(cancellation);
            return result;
        });

    public Task<PageResult<ArticleMovementDto>> ReadAsync(Guid id, ArticleMovementOptions options, CancellationToken cancellation) =>
        database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellation);
            if (!await database.Articles.AnyAsync(value => value.Id == id, cancellation))
                throw new InventoryProblemException(404, "NotFound", "El artículo no existe.");
            var providers = readers.ToArray();
            if (providers.Length != 1)
                throw new InventoryProblemException(503, "DependencyUnavailable", "Historial pendiente de integración: falta el lector del diario de stock compartido.");
            var result = await providers[0].ReadAsync(database, id, options, cancellation);
            await transaction.CommitAsync(cancellation);
            return result;
        });
}
