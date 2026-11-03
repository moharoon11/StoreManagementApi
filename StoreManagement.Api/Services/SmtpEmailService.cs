using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NLog;

namespace StoreManagement.Api.Services;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 465;
    public string SmtpUsername { get; init; } = string.Empty;
    public string SmtpPassword { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = "BillMate Security";
}

public sealed class SmtpEmailService : IEmailService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly EmailOptions _options;
    public SmtpEmailService(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public Task SendPasswordResetOtpAsync(string recipient, string otp, CancellationToken cancellationToken = default)
    {
        var body = $@"<p>A password reset was requested for your BillMate account.</p>
<p>Your verification code is <strong style=""font-size: 24px; letter-spacing: 4px;"">{WebUtility.HtmlEncode(otp)}</strong></p>
<p>This code expires in 5 minutes and can be used once. If you did not request it, ignore this email.</p>";
        return SendAsync(recipient, "Your BillMate password reset code", body, cancellationToken);
    }

    public Task SendPasswordChangedConfirmationAsync(string recipient, CancellationToken cancellationToken = default) =>
        SendAsync(recipient, "Your BillMate password was changed",
            "<p>Your BillMate password was changed successfully. All existing sessions were signed out.</p><p>If this was not you, contact support immediately.</p>", cancellationToken);

    private async Task SendAsync(string recipient, string subject, string html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost) || string.IsNullOrWhiteSpace(_options.SmtpUsername) ||
            string.IsNullOrWhiteSpace(_options.SmtpPassword) || string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException("Email is not configured. Set Email:SmtpHost, Email:SmtpUsername, Email:SmtpPassword, and Email:FromAddress in the deployment environment.");
        }
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

            using var client = new SmtpClient();
            Logger.Info("Connecting to SMTP server {0}:{1} to send {2} to recipient domain {3}.",
                _options.SmtpHost, _options.SmtpPort, subject, RecipientDomain(recipient));
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, SecureSocketOptions.SslOnConnect, cancellationToken);
            Logger.Debug("SMTP connection established. Authenticating SMTP user {0}.", _options.SmtpUsername);
            await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            Logger.Info("Security email accepted by the configured SMTP server for recipient domain {0}.", RecipientDomain(recipient));
        }
        catch (Exception ex)
        {
            // Passing the exception as the first argument preserves its complete
            // type, stack trace, and inner-exception chain in NLog.
            Logger.Error(ex, "SMTP delivery failed for {0} to recipient domain {1} via {2}:{3}.",
                subject, RecipientDomain(recipient), _options.SmtpHost, _options.SmtpPort);
            throw;
        }
    }

    private static string RecipientDomain(string recipient)
    {
        var atIndex = recipient.LastIndexOf('@');
        return atIndex >= 0 && atIndex < recipient.Length - 1 ? recipient[(atIndex + 1)..] : "invalid-address";
    }
}
