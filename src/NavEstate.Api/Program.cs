using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NavEstate.Api.Auth;
using NavEstate.Api.Bc;
using NavEstate.Api.Common;
using NavEstate.Api.Data;
using NavEstate.Api.Domain;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var conn = cfg.GetConnectionString("NavEstate") ?? throw new InvalidOperationException("ConnectionStrings:NavEstate is missing");

builder.Services.Configure<BcOptions>(cfg.GetSection(BcOptions.Section));
builder.Services.Configure<SmsOptions>(cfg.GetSection(SmsOptions.Section));
builder.Services.Configure<AuthOptions>(cfg.GetSection(AuthOptions.Section));
builder.Services.Configure<SeedOptions>(cfg.GetSection(SeedOptions.Section));
builder.Services.Configure<FileStoreOptions>(cfg.GetSection(FileStoreOptions.Section));
builder.Services.Configure<JobOptions>(cfg.GetSection(JobOptions.Section));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<SessionContextInterceptor>();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<NavEstateDbContext>((sp, o) => o
    .UseSqlServer(conn, sql => { sql.MigrationsHistoryTable("__ef_migrations", "app"); })
    .UseSnakeCaseNamingConvention()
    .AddInterceptors(sp.GetRequiredService<SessionContextInterceptor>(), sp.GetRequiredService<AuditInterceptor>()));

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<NavEstateDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<NavClaimsFactory>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "navestate.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(10);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Staff));
    o.AddPolicy(Policies.Setup, p => p.RequireRole(Roles.SuperAdmin, Roles.Accountant));
    o.AddPolicy(Policies.Operations, p => p.RequireRole(Roles.SuperAdmin, Roles.PropertyManager));
    o.AddPolicy(Policies.UserAdmin, p => p.RequireRole(Roles.SuperAdmin));
    o.AddPolicy(Policies.BcSync, p => p.RequireRole(Roles.SuperAdmin, Roles.Accountant));
    o.AddPolicy(Policies.TenantPortal, p => p.RequireRole(Roles.Tenant));
    o.AddPolicy(Policies.LandlordPortal, p => p.RequireRole(Roles.Landlord));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("otp", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5) }));
});

// Business Central
var bcOptions = cfg.GetSection(BcOptions.Section).Get<BcOptions>() ?? new BcOptions();
if (bcOptions.UseMock) builder.Services.AddSingleton<IBcClient, MockBcClient>();
else builder.Services.AddHttpClient<IBcClient, BcSoapClient>(c => c.Timeout = TimeSpan.FromSeconds(bcOptions.TimeoutSeconds));
builder.Services.AddScoped<BcSyncQueue>();
builder.Services.AddScoped<BcSyncWorker>();
builder.Services.AddScoped<BcHealthJobs>();

builder.Services.AddScoped<NumberSeriesService>();
builder.Services.AddScoped<Seeder>();
builder.Services.AddSingleton<IFileStore, LocalFileStore>();
// With no API key the sender only logs the message (development shows tenant OTP codes in the log).
builder.Services.AddHttpClient<ISmsSender, AfricasTalkingSmsSender>();

// Hangfire (SQL Server storage, schema "hangfire" in the NavEstate database)
var jobs = cfg.GetSection(JobOptions.Section).Get<JobOptions>() ?? new JobOptions();
builder.Services.AddHangfire(h => h
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(conn, new SqlServerStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromSeconds(15) }));
if (jobs.RunServer)
    builder.Services.AddHangfireServer(o => { o.WorkerCount = 2; o.ServerName = "navestate"; });

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (cfg.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NavEstateDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Seeder>().RunAsync(scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles(); // the built React app (wwwroot)
app.UseRateLimiter();
app.UseAuthentication();
app.UseNavEstateSecurity();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new SuperAdminDashboardFilter()], DashboardTitle = "NavEstate jobs" });

if (jobs.RunServer)
{
    var rec = app.Services.GetRequiredService<IRecurringJobManager>();
    var nairobi = new RecurringJobOptions { TimeZone = Nairobi.Zone };
    rec.AddOrUpdate<BcSyncWorker>("bc-sync", w => w.ProcessDueAsync(CancellationToken.None), Cron.Minutely(), nairobi);
    rec.AddOrUpdate<BcHealthJobs>("bc-ping", j => j.PingAsync(CancellationToken.None), "*/5 * * * *", nairobi);
    app.Services.GetRequiredService<IBackgroundJobClient>().Enqueue<BcHealthJobs>(j => j.PingAsync(CancellationToken.None));
    rec.AddOrUpdate<BcHealthJobs>("bc-usd-rate", j => j.RefreshUsdRateAsync(CancellationToken.None), "0 6 * * *", nairobi);
}

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallbackToFile("index.html"); // client-side routes

app.Run();

public class JobOptions
{
    public const string Section = "Jobs";
    /// <summary>Run the Hangfire server in this process (off in tests).</summary>
    public bool RunServer { get; set; } = true;
}

public class SuperAdminDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => context.GetHttpContext().User.IsInRole(Roles.SuperAdmin);
}

public partial class Program;
