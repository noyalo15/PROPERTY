namespace NavEstate.Api.Domain;

/// <summary>Audit columns every table carries (spec: created_at, created_by, updated_at, updated_by).</summary>
public interface IAudited
{
    DateTimeOffset CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}

/// <summary>Base for tables keyed by a sequential GUID.</summary>
public abstract class Entity : IAudited
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Marks a table that is not written to audit_log (high-volume technical tables).</summary>
public interface INotAudited { }
