using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "SperoFlow";

    public bool UseSsl { get; init; } = true;
}

public sealed partial class SmtpAccountEmailSender : IAccountEmailSender
{
    private readonly SmtpOptions _smtp;
    private readonly AccountOptions _accounts;
    private readonly ILogger<SmtpAccountEmailSender> _logger;

    public SmtpAccountEmailSender(
        IOptions<SmtpOptions> smtp,
        IOptions<AccountOptions> accounts,
        ILogger<SmtpAccountEmailSender> logger)
    {
        _smtp = smtp.Value;
        _accounts = accounts.Value;
        _logger = logger;
    }

    public Task SendEmailConfirmationAsync(Guid userId, string email, string token, CancellationToken cancellationToken) =>
        SendAsync(
            email,
            "Confirm your SperoFlow email",
            "Confirm your account: " + BuildUrl("/confirm-email", userId, token),
            cancellationToken);

    public Task SendPasswordResetAsync(Guid userId, string email, string token, CancellationToken cancellationToken) =>
        SendAsync(
            email,
            "Reset your SperoFlow password",
            "Reset your password: " + BuildUrl("/reset-password", userId, token),
            cancellationToken);

    private async Task SendAsync(string email, string subject, string body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_smtp.Host) || string.IsNullOrWhiteSpace(_smtp.FromAddress))
        {
            LogSmtpNotConfigured(_logger, email);
            if (_accounts.RequireConfirmedEmail)
            {
                throw new InvalidOperationException("SMTP must be configured when confirmed email is required.");
            }

            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
            Subject = subject,
            Body = body,
        };
        message.To.Add(new MailAddress(email));
        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_smtp.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_smtp.Username, _smtp.Password),
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "SMTP is not configured; account mail to {Recipient} was not sent.")]
    private static partial void LogSmtpNotConfigured(ILogger logger, string recipient);
    private string BuildUrl(string path, Guid userId, string token)
    {
        var origin = _accounts.PublicWebOrigin.TrimEnd('/');
        return origin + path + "?userId=" + Uri.EscapeDataString(userId.ToString("D", System.Globalization.CultureInfo.InvariantCulture)) + "&token=" + Uri.EscapeDataString(token);
    }
}

public static class AccountEmailServiceCollectionExtensions
{
    public static IServiceCollection AddSperoFlowAccountMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.SectionName));
        services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
        return services;
    }
}
