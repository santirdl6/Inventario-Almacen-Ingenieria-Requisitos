using Inventario.Api.Infrastructure.Persistence;
using Inventario.Api.Features.Purchasing.Commands;
using Inventario.Api.Features.Purchasing.Queries;
using Inventario.Api.Infrastructure.Messaging;

namespace Inventario.Api.Features.Purchasing;

public static class PurchasingRegistration
{
    public static IServiceCollection AddPurchasing(this IServiceCollection services)
    {
        services.AddOptions<PurchasingOptions>().BindConfiguration("Purchasing")
            .Validate(PurchasingOptions.IsValid, "Purchasing:Currency debe ser una moneda ISO 4217 en mayúsculas.")
            .ValidateOnStart();
        services.AddScoped<PurchasingArticleUsageContributor>();
        services.AddScoped<IArticleUsageContributor>(provider => provider.GetRequiredService<PurchasingArticleUsageContributor>());
        services.AddScoped<IArticleFirstReferenceContributor>(provider => provider.GetRequiredService<PurchasingArticleUsageContributor>());
        services.AddScoped<PurchaseOrderDraftCommands>();
        services.AddScoped<PurchaseOrderQueries>();
        services.AddOptions<PurchaseMailOptions>().BindConfiguration("Smtp")
            .Validate<IHostEnvironment>(PurchaseMailOptions.IsValid, "La configuración SMTP requiere contacto, puerto y seguridad válidos.").ValidateOnStart();
        services.AddOptions<PurchaseDeliveryOptions>().BindConfiguration("PurchaseDelivery")
            .Validate(PurchaseDeliveryOptions.IsValid, "Los plazos de entrega deben ser positivos, lease > timeout + 30 y MaxAttempts entre 1 y 5.").ValidateOnStart();
        services.AddSingleton<PurchaseOrderDocumentRenderer>();
        services.AddSingleton<IPurchaseMailTransport, MailKitPurchaseMailTransport>();
        services.AddSingleton<PurchaseDeliveryWorker>();
        services.AddScoped<PurchaseDeliveryCommands>();
        services.AddScoped<PurchaseSendReview>();
        services.AddScoped<PurchaseOrderSendPreviewQuery>();
        services.AddScoped<RequestPurchaseOrderSend>();
        services.AddScoped<RetryPurchaseOrderDelivery>();
        services.AddScoped<PurchaseOrderDeliveryQueries>();
        return services;
    }
}
