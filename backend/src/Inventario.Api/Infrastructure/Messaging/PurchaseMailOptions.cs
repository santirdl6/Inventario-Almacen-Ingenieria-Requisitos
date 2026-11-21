using MailKit.Security;
using MimeKit;

namespace Inventario.Api.Infrastructure.Messaging;

public sealed class PurchaseMailOptions
{
    public string Host { get; set; } = "smtp";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "inventario@localhost.test";
    public string Security { get; set; } = "StartTls";
    public string? Username { get; set; }
    public string? Password { get; set; }
    internal SecureSocketOptions SocketOptions => Security switch
    { "None" => SecureSocketOptions.None, "SslOnConnect" => SecureSocketOptions.SslOnConnect, _ => SecureSocketOptions.StartTls };

    internal static bool Mailbox(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256
        && !value.Contains('\r') && !value.Contains('\n') && MailboxAddress.TryParse(value, out var mailbox) && mailbox.Address == value;

    internal static bool IsValid(PurchaseMailOptions options, IHostEnvironment environment) =>
        !string.IsNullOrWhiteSpace(options.Host) && options.Port is > 0 and <= 65535 && Mailbox(options.From)
        && options.Security is "None" or "StartTls" or "SslOnConnect"
        && (options.Security != "None" || environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        && string.IsNullOrEmpty(options.Username) == string.IsNullOrEmpty(options.Password);
}
