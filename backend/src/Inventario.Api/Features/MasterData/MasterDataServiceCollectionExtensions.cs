using Inventario.Api.Domain.MasterData;
using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Features.MasterData.Commands;
using Inventario.Api.Features.MasterData.Queries;
using Inventario.Api.Features.MasterData.Maintenance;

namespace Inventario.Api.Features.MasterData;

internal static class MasterDataServiceCollectionExtensions
{
    public static IServiceCollection AddMasterData(this IServiceCollection services)
    {
        services.AddScoped<ArticleUsageCoverageValidator>();
        services.AddScoped<IArticleUsageReader, ArticleUsageReader>();
        services.AddScoped<FamilyCommands>();
        services.AddScoped<FamilyQueries>();
        services.AddScoped<SupplierCommands>();
        services.AddScoped<SupplierQueries>();
        services.AddScoped<ArticleCommands>();
        services.AddScoped<ArticleQueries>();
        services.AddScoped<DeleteArticleHandler>();
        services.AddScoped<ArticleMovementQueries>();
        services.AddScoped<ArticleDefinitionReadiness>();
        services.AddScoped<ArticleReferenceTracker>();
        services.AddScoped<ArticleLifecycleCoordinator>();
        services.AddScoped<PrepareHu08FamilyMapHandler>();
        return services;
    }
}
