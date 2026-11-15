using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NavEstate.Api.Auth;
using NavEstate.Api.Common;
using NavEstate.Api.Domain;

namespace NavEstate.Api.Data;

/// <summary>
/// Sets SESSION_CONTEXT on every connection the API opens, so the SQL Server row-level
/// security policy (see migration RowLevelSecurity) filters properties and units by the
/// signed-in user's assignments. Background jobs and super admins bypass.
/// </summary>
public class SessionContextInterceptor(ICurrentUser user) : DbConnectionInterceptor
{
    const string Sql = "EXEC sp_set_session_context @key = N'user_id', @value = @uid; " +
                       "EXEC sp_set_session_context @key = N'rls_bypass', @value = @bypass;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = Build(connection);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        await using var cmd = Build(connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    DbCommand Build(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        var uid = cmd.CreateParameter(); uid.ParameterName = "@uid"; uid.Value = (object?)user.UserId ?? DBNull.Value;
        var bypass = cmd.CreateParameter(); bypass.ParameterName = "@bypass"; bypass.Value = user.BypassRowSecurity;
        cmd.Parameters.Add(uid); cmd.Parameters.Add(bypass);
        return cmd;
    }
}

/// <summary>Fills audit columns and writes app.audit_log rows with JSON old/new values.</summary>
public class AuditInterceptor(ICurrentUser user, TimeProvider clock) : SaveChangesInterceptor
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    void Apply(DbContext db)
    {
        var now = clock.Now();
        var uid = user.UserId ?? "system";
        var logs = new List<AuditLog>();

        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            if (entry.Entity is IAudited a)
            {
                if (entry.State == EntityState.Added) { a.CreatedAt = now; a.CreatedBy = uid; }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(IAudited.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAudited.CreatedBy)).IsModified = false;
                }
                a.UpdatedAt = now; a.UpdatedBy = uid;
            }

            if (entry.Entity is INotAudited || entry.Entity is AuditLog) continue;
            if (entry.Metadata.GetTableName() is not { } table) continue;
            if (entry.Metadata.GetSchema() == "app" && entry.Entity is not UserPropertyAssignment && entry.Entity is not AppUser) continue;

            logs.Add(new AuditLog
            {
                TableName = $"{entry.Metadata.GetSchema()}.{table}",
                RecordId = string.Join("|", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString())),
                Action = entry.State switch { EntityState.Added => "insert", EntityState.Modified => "update", _ => "delete" },
                OldValues = entry.State == EntityState.Added ? null : Serialize(entry, original: true),
                NewValues = entry.State == EntityState.Deleted ? null : Serialize(entry, original: false),
                UserId = uid,
                CreatedAt = now
            });
        }
        if (logs.Count > 0) db.Set<AuditLog>().AddRange(logs);
    }

    static readonly HashSet<string> Secret = ["PasswordHash", "SecurityStamp", "ConcurrencyStamp", "RowVersion"];

    static string Serialize(EntityEntry entry, bool original)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (Secret.Contains(p.Metadata.Name)) continue;
            if (entry.State == EntityState.Modified && !p.IsModified && !p.Metadata.IsPrimaryKey()) continue;
            dict[p.Metadata.GetColumnName()] = original ? p.OriginalValue : p.CurrentValue;
        }
        return JsonSerializer.Serialize(dict, Json);
    }
}
