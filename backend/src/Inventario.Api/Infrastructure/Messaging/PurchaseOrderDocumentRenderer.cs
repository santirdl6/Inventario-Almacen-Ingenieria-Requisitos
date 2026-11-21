using System.Net;
using System.Text;
using System.Globalization;
using Inventario.Api.Features.Purchasing;
using MimeKit;
using MimeKit.Utils;

namespace Inventario.Api.Infrastructure.Messaging;

public sealed record RenderedPurchaseDocument(byte[] DocumentBytes, byte[] MimeBytes, string MessageId);

public sealed class PurchaseOrderDocumentRenderer
{
    public const string TemplateVersion = "1";
    public string Html(PurchaseOrderDetail order)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Pedido ")
            .Append(E(order.Number)).Append("</title></head><body><h1>Pedido ").Append(E(order.Number))
            .Append("</h1><p>Proveedor: ").Append(E(order.SupplierSnapshot.Code)).Append(" — ").Append(E(order.SupplierSnapshot.Name))
            .Append("</p><p>Fecha prevista general: ").Append(order.ExpectedDeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(". Moneda: ").Append(E(order.Currency)).Append(".</p><table><thead><tr><th>Línea</th><th>Artículo</th><th>Cantidad</th><th>Unidad</th><th>Precio unitario</th><th>Fecha prevista</th></tr></thead><tbody>");
        foreach (var line in order.Lines)
            html.Append("<tr><td>").Append(line.Position).Append("</td><td>").Append(E(line.ArticleSnapshot.Sku)).Append(" — ")
                .Append(E(line.ArticleSnapshot.Name)).Append("</td><td>").Append(E(line.OrderedQuantity)).Append("</td><td>")
                .Append(E(line.ArticleSnapshot.BaseUnit)).Append("</td><td>").Append(E(line.UnitPrice)).Append(' ').Append(E(order.Currency))
                .Append("</td><td>").Append(line.ExpectedDeliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</td></tr>");
        return html.Append("</tbody></table></body></html>").ToString();
    }

    public async Task<RenderedPurchaseDocument> MessageAsync(PurchaseOrderDetail order, string recipient, string sender,
        DateTimeOffset createdAt, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(Html(order));
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sender)); message.To.Add(MailboxAddress.Parse(recipient));
        message.MessageId = MimeUtils.GenerateMessageId("inventario.local"); message.Date = createdAt;
        message.Subject = $"Pedido {order.Number}";
        var body = new BodyBuilder { TextBody = $"Pedido {order.Number} a {order.SupplierSnapshot.Name}.\nSe adjunta el documento revisado del pedido. Moneda: {order.Currency}." };
        body.Attachments.Add($"pedido-{order.Number}.html", bytes, ContentType.Parse("text/html; charset=utf-8"));
        message.Body = body.ToMessageBody();
        await using var output = new MemoryStream();
        var format = FormatOptions.Default.Clone(); format.NewLineFormat = NewLineFormat.Dos;
        await message.WriteToAsync(format, output, cancellation);
        return new(bytes, output.ToArray(), message.MessageId);
    }
}
