using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ChessApp.Backend.Services;

public class EmailOptions
{
    // "Log" (default: emails only go to the server log) or "Brevo"
    public string Provider { get; set; } = "Log";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "BlitzMate";
    public BrevoOptions Brevo { get; set; } = new();
}

public class BrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.brevo.com/";
}

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string textBody, CancellationToken ct = default);
}

/// <summary>
/// Writes emails to the server log instead of sending them. For local
/// development, and for staging until a real provider is configured.
/// </summary>
public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;

    public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toAddress, string subject, string textBody, CancellationToken ct = default)
    {
        _logger.LogInformation("Email to {To} (not sent, Email:Provider is Log)\nSubject: {Subject}\n{Body}",
            toAddress, subject, textBody);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends through Brevo's transactional email API (HTTPS, so it works on hosts
/// that block outgoing SMTP). The sender address must be verified in Brevo.
/// </summary>
public class BrevoEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly EmailOptions _options;

    public BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task SendAsync(string toAddress, string subject, string textBody, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = _options.FromName, email = _options.FromAddress },
                to = new[] { new { email = toAddress } },
                subject,
                textContent = textBody
            })
        };
        request.Headers.Add("api-key", _options.Brevo.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Brevo rejected the email ({(int)response.StatusCode}): {body}");
        }
    }
}
