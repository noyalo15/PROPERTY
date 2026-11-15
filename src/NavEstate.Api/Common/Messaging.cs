using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NavEstate.Api.Common;

public static partial class Phone
{
    /// <summary>Normalises Kenyan numbers to +2547XXXXXXXX / +2541XXXXXXXX. Returns null if not a valid number.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var d = NonDigits().Replace(raw, "");
        if (d.StartsWith("254") && d.Length == 12) return "+" + d;
        if (d.StartsWith('0') && d.Length == 10) return "+254" + d[1..];
        if ((d.StartsWith('7') || d.StartsWith('1')) && d.Length == 9) return "+254" + d;
        if (raw.TrimStart().StartsWith('+') && d.Length is >= 8 and <= 15) return "+" + d;
        return null;
    }

    [GeneratedRegex("[^0-9]")] private static partial Regex NonDigits();
}

public class SmsOptions
{
    public const string Section = "AfricasTalking";
    public string Username { get; set; } = "sandbox";
    public string ApiKey { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.africastalking.com";
}

public interface ISmsSender
{
    Task<bool> SendAsync(string phone, string message, CancellationToken ct = default);
}

/// <summary>Africa's Talking SMS. With no API key configured it only logs the message (development).</summary>
public class AfricasTalkingSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<AfricasTalkingSmsSender> log) : ISmsSender
{
    public async Task<bool> SendAsync(string phone, string message, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey))
        {
            log.LogWarning("SMS not sent (AfricasTalking:ApiKey not configured). To {Phone}: {Message}", phone, message);
            return false;
        }
        var form = new Dictionary<string, string> { ["username"] = o.Username, ["to"] = phone, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(o.SenderId)) form["from"] = o.SenderId;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/version1/messaging") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Add("apiKey", o.ApiKey);
        req.Headers.Add("Accept", "application/json");
        try
        {
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) log.LogWarning("Africa's Talking returned {Status}", res.StatusCode);
            return res.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "SMS send failed");
            return false;
        }
    }
}

/// <summary>Keeps the last SMS per phone in memory; used in development and tests to read OTP codes.</summary>
public class CapturingSmsSender : ISmsSender
{
    public Dictionary<string, string> Last { get; } = new();
    public Task<bool> SendAsync(string phone, string message, CancellationToken ct = default)
    {
        lock (Last) Last[phone] = message;
        return Task.FromResult(true);
    }
}
