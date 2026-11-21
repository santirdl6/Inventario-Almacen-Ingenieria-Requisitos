namespace Inventario.Api.Domain.Stock;

public enum CycleCountState
{
    Draft,
    Counting,
    AwaitingApproval,
    Confirmed,
    Cancelled
}

/// <summary>Línea esperada capturada al iniciar: bucket con físico mayor que cero (research D-05).</summary>
public sealed record CountExpectedLine(Guid ArticleId, Guid LocationId, Guid LotId, string LotCode, decimal TheoreticalPhysical);

/// <summary>
/// Conteo cíclico (HU-04; data-model). Solo aplica reglas locales: alcance de 1 a 50 ubicaciones sin
/// repetir, transiciones de D-02, cantidades y motivos. Estado y existencia de las ubicaciones, lotes,
/// fracciones, permisos y congelación ajena los decide el comando tras los bloqueos (D-03, D-04).
/// Confirmar y cancelar no modifican líneas (D-07); un conteo cerrado tampoco lo admiten los triggers.
/// </summary>
public sealed class CycleCount
{
    public const int MaxLocations = 50;
    public const int CancelReasonMaxLength = 500;
    public const string CodePrefix = "CC-";

    private readonly List<CountLocation> locations = [];
    private readonly List<CountLine> lines = [];

    private CycleCount() { }

    public Guid Id { get; private set; }
    /// <summary>«CC-» + los 12 primeros hexadecimales del Id, en mayúsculas (D-10).</summary>
    public string CountCode { get; private set; } = "";
    public CycleCountState State { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? StartedBy { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }
    /// <summary>Operación que confirma o cancela; null mientras está abierto.</summary>
    public Guid? OperationId { get; private set; }
    /// <summary>Movimiento del ajuste; null si no hubo diferencias o se canceló (D-06).</summary>
    public Guid? AdjustmentMovementId { get; private set; }
    // rowversion: ETag del detalle (D-11, D-12).
    public byte[] Version { get; private set; } = [];
    public IReadOnlyList<CountLocation> Locations => locations;
    public IReadOnlyList<CountLine> Lines => lines;

    public bool IsClosed => State is CycleCountState.Confirmed or CycleCountState.Cancelled;

    public static string CodeFor(Guid id) => CodePrefix + id.ToString("N")[..12].ToUpperInvariant();

    /// <summary>Borrador con identidad decidida por el comando (estable en los reintentos del ejecutor).</summary>
    public static CycleCount CreateDraft(Guid id, IReadOnlyCollection<Guid> locationIds, Guid actorId, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("El conteo requiere un identificador.", nameof(id));
        RequireActor(actorId);
        var count = new CycleCount
        {
            Id = id,
            CountCode = CodeFor(id),
            State = CycleCountState.Draft,
            CreatedBy = actorId,
            CreatedAt = now
        };
        count.ApplyScope(locationIds);
        return count;
    }

    /// <summary>Sustituye el alcance de un borrador conservando las filas que siguen (PUT).</summary>
    public void ReplaceScope(IReadOnlyCollection<Guid> locationIds)
    {
        Require(CycleCountState.Draft);
        ApplyScope(locationIds);
    }

    /// <summary>Draft → Counting: congela todo el alcance y crea las líneas esperadas con su teórico.</summary>
    public void Start(IReadOnlyCollection<CountExpectedLine> expected, Guid actorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(expected);
        RequireActor(actorId);
        Require(CycleCountState.Draft);
        var scope = locations.Select(value => value.LocationId).ToHashSet();
        if (expected.Any(value => !scope.Contains(value.LocationId)))
            throw new ArgumentException("Todas las líneas esperadas deben estar en el alcance.", nameof(expected));
        var created = expected.Select(value => CountLine.Expected(Id, value)).ToList();
        if (created.GroupBy(value => (value.ArticleId, value.LocationId, value.LotCode)).Any(group => group.Count() > 1))
            throw new ArgumentException("Cada bucket solo puede aparecer una vez en el conteo.", nameof(expected));
        foreach (var location in locations)
            location.Freeze();
        lines.AddRange(created);
        State = CycleCountState.Counting;
        StartedBy = actorId;
        StartedAt = now;
    }

    /// <summary>Registra el contado de una línea; el cero se escribe (sin valor no es cero).</summary>
    public CountLine RecordCounted(Guid lineId, decimal counted, Guid actorId, DateTimeOffset now)
    {
        RequireActor(actorId);
        Require(CycleCountState.Counting);
        var line = Line(lineId);
        line.Record(counted, actorId, now);
        return line;
    }

    /// <summary>Hallazgo: mercancía presente en un bucket del alcance sin línea esperada.</summary>
    public CountLine AddFound(Guid articleId, Guid locationId, Guid? lotId, string lotCode, decimal counted, Guid actorId,
        DateTimeOffset now)
    {
        RequireActor(actorId);
        Require(CycleCountState.Counting);
        if (locations.All(value => value.LocationId != locationId))
            throw new ArgumentException("El hallazgo debe estar en una ubicación del alcance.", nameof(locationId));
        var line = CountLine.Found(Id, articleId, locationId, lotId, lotCode, counted, actorId, now);
        if (lines.Any(value => value.ArticleId == articleId && value.LocationId == locationId && value.LotCode == line.LotCode))
            throw new ArgumentException("El bucket ya tiene una línea en este conteo.", nameof(lotCode));
        lines.Add(line);
        return line;
    }

    /// <summary>Retira un hallazgo antes de enviar; las líneas esperadas no se retiran.</summary>
    public CountLine RemoveFound(Guid lineId)
    {
        Require(CycleCountState.Counting);
        var line = Line(lineId);
        if (!line.IsFound)
            throw new InvalidOperationException("Solo se pueden retirar hallazgos.");
        lines.Remove(line);
        return line;
    }

    /// <summary>Counting → AwaitingApproval; exige el contado de todas las líneas.</summary>
    public void Submit(Guid actorId, DateTimeOffset now)
    {
        RequireActor(actorId);
        Require(CycleCountState.Counting);
        if (lines.Any(value => value.CountedPhysical is null))
            throw new InvalidOperationException("Todas las líneas deben estar contadas antes de enviar.");
        State = CycleCountState.AwaitingApproval;
        SubmittedBy = actorId;
        SubmittedAt = now;
    }

    /// <summary>Revisión: fija o borra el motivo de una línea sin cambiar el contado (D-07, H6).</summary>
    public CountLine Review(Guid lineId, string? reason, Guid actorId)
    {
        RequireActor(actorId);
        Require(CycleCountState.AwaitingApproval);
        var line = Line(lineId);
        line.SetReason(reason, actorId);
        return line;
    }

    /// <summary>
    /// AwaitingApproval → Confirmed: aprobación, operación, movimiento del ajuste (si lo hay) y
    /// liberación del alcance. No modifica líneas (D-07).
    /// </summary>
    public void Confirm(Guid operationId, Guid? adjustmentMovementId, Guid actorId, DateTimeOffset now)
    {
        RequireOperation(operationId);
        RequireActor(actorId);
        Require(CycleCountState.AwaitingApproval);
        if (lines.Any(value => value.HasDifference && value.Reason is null))
            throw new InvalidOperationException("Cada diferencia necesita su motivo antes de confirmar.");
        if (lines.Any(value => value.HasDifference) != adjustmentMovementId is not null)
            throw new ArgumentException("El ajuste existe solo si hay diferencias.", nameof(adjustmentMovementId));
        foreach (var location in locations)
            location.Release();
        State = CycleCountState.Confirmed;
        ApprovedBy = actorId;
        ApprovedAt = now;
        OperationId = operationId;
        AdjustmentMovementId = adjustmentMovementId;
    }

    /// <summary>Cancela un conteo abierto con motivo; conserva líneas y libera el alcance.</summary>
    public void Cancel(Guid operationId, string reason, Guid actorId, DateTimeOffset now)
    {
        RequireOperation(operationId);
        RequireActor(actorId);
        if (IsClosed)
            throw new InvalidOperationException("El conteo ya está cerrado.");
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > CancelReasonMaxLength)
            throw new ArgumentException($"El motivo de cancelación tiene entre 1 y {CancelReasonMaxLength} caracteres.", nameof(reason));
        foreach (var location in locations)
            location.Release();
        State = CycleCountState.Cancelled;
        CancelledBy = actorId;
        CancelledAt = now;
        CancelReason = normalized;
        OperationId = operationId;
    }

    public CountLine Line(Guid lineId) =>
        lines.SingleOrDefault(value => value.Id == lineId)
        ?? throw new KeyNotFoundException("La línea no pertenece al conteo.");

    private void ApplyScope(IReadOnlyCollection<Guid> locationIds)
    {
        ArgumentNullException.ThrowIfNull(locationIds);
        if (locationIds.Count == 0) throw new ArgumentException("El conteo necesita al menos una ubicación.", nameof(locationIds));
        if (locationIds.Count > MaxLocations)
            throw new ArgumentException($"El conteo admite como máximo {MaxLocations} ubicaciones.", nameof(locationIds));
        if (locationIds.Any(value => value == Guid.Empty))
            throw new ArgumentException("Las ubicaciones requieren identificador.", nameof(locationIds));
        if (locationIds.Distinct().Count() != locationIds.Count)
            throw new ArgumentException("Cada ubicación solo puede aparecer una vez.", nameof(locationIds));
        locations.RemoveAll(value => !locationIds.Contains(value.LocationId));
        foreach (var locationId in locationIds.Where(value => locations.All(location => location.LocationId != value)))
            locations.Add(new CountLocation(Id, locationId));
    }

    private void Require(CycleCountState state)
    {
        if (State != state)
            throw new InvalidOperationException($"El conteo está en {State} y la operación exige {state}.");
    }

    private static void RequireActor(Guid actorId)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("El conteo requiere su responsable.", nameof(actorId));
    }

    private static void RequireOperation(Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("El cierre requiere su operación.", nameof(operationId));
    }
}
