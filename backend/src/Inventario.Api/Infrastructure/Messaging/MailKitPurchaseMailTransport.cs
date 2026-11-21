using System.Net.Sockets;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Inventario.Api.Infrastructure.Messaging;

public sealed class MailKitPurchaseMailTransport(IOptions<PurchaseMailOptions> configuration,
    IOptions<PurchaseDeliveryOptions> deliveryOptions, ILogger<MailKitPurchaseMailTransport> logger) : IPurchaseMailTransport
{
    public async Task<PurchaseMailObservation> SendAsync(byte[] bytes, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(deliveryOptions.Value.TimeoutSeconds));
        var token = timeout.Token; var sending = false;
        using var client = new SmtpClient();
        try
        {
            await using var input = new MemoryStream(bytes, writable: false);
            using var message = await MimeMessage.LoadAsync(input, token);
            var options = configuration.Value;
            await client.ConnectAsync(options.Host, options.Port, options.SocketOptions, token);
            if (!string.IsNullOrEmpty(options.Username)) await client.AuthenticateAsync(options.Username, options.Password!, token);
            sending = true;
            var format = FormatOptions.Default.Clone(); format.NewLineFormat = NewLineFormat.Dos;
            var result = await client.SendAsync(format, new SmtpSendRequest(message), token);
            var accepted = result.AcceptedRecipients.Count == 1 && result.RejectedRecipients.Count == 0;
            return new(accepted, !accepted, false, accepted ? null : "SmtpRejected",
                accepted ? null : "El servicio de correo rechazó el destinatario conservado.", DateTimeOffset.UtcNow);
        }
        catch (SmtpCommandException error)
        {
            return new(false, (int)error.StatusCode >= 500, false, "SmtpRejected",
                "El servicio de correo rechazó el intento. Consulta el estado antes de reintentar.", DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or MailKit.ProtocolException or MailKit.ServiceNotConnectedException)
        {
            return new(false, false, sending, sending ? "SmtpAcceptanceUncertain" : "SmtpUnavailable",
                sending ? "No se pudo comprobar la aceptación del correo; podría existir una copia del mismo pedido."
                    : "No se pudo conectar con el servicio de correo. La solicitud sigue conservada.", DateTimeOffset.UtcNow);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return new(false, true, false, "SmtpAuthenticationFailed", "El servicio de correo no aceptó la autenticación de la aplicación.", DateTimeOffset.UtcNow);
        }
        catch (NotSupportedException)
        {
            return new(false, true, false, sending ? "SmtpMessageUnsupported" : "SmtpTlsUnavailable",
                sending ? "El servicio de correo no admite el formato de dirección o mensaje conservado."
                    : "No se pudo establecer la conexión de correo con la seguridad configurada.", DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is MailKit.Security.SslHandshakeException or System.Security.Authentication.AuthenticationException)
        {
            return new(false, true, false, "SmtpTlsUnavailable", "No se pudo establecer la conexión de correo con la seguridad configurada.", DateTimeOffset.UtcNow);
        }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, token); }
                catch (Exception error) { logger.LogDebug("La desconexión SMTP no terminó; no cambia una aceptación conocida. Tipo: {ErrorType}", error.GetType().Name); }
            }
        }
    }
}
