using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using StoreManagement.Api.Models;

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
    public string PasswordResetUrl { get; init; } = string.Empty;
}

public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;
    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task SendPasswordResetAsync(string recipient, string resetUrl, PasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        var gps = request.GpsLatitude.HasValue && request.GpsLongitude.HasValue
            ? $"{request.GpsLatitude.Value}, {request.GpsLongitude.Value} (shared with permission)" : "Not shared";
        var body = $@"<p>A password reset was requested for your BillMate account.</p>
<p><a href=""{WebUtility.HtmlEncode(resetUrl)}"">Reset your password</a></p>
<p>This single-use link expires in one hour. If you did not request it, ignore this email.</p>
<h3>Request details</h3><ul>
<li>Server time (UTC): {request.RequestedAt:yyyy-MM-dd HH:mm:ss}</li>
<li>Device type: {WebUtility.HtmlEncode(Unknown(request.DeviceType))}</li>
<li>Operating system: {WebUtility.HtmlEncode(Unknown(request.OperatingSystem))}</li>
<li>Browser: {WebUtility.HtmlEncode(Unknown(request.Browser))}</li>
<li>Device/app installation ID: {WebUtility.HtmlEncode(Unknown(request.DeviceId))}</li>
<li>Public IP: {WebUtility.HtmlEncode(Unknown(request.PublicIp))}</li>
<li>Approximate city/region: {WebUtility.HtmlEncode(Location(request))}</li>
<li>GPS location: {WebUtility.HtmlEncode(gps)}</li></ul>";
        return SendAsync(recipient, "Reset your BillMate password", body, cancellationToken);
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
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();
        using var client = new SmtpClient();
        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, SecureSocketOptions.SslOnConnect, cancellationToken);
        await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        _logger.LogInformation("Security email accepted by the configured SMTP server.");
    }
    private static string Unknown(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
    private static string Location(PasswordResetRequest request) =>
        string.Join(", ", new[] { request.City, request.Region }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } location ? location : "Unavailable";
}
