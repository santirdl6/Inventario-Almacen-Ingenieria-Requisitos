using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventario.Api.Domain.MasterData;
using Inventario.Api.Domain.Purchasing;
using Inventario.Api.Infrastructure.Http;
using Inventario.Api.Infrastructure.Messaging;
using Inventario.Api.Infrastructure.Persistence.Operations;
using Microsoft.Extensions.Options;

namespace Inventario.Api.Features.Purchasing.Queries;

public sealed record PurchaseSendPreview(string PurchaseOrderVersion, string SupplierVersion, string? RecipientEmail,
    string SenderEmail, PurchaseOrderDetail Document, string DocumentHtml, string ReviewToken, string[] BlockReasons);

public sealed class PurchaseSendReview(PurchaseOrderDocumentRenderer renderer, IOptions<PurchaseMailOptions> mail)
{
    public PurchaseSendPreview Build(PurchaseOrder order, Supplier supplier, IEnumerable<Article> articles)
    {
        var document = PurchaseOrderDetail.From(order); var html = renderer.Html(document);
        var masters = articles.OrderBy(value => value.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        var reasons = new List<string>();
        if (!order.IsEditable) reasons.Add("El envío ya se solicitó; consulta la entrega conservada.");
        if (!supplier.IsActive) reasons.Add("El proveedor está inactivo.");
        if (!PurchaseMailOptions.Mailbox(supplier.Email)) reasons.Add("Completa o corrige el correo del proveedor antes de enviar.");
        if (masters.Any(value => !value.IsActive)) reasons.Add("El pedido contiene artículos inactivos.");
        var fingerprint = RequestFingerprint.Create("POST",
            $"/api/v1/purchase-orders/{order.Id:D}/send", JsonSerializer.SerializeToElement(new
            {
                documentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))),
                purchaseOrderVersion = EntityTags.Format(order.Version), supplierVersion = EntityTags.Format(supplier.Version),
                recipientEmail = supplier.Email, senderEmail = mail.Value.From, templateVersion = PurchaseOrderDocumentRenderer.TemplateVersion,
                articles = masters.Select(value => new { value.Id, version = EntityTags.Format(value.Version) }).ToArray()
            }, OperationResponse.JsonOptions));
        var token = Convert.ToBase64String(Convert.FromHexString(fingerprint));
        return new(EntityTags.Format(order.Version), EntityTags.Format(supplier.Version), supplier.Email,
            mail.Value.From, document, html, token, reasons.ToArray());
    }
}
