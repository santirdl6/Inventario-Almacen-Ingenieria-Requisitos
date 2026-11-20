using System.Text.Json;
using Inventario.Api.Domain.Stock;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.Stock.CustomerOrders;

/// <summary>
/// Alta del pedido de cliente mínimo (HU-05 T008; research D-04, D-10) sobre el protocolo común de
/// A: el ejecutor autoriza (Shipping.Record), abre la transacción, adquiere operation:{key},
/// resuelve el replay y guarda OperationResult. Aquí se bloquean los documentos y los artículos, se
/// decide bajo bloqueo y se escribe el pedido. Sin edición ni cancelación (clarificación 3).
/// </summary>
public sealed class CustomerOrderCommands(InventoryOperationExecutor executor)
{
    public const string Permission = "Shipping.Record";
    public const string Resource = "/api/v1/customer-orders";

    public Task<OperationResponse> CreateAsync(Guid operation, CustomerOrderWrite write, CancellationToken cancellation)
    {
        // Identidad estable en los reintentos del ejecutor: se decide fuera de change.
        var id = Guid.NewGuid();
        var request = new OperationRequest(operation, "POST", Resource, Permission,
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions));
        return executor.ExecuteAsync(request, async (context, token) =>
        {
            var values = Validate(write);
            var articleIds = values.Lines.Select(line => line.ArticleId).ToList();
            // Orden del protocolo: documentos (una sola llamada) → artículos. Todo antes de decidir.
            await context.Locks.DocumentsAsync([
                new DocumentLock(StockDocuments.CustomerOrderReference, StockDocuments.ReferenceLockId(values.ExternalReference)),
                new DocumentLock(StockDocuments.CustomerOrder, id)], token);
            await context.Locks.ArticlesAsync(articleIds, token);
            var db = context.Database;

            // Comparación sin mayúsculas: la columna usa la intercalación CI_AS de Sku.
            if (await db.Set<CustomerOrder>().AsNoTracking().AnyAsync(order => order.ExternalReference == values.ExternalReference, token))
                throw new InventoryProblemException(409, "DuplicateCode",
                    $"La referencia {values.ExternalReference} ya pertenece a otro pedido.",
                    new Dictionary<string, string[]> { ["externalReference"] = ["Introduce una referencia que no esté usada."] });

            var articles = await db.Articles.Where(article => articleIds.Contains(article.Id)).ToDictionaryAsync(article => article.Id, token);
            var missing = new Dictionary<string, string[]>();
            for (var index = 0; index < values.Lines.Count; index++)
                if (!articles.ContainsKey(values.Lines[index].ArticleId))
                    missing[$"lines[{index}].articleId"] = ["El artículo no existe."];
            if (missing.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa los artículos marcados: ya no existen.", missing);

            var inactive = values.Lines.Select((line, index) => (Article: articles[line.ArticleId], Index: index))
                .Where(pair => !pair.Article.IsActive).ToList();
            if (inactive.Count > 0)
                throw new InventoryProblemException(409, "ArticleInactive",
                    $"No se pueden pedir artículos desactivados: {string.Join(", ", inactive.Select(pair => pair.Article.Sku))}.",
                    inactive.ToDictionary(pair => $"lines[{pair.Index}].articleId", _ => new[] { "El artículo está desactivado." }));

            var fractions = new Dictionary<string, string[]>();
            for (var index = 0; index < values.Lines.Count; index++)
            {
                var line = values.Lines[index];
                if (!articles[line.ArticleId].AllowsFractional && decimal.Truncate(line.Quantity) != line.Quantity)
                    fractions[$"lines[{index}].quantity"] = ["Este artículo solo admite cantidades enteras."];
            }
            if (fractions.Count > 0)
                throw new InventoryProblemException(422, "ValidationFailed", "Revisa las cantidades marcadas.", fractions);

            // La primera referencia, aunque sea un pedido, fija la definición del artículo (R-05).
            await context.Services.GetRequiredService<ArticleReferenceTracker>().MarkAsync(context, articleIds, token);
            var order = CustomerOrder.Create(id, values.ExternalReference, values.CustomerReference,
                values.Lines.Select(line => new CustomerOrderLineRequest(line.ArticleId, line.Quantity)).ToList());
            db.Add(order);
            await db.SaveChangesAsync(token);

            var headers = new Dictionary<string, string>
            {
                ["ETag"] = EntityTags.Format(order.Version),
                ["Location"] = $"{Resource}/{order.Id:D}"
            };
            return OperationResponse.Json(201, new CustomerOrderMutation(context.OperationId, CustomerOrderView.From(order, [])),
                order.Id, headers);
        }, cancellation);
    }

    internal sealed record Line(Guid ArticleId, decimal Quantity);

    internal sealed record Values(string ExternalReference, string CustomerReference, IReadOnlyList<Line> Lines);

    /// <summary>Validación de forma: reúne todos los errores antes de responder (D-10).</summary>
    internal static Values Validate(CustomerOrderWrite write)
    {
        var errors = new Dictionary<string, string[]>();
        var external = Text(write.ExternalReference, CustomerOrder.ExternalReferenceMaxLength, "externalReference",
            "Indica la referencia del pedido.", errors);
        var customer = Text(write.CustomerReference, CustomerOrder.CustomerReferenceMaxLength, "customerReference",
            "Indica el cliente.", errors);
        var lines = new List<Line>();
        var requested = write.Lines ?? [];
        if (requested.Count == 0) errors["lines"] = ["Añade al menos una línea."];
        else if (requested.Count > CustomerOrder.MaxLines) errors["lines"] = [$"El pedido admite como máximo {CustomerOrder.MaxLines} líneas."];
        var seen = new HashSet<Guid>();
        for (var index = 0; index < requested.Count; index++)
        {
            var line = requested[index];
            var articleValid = Guid.TryParse(line.ArticleId, out var article) && article != Guid.Empty;
            if (!articleValid) errors[$"lines[{index}].articleId"] = ["Elige un artículo."];
            else if (!seen.Add(article)) errors[$"lines[{index}].articleId"] = ["Este artículo ya está en otra línea del pedido."];

            var quantityProblem = !CustomerOrderWrite.TryQuantity(line.Quantity, out var quantity) ? "Indica una cantidad numérica."
                : quantity <= 0 ? "La cantidad debe ser mayor que cero."
                : decimal.Round(quantity, 3) != quantity ? "La cantidad admite como máximo tres decimales."
                : quantity >= 1_000_000_000_000_000m ? "La cantidad es demasiado grande."
                : null;
            if (quantityProblem is not null) errors[$"lines[{index}].quantity"] = [quantityProblem];
            if (articleValid && quantityProblem is null) lines.Add(new(article, quantity));
        }
        if (errors.Count > 0)
            throw new InventoryProblemException(422, "ValidationFailed", "Revisa los datos marcados del pedido.", errors);
        return new(external!, customer!, lines);
    }

    private static string? Text(string? value, int maximum, string field, string required, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0) errors[field] = [required];
        else if (trimmed.Length > maximum) errors[field] = [$"No puede superar {maximum} caracteres."];
        else return trimmed;
        return null;
    }
}
