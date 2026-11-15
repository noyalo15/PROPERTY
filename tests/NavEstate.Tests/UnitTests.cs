using System.Text;
using FluentAssertions;
using NavEstate.Api.Bc;
using NavEstate.Api.Common;
using NavEstate.Api.Features;
using Xunit;

namespace NavEstate.Tests;

public class BcSoapTests
{
    [Fact]
    public void Envelope_matches_the_spec_and_keeps_parameter_order()
    {
        var xml = BcSoap.BuildEnvelope("PostInvoice",
        [
            new("externalRef", "INV-2026-11-000123"), new("customerNo", "T00104"), new("postingDate", "2026-11-01"),
            new("currencyCode", ""), new("propertyCode", "RIVERSIDE"), new("unitCode", "A-104"), new("lines", "RENT;Rent Nov & Dec;45000.00")
        ]);
        xml.Should().StartWith("<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:ne=\"urn:microsoft-dynamics-schemas/codeunit/NavEstate\"><soap:Body><ne:PostInvoice>");
        xml.Should().Contain("<ne:externalRef>INV-2026-11-000123</ne:externalRef><ne:customerNo>T00104</ne:customerNo>");
        xml.Should().Contain("<ne:lines>RENT;Rent Nov &amp; Dec;45000.00</ne:lines>", "XML special characters must be escaped");
        xml.IndexOf("externalRef", StringComparison.Ordinal).Should().BeLessThan(xml.IndexOf("lines", StringComparison.Ordinal));
        BcSoap.SoapAction("PostInvoice").Should().Be("\"urn:microsoft-dynamics-schemas/codeunit/NavEstate:PostInvoice\"");
    }

    [Theory]
    [InlineData("OK:PSI000123", true, "PSI000123", null)]
    [InlineData("ERR:Customer T00999 does not exist.", false, null, "Customer T00999 does not exist.")]
    [InlineData("129.5", true, "129.5", null)]
    public void Parses_return_value(string value, bool ok, string? doc, string? err)
    {
        var xml = $"<Soap:Envelope xmlns:Soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><Soap:Body><PostInvoice_Result xmlns=\"urn:microsoft-dynamics-schemas/codeunit/NavEstate\"><return_value>{value}</return_value></PostInvoice_Result></Soap:Body></Soap:Envelope>";
        var r = BcSoap.ParseResponse(xml);
        r.Success.Should().Be(ok);
        r.DocumentNo.Should().Be(doc);
        r.Error.Should().Be(err);
    }

    [Fact]
    public void Soap_fault_is_a_failure()
    {
        var xml = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>a:Microsoft.Dynamics.Nav.Types.Exceptions.NavNCLDialogException</faultcode><faultstring>The Gen. Bus. Posting Group does not exist.</faultstring></s:Fault></s:Body></s:Envelope>";
        var r = BcSoap.ParseResponse(xml);
        r.Success.Should().BeFalse();
        r.Error.Should().Contain("Gen. Bus. Posting Group");
    }

    [Fact]
    public async Task Soap_client_sends_basic_auth_soapaction_and_reads_result()
    {
        HttpRequestMessage? sent = null; string? body = null;
        var handler = new StubHandler(async req =>
        {
            sent = req; body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<Soap:Envelope xmlns:Soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><Soap:Body><Ping_Result xmlns=\"urn:microsoft-dynamics-schemas/codeunit/NavEstate\"><return_value>OK:CRONUS|2026-11-15T10:00:00Z</return_value></Ping_Result></Soap:Body></Soap:Envelope>")
            };
        });
        var opts = Microsoft.Extensions.Options.Options.Create(new BcOptions { SoapUrl = "http://bc.local:7047/BC130/WS/CRONUS/Codeunit/NavEstate", Username = "NAVESTATE-SVC", Password = "secret", UseMock = false });
        var client = new BcSoapClient(new HttpClient(handler), opts, Microsoft.Extensions.Logging.Abstractions.NullLogger<BcSoapClient>.Instance);

        var r = await client.CallAsync("Ping", []);

        r.Success.Should().BeTrue();
        r.DocumentNo.Should().Be("CRONUS|2026-11-15T10:00:00Z");
        sent!.Headers.Authorization!.Scheme.Should().Be("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(sent.Headers.Authorization.Parameter!)).Should().Be("NAVESTATE-SVC:secret");
        sent.Headers.GetValues("SOAPAction").Single().Should().Be("\"urn:microsoft-dynamics-schemas/codeunit/NavEstate:Ping\"");
        sent.Content!.Headers.ContentType!.ToString().Should().Be("text/xml; charset=utf-8");
        body.Should().Contain("<ne:Ping></ne:Ping>");
    }

    [Fact]
    public async Task Http_500_with_fault_is_a_failure_with_the_fault_text()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultstring>Locked</faultstring></s:Fault></s:Body></s:Envelope>")
        }));
        var client = new BcSoapClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new BcOptions { SoapUrl = "http://x" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BcSoapClient>.Instance);
        var r = await client.CallAsync("PostInvoice", []);
        r.Success.Should().BeFalse();
        r.Error.Should().Be("SOAP fault: Locked");
    }

    class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => f(request);
    }
}

public class BcFormatTests
{
    [Fact]
    public void Lines_strip_separators_truncate_descriptions_and_use_dot_decimals()
    {
        var s = BcFormat.Lines([("RENT", "Rent; November | 2026", 45000m), ("WATER", new string('x', 60), 1234.5m)]);
        s.Should().Be($"RENT;Rent  November   2026;45000.00|WATER;{new string('x', 50)};1234.50");
    }

    [Fact]
    public void AppliesTo_and_currency_and_dates()
    {
        BcFormat.AppliesTo([("PSI000123", 30000m), ("PSI000124", 2500.456m)]).Should().Be("PSI000123;30000.00|PSI000124;2500.46");
        BcFormat.Currency("KES").Should().Be("");
        BcFormat.Currency("usd").Should().Be("USD");
        BcFormat.Date(new DateOnly(2026, 11, 1)).Should().Be("2026-11-01");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    public void Retry_backoff_is_1_5_15_60_minutes(int attempts, int minutes) =>
        BcSyncWorker.DelayAfter(attempts).Should().Be(TimeSpan.FromMinutes(minutes));

    [Fact]
    public void Gives_up_after_five_attempts() => BcSyncWorker.DelayAfter(5).Should().BeNull();

    [Fact]
    public async Task Mock_is_idempotent_on_externalRef()
    {
        var bc = new MockBcClient();
        var a = await bc.CallAsync("PostInvoice", [new("externalRef", "INV-2026-11-000001")]);
        var b = await bc.CallAsync("PostInvoice", [new("externalRef", "INV-2026-11-000001")]);
        var c = await bc.CallAsync("PostInvoice", [new("externalRef", "INV-2026-11-000002")]);
        b.DocumentNo.Should().Be(a.DocumentNo);
        c.DocumentNo.Should().NotBe(a.DocumentNo);
    }
}

public class DomainRuleTests
{
    [Theory]
    [InlineData("0712345678", "+254712345678")]
    [InlineData("0712 345 678", "+254712345678")]
    [InlineData("254712345678", "+254712345678")]
    [InlineData("+254 712-345-678", "+254712345678")]
    [InlineData("712345678", "+254712345678")]
    [InlineData("0110123456", "+254110123456")]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void Normalises_kenyan_phone_numbers(string raw, string? expected) => Phone.Normalize(raw).Should().Be(expected);

    [Fact]
    public void Occupancy_excludes_units_that_cannot_be_let()
    {
        PropertiesController.Occupancy(108, 120, 2).Should().Be(91.5m);
        PropertiesController.Occupancy(0, 0, 0).Should().Be(0);
        PropertiesController.Occupancy(1, 1, 1).Should().Be(0);
    }

    [Fact]
    public void Water_bands_must_start_at_zero_and_be_contiguous()
    {
        WaterTariffsController.ValidateBands([new(0, 6, 60), new(6, 20, 85), new(20, null, 110)]).Should().BeNull();
        WaterTariffsController.ValidateBands([new(1, 6, 60)]).Should().Contain("start at 0");
        WaterTariffsController.ValidateBands([new(0, 6, 60), new(7, null, 85)]).Should().Contain("contiguous");
        WaterTariffsController.ValidateBands([new(0, null, 60), new(6, null, 85)]).Should().Contain("open-ended");
        WaterTariffsController.ValidateBands([]).Should().NotBeNull();
    }

    [Fact]
    public void Unit_import_parses_csv_and_reports_row_errors()
    {
        var csv = "Code,Block,Unit Type,Floor,Area Sqft,Default Rent,Currency,Status\n" +
                  "a-104,A,1br,1,550,\"32,000\",,vacant\n" +
                  "A-105,A,penthouse,1,,40000,KES,vacant\n" +
                  "A-106,A,2br,1,,abc,KES,\n" +
                  ",,,,,,,\n";
        var table = UnitsController.ReadCsv(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        var (rows, errors) = UnitsController.ParseRows(table, "KES");
        rows.Should().ContainSingle();
        rows[0].Should().BeEquivalentTo(new UnitsController.ImportRow(2, "A-104", "A", "1br", "1", 550, 32000, "KES", "vacant"));
        errors.Should().HaveCount(2);
        errors[0].Should().StartWith("Row 3: unit_type must be one of");
        errors[1].Should().Be("Row 4: default_rent must be a number");
    }
}
