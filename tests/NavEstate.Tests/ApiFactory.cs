using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NavEstate.Api.Common;
using Xunit;

namespace NavEstate.Tests;

/// <summary>
/// Runs the real API against a throwaway SQL Server database (migrations, row-level security and
/// demo seed included). Point NAVESTATE_TEST_SQL at a SQL Server 2019+ instance; the default is the
/// local Docker container from the README.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "Test#Passw0rd1";
    readonly string _db = $"NavEstate_Test_{Guid.NewGuid():N}"[..30];
    static string Server => Environment.GetEnvironmentVariable("NAVESTATE_TEST_SQL")
        ?? "Server=localhost,1433;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True";
    public string ConnectionString => $"{Server};Database={_db}";
    public CapturingSmsSender Sms { get; } = new();
    public bool RequireStaffTwoFactor { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:NavEstate", ConnectionString);
        builder.UseSetting("Jobs:RunServer", "false");
        builder.UseSetting("RateLimits:SignInsPerMinute", "1000"); // every test signs in
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
        builder.UseSetting("Auth:RequireStaffTwoFactor", RequireStaffTwoFactor ? "true" : "false");
        builder.UseSetting("BusinessCentral:UseMock", "true");
        builder.UseSetting("Files:Root", Path.Combine(Path.GetTempPath(), _db));
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<ISmsSender>();
            s.AddSingleton<ISmsSender>(Sms);
        });
    }

    public Task InitializeAsync()
    {
        _ = Server; // touch the configuration early
        _ = Services; // builds the host: migrates and seeds
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var c = new SqlConnection($"{Server};Database=master");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_db}') IS NOT NULL BEGIN ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient Client()
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-NavEstate", "1");
        return c;
    }

    public async Task<HttpClient> LoginAsync(string user)
    {
        var c = Client();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email = $"{user}@navestate.local", password = DemoPassword });
        r.EnsureSuccessStatusCode();
        return c;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
