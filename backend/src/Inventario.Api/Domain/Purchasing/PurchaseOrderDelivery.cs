using System.Security.Cryptography;

namespace Inventario.Api.Domain.Purchasing;

public sealed class PurchaseOrderDelivery
{
    private PurchaseOrderDelivery() { }
    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public Guid RequestOperationId { get; private set; }
    public string RequiredPermission { get; private set; } = "Purchasing.Send";
    public DateTimeOffset CreatedAt { get; private set; }
    public string RecipientEmail { get; private set; } = "";
    public string SenderEmail { get; private set; } = "";
    public string MessageId { get; private set; } = "";
    public string DocumentSnapshot { get; private set; } = "";
    public byte[] DocumentBytes { get; private set; } = [];
    public byte[] MimeBytes { get; private set; } = [];
    public string DocumentName { get; private set; } = "";
    public string MediaType { get; private set; } = "text/html; charset=utf-8";
    public byte[] DocumentHash { get; private set; } = [];
    public byte[] MessageHash { get; private set; } = [];
    public string TemplateVersion { get; private set; } = "1";
    public string ReviewedToken { get; private set; } = "";

    internal static PurchaseOrderDelivery Create(Guid id, PurchaseOrder order, Guid actor, Guid operation,
        string recipient, string sender, string messageId, string snapshot, byte[] document, byte[] mime,
        string templateVersion, string token, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || actor == Guid.Empty || operation == Guid.Empty || !order.IsEditable || string.IsNullOrWhiteSpace(order.Number))
            throw new ArgumentException("La entrega requiere una autorización y documento editables válidos.");
        static bool Mailbox(string value) => value.Length <= 256 && !value.Contains('\r') && !value.Contains('\n')
            && System.Net.Mail.MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
        if (!Mailbox(recipient) || !Mailbox(sender) || string.IsNullOrWhiteSpace(messageId) || messageId.Length > 255)
            throw new ArgumentException("La entrega requiere contactos e identidad de mensaje válidos.");
        using var parsed = System.Text.Json.JsonDocument.Parse(snapshot);
        if (document.Length == 0 || mime.Length == 0 || string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("El documento revisado y el mensaje deben estar conservados.");
        return new PurchaseOrderDelivery { Id = id, PurchaseOrderId = order.Id, RequestedBy = actor,
            RequestOperationId = operation, CreatedAt = createdAt.ToUniversalTime(), RecipientEmail = recipient,
            SenderEmail = sender, MessageId = messageId, DocumentSnapshot = snapshot, DocumentBytes = document.ToArray(),
            MimeBytes = mime.ToArray(), DocumentName = $"pedido-{order.Number}.html", TemplateVersion = templateVersion, ReviewedToken = token,
            DocumentHash = SHA256.HashData(document), MessageHash = SHA256.HashData(mime) };
    }
}
