using System.Globalization;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace NavEstate.Api.Bc;

public class BcOptions
{
    public const string Section = "BusinessCentral";
    /// <summary>e.g. http://localhost:7047/BC130/WS/CRONUS/Codeunit/NavEstate (codeunit 52140613 published as NavEstate).</summary>
    public string SoapUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>true = no BC calls; a mock returns OK:&lt;doc&gt; so the app runs without BC.</summary>
    public bool UseMock { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 100;
    public string CustPostingGroup { get; set; } = "TENANTS";
    public string VatBusPostingGroup { get; set; } = "DOMESTIC";
    public int BatchSize { get; set; } = 50;
}

public record BcParam(string Name, string Value);

public record BcResult(bool Success, string? DocumentNo, string? Error, string? Raw)
{
    public static BcResult Ok(string doc, string? raw = null) => new(true, doc, null, raw);
    public static BcResult Fail(string error, string? raw = null) => new(false, null, error, raw);
}

public interface IBcClient
{
    /// <summary>Calls a NavEstate codeunit function. Parameters are sent in the given order.</summary>
    Task<BcResult> CallAsync(string function, IReadOnlyList<BcParam> parameters, CancellationToken ct = default);
}

/// <summary>Builds and parses SOAP envelopes for the BC 13 NavEstate codeunit (52140613).</summary>
public static class BcSoap
{
    public const string Ns = "urn:microsoft-dynamics-schemas/codeunit/NavEstate";
    const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";

    public static string SoapAction(string function) => $"\"{Ns}:{function}\"";

    public static string BuildEnvelope(string function, IReadOnlyList<BcParam> parameters)
    {
        var sb = new StringBuilder();
        sb.Append($"<soap:Envelope xmlns:soap=\"{SoapNs}\" xmlns:ne=\"{Ns}\"><soap:Body><ne:{function}>");
        foreach (var p in parameters)
            sb.Append($"<ne:{p.Name}>{SecurityElement.Escape(p.Value)}</ne:{p.Name}>");
        sb.Append($"</ne:{function}></soap:Body></soap:Envelope>");
        return sb.ToString();
    }

    /// <summary>Reads return_value: "OK:&lt;doc&gt;" success, "ERR:&lt;reason&gt;" failure; a SOAP fault is a failure.</summary>
    public static BcResult ParseResponse(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (Exception ex) { return BcResult.Fail($"Invalid SOAP response: {ex.Message}", xml); }

        var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring");
        if (fault is not null) return BcResult.Fail($"SOAP fault: {fault.Value}", xml);

        var rv = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "return_value");
        if (rv is null) return BcResult.Fail("No return_value in SOAP response", xml);
        return ParseReturnValue(rv.Value, xml);
    }

    public static BcResult ParseReturnValue(string value, string? raw = null)
    {
        value = value.Trim();
        if (value.StartsWith("OK:", StringComparison.OrdinalIgnoreCase)) return BcResult.Ok(value[3..], raw ?? value);
        if (value.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase)) return BcResult.Fail(value[4..], raw ?? value);
        // GetExchangeRate returns a bare decimal.
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return BcResult.Ok(value, raw ?? value);
        return BcResult.Fail($"Unexpected return_value: {value}", raw ?? value);
    }
}

/// <summary>Formats values the way the C/AL codeunit parses them.</summary>
public static class BcFormat
{
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Amount(decimal a) => Math.Round(a, 2).ToString("0.00", CultureInfo.InvariantCulture);
    /// <summary>'' for KES (BC local currency).</summary>
    public static string Currency(string code) => string.Equals(code, "KES", StringComparison.OrdinalIgnoreCase) ? "" : code.ToUpperInvariant();
    public static string Clean(string? s, int max) => Truncate((s ?? "").Replace(";", " ").Replace("|", " ").Trim(), max);
    static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>lines = "CHARGECODE;Description;Amount|…" (amounts excl. VAT, description max 50).</summary>
    public static string Lines(IEnumerable<(string ChargeCode, string Description, decimal Amount)> lines) =>
        string.Join("|", lines.Select(l => $"{Clean(l.ChargeCode, 20)};{Clean(l.Description, 50)};{Amount(l.Amount)}"));

    /// <summary>appliesTo = "BCINVOICENO;Amount|…".</summary>
    public static string AppliesTo(IEnumerable<(string BcDocNo, decimal Amount)> items) =>
        string.Join("|", items.Select(i => $"{Clean(i.BcDocNo, 20)};{Amount(i.Amount)}"));
}

public class BcSoapClient(HttpClient http, IOptions<BcOptions> options, ILogger<BcSoapClient> log) : IBcClient
{
    public async Task<BcResult> CallAsync(string function, IReadOnlyList<BcParam> parameters, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SoapUrl)) return BcResult.Fail("BusinessCentral:SoapUrl is not configured");

        using var req = new HttpRequestMessage(HttpMethod.Post, o.SoapUrl)
        {
            Content = new StringContent(BcSoap.BuildEnvelope(function, parameters), Encoding.UTF8, "text/xml")
        };
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=utf-8");
        req.Headers.TryAddWithoutValidation("SOAPAction", BcSoap.SoapAction(function));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Username}:{o.Password}")));

        try
        {
            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                // BC returns HTTP 500 with a SOAP fault for C/AL errors.
                var parsed = body.Contains("Envelope", StringComparison.Ordinal) ? BcSoap.ParseResponse(body) : null;
                return BcResult.Fail(parsed?.Error ?? $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}", body);
            }
            return BcSoap.ParseResponse(body);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return BcResult.Fail($"Timeout after {o.TimeoutSeconds}s calling {function}");
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "BC call {Function} failed", function);
            return BcResult.Fail($"Connection error: {ex.Message}");
        }
    }
}

/// <summary>
/// Stand-in for BC while the server credentials are not available. Idempotent on externalRef like
/// the real codeunit. Tests can make the next calls fail with <see cref="FailNext"/>.
/// </summary>
public class MockBcClient : IBcClient
{
    readonly Dictionary<string, string> _byRef = new();
    readonly object _lock = new();
    int _seq;
    public int FailNext { get; set; }
    public string FailMessage { get; set; } = "Mock failure";
    public List<(string Function, IReadOnlyList<BcParam> Params)> Calls { get; } = [];

    public Task<BcResult> CallAsync(string function, IReadOnlyList<BcParam> parameters, CancellationToken ct = default)
    {
        lock (_lock)
        {
            Calls.Add((function, parameters));
            if (FailNext > 0) { FailNext--; return Task.FromResult(BcResult.Fail(FailMessage)); }

            string P(string name) => parameters.FirstOrDefault(p => p.Name == name)?.Value ?? "";
            var result = function switch
            {
                "Ping" => BcResult.Ok("MOCK BC 13|" + DateTime.UtcNow.ToString("s")),
                "GetExchangeRate" => BcResult.Ok("129.500000"),
                "GetCustomerBalance" => BcResult.Ok("0.00|0.00|"),
                "UpsertTenant" => BcResult.Ok(P("tenantNo")),
                "TransferTenantUnit" => BcResult.Ok(P("customerNo")),
                _ => null
            };
            if (result is not null) return Task.FromResult(result);

            var ext = P("externalRef");
            if (!_byRef.TryGetValue(ext, out var doc))
            {
                var prefix = function switch { "PostInvoice" => "PSI", "PostCreditMemo" => "PSCM", _ => "GJ" };
                doc = $"{prefix}{++_seq:D6}";
                if (ext != "") _byRef[ext] = doc;
            }
            return Task.FromResult(BcResult.Ok(doc));
        }
    }
}
