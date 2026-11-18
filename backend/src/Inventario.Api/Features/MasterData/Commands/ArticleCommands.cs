using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Api.Features.MasterData.Commands;

public sealed record ArticleDto(Guid Id, string Sku, string Name, Guid FamilyId, string BaseUnit,
    bool AllowsFractional, bool TracksLot, bool IsActive, bool IsDefinitionLocked)
{
    public static ArticleDto From(Article article) => new(article.Id, article.Sku, article.Name, article.FamilyId,
        article.BaseUnit, article.AllowsFractional, article.TracksLot, article.IsActive, article.IsDefinitionLocked);
}
public sealed record ArticleMutation(Guid OperationId, ArticleDto Resource);
public sealed record ArticleWrite(string? Sku, string? Name, Guid? FamilyId, string? BaseUnit,
    bool? AllowsFractional, bool? TracksLot, bool? IsActive)
{
    public static ArticleWrite Read(JsonElement body, bool create)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new InventoryProblemException(400, "InvalidRequest", "Se requiere un objeto JSON.");
        foreach (var property in body.EnumerateObject())
            if (property.Name is not ("sku" or "name" or "familyId" or "baseUnit" or "allowsFractional" or "tracksLot" or "isActive"))
                throw InventoryProblemException.Validation(property.Name, "Este campo no es editable en artículos.");
        ArticleWrite value;
        try { value = body.Deserialize<ArticleWrite>(OperationResponse.JsonOptions)!; }
        catch (JsonException) { throw new InventoryProblemException(400, "InvalidRequest", "Los tipos o identificadores JSON no son válidos."); }
        if (!create && value.IsActive is null || body.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.Null)
            throw InventoryProblemException.Validation("isActive", "El estado debe estar definido.");
        return value with { Sku = value.Sku?.Trim().ToUpperInvariant(), Name = value.Name?.Trim(),
            BaseUnit = value.BaseUnit?.Trim().ToUpperInvariant(), IsActive = value.IsActive ?? true };
    }
}

public sealed class ArticleCommands(InventoryOperationExecutor executor)
{
    public Task<OperationResponse> WriteAsync(Guid key, Guid? id, ArticleWrite write, string? etag, CancellationToken cancellation)
    {
        var create = id is null; var articleId = id ?? Guid.NewGuid();
        var resource = create ? "/api/v1/articles" : $"/api/v1/articles/{articleId:D}";
        return executor.ExecuteAsync(new(key, create ? "POST" : "PUT", resource, "MasterData.Manage",
            JsonSerializer.SerializeToElement(write, OperationResponse.JsonOptions)), async (context, token) =>
        {
            if (write.FamilyId is null || write.FamilyId == Guid.Empty)
                throw InventoryProblemException.Validation("familyId", "Selecciona una familia existente.");
            if (write.AllowsFractional is null || write.TracksLot is null)
                throw InventoryProblemException.Validation(write.AllowsFractional is null ? "allowsFractional" : "tracksLot", "Define las reglas de cantidades y lotes.");
            var sku = MasterDataText.Code(write.Sku, "sku"); var name = MasterDataText.Name(write.Name);
            var unit = MasterDataText.BaseUnit(write.BaseUnit); var familyId = write.FamilyId.Value;
            await context.Locks.DocumentsAsync([new DocumentLock("family", familyId)], token);
            await context.Locks.ArticlesAsync([articleId], token);
            var db = context.Database;
            var article = create ? null : await db.Articles.SingleOrDefaultAsync(value => value.Id == articleId, token);
            if (!create && article is null) throw new InventoryProblemException(404, "NotFound", "El artículo ya no existe.");
            if (!create) EntityTags.RequireMatch(etag, article!.Version);
            var family = await db.Set<Family>().SingleOrDefaultAsync(value => value.Id == familyId, token);
            if (family is null || (create || article!.FamilyId != familyId) && !family.IsActive)
                throw InventoryProblemException.Validation("familyId", "La nueva asignación requiere una familia activa.");
            if (create && write.IsActive != true) throw InventoryProblemException.Validation("isActive", "Los artículos nuevos se crean activos.");
            var definitionChanged = create || article!.BaseUnit != unit || article.AllowsFractional != write.AllowsFractional
                || article.TracksLot != write.TracksLot;
            if (definitionChanged)
            {
                context.Services.GetRequiredService<ArticleDefinitionReadiness>().EnsureReady();
                var usage = await context.Services.GetRequiredService<IArticleUsageReader>().ReadAsync(articleId, token);
                if (!create && (article!.IsDefinitionLocked || usage.HasReferences || usage.HasHistory))
                    throw new InventoryProblemException(409, "ArticleDefinitionLocked", "La primera referencia bloquea unidad, lotes y fracciones, aunque después se retire.");
            }
            var before = article is null ? null : ArticleDto.From(article);
            var action = create ? MasterDataAction.Create : article!.IsActive != write.IsActive
                ? write.IsActive!.Value ? MasterDataAction.Reactivate : MasterDataAction.Deactivate : MasterDataAction.Update;
            if (create)
            {
                article = Article.Create(familyId, sku, name, unit, write.AllowsFractional.Value, write.TracksLot.Value, articleId);
                db.Articles.Add(article);
            }
            else
            {
                article!.Change(sku, name, familyId, unit, write.AllowsFractional.Value, write.TracksLot.Value, write.IsActive!.Value);
                db.Entry(article).Property(value => value.Name).IsModified = true;
            }
            if (!create && before!.IsActive != article!.IsActive)
                await context.Services.GetRequiredService<ArticleLifecycleCoordinator>().ApplyAsync(context, article, token);
            await db.SaveChangesAsync(token);
            var after = ArticleDto.From(article!);
            MasterDataAuditWriter.Record(context, MasterDataEntity.Article, articleId, action, before, after);
            var headers = new Dictionary<string, string> { ["ETag"] = EntityTags.Format(article!.Version) };
            if (create) headers["Location"] = $"/api/v1/articles/{articleId:D}";
            return OperationResponse.Json(create ? 201 : 200, new ArticleMutation(key, after), articleId, headers);
        }, cancellation);
    }
}
