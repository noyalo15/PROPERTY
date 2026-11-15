using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace NavEstate.Api.Domain;

// ---------- schema app (the design doc's "sys"; sys is reserved in SQL Server) ----------

public class AppUser : IdentityUser
{
    [MaxLength(100)] public string FullName { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? LandlordId { get; set; }
}

public class BcSyncQueueItem : Entity, INotAudited
{
    [MaxLength(20)] public string EntityType { get; set; } = "tenant";
    public Guid EntityId { get; set; }
    [MaxLength(50)] public string FunctionName { get; set; } = "";
    [MaxLength(50)] public string? ExternalRef { get; set; }
    /// <summary>JSON array of {name, value} in the SOAP parameter order.</summary>
    public string Payload { get; set; } = "[]";
    [MaxLength(20)] public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    [MaxLength(20)] public string? BcDocumentNo { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    /// <summary>Row that must succeed first (tenant before bill, bill before payment).</summary>
    public Guid? DependsOnId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class NumberSeries
{
    [MaxLength(20)] public string Code { get; set; } = "";
    [MaxLength(10)] public string Period { get; set; } = "";
    public long LastNo { get; set; }
}

public class AuditLog : INotAudited
{
    public long Id { get; set; }
    [MaxLength(100)] public string TableName { get; set; } = "";
    [MaxLength(100)] public string RecordId { get; set; } = "";
    [MaxLength(10)] public string Action { get; set; } = "";
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Gap filled: drives row-level security. Either a property or a whole landlord.</summary>
public class UserPropertyAssignment : Entity
{
    [MaxLength(450)] public string UserId { get; set; } = "";
    public Guid? LandlordId { get; set; }
    public Guid? PropertyId { get; set; }
}

public class BcPingLog : INotAudited
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public bool Ok { get; set; }
    [MaxLength(500)] public string? Message { get; set; }
    public int DurationMs { get; set; }
}
