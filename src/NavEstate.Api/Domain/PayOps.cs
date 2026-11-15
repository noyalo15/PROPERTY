using System.ComponentModel.DataAnnotations;

namespace NavEstate.Api.Domain;

// ---------- schema pay (stage 4) ----------

public class Payment : Entity
{
    [MaxLength(30)] public string ReceiptNo { get; set; } = "";
    public Guid TenantId { get; set; }
    public Guid? LeaseId { get; set; }
    [MaxLength(20)] public string Channel { get; set; } = "mpesa";
    [MaxLength(35)] public string? Reference { get; set; }
    [MaxLength(100)] public string? PayerName { get; set; }
    [MaxLength(30)] public string? PayerPhone { get; set; }
    public DateOnly PaymentDate { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    public decimal Amount { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? AmountInLeaseCurrency { get; set; }
    [MaxLength(50)] public string? ChequeBank { get; set; }
    public DateOnly? ChequeDate { get; set; }
    [MaxLength(20)] public string? ChequeStatus { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "unallocated";
    [MaxLength(20)] public string? BcDocumentNo { get; set; }
    [MaxLength(20)] public string? BcSyncStatus { get; set; }
    [MaxLength(450)] public string? ReceivedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class PaymentAllocation : Entity
{
    public Guid PaymentId { get; set; }
    public Guid BillId { get; set; }
    public decimal Amount { get; set; }
}

public class MpesaTransaction : Entity
{
    [MaxLength(30)] public string TransId { get; set; } = "";
    public DateTimeOffset TransTime { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(30)] public string? Msisdn { get; set; }
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(50)] public string? BillRefNumber { get; set; }
    [MaxLength(20)] public string? BusinessShortCode { get; set; }
    public string Raw { get; set; } = "{}";
    public Guid? MatchedTenantId { get; set; }
    public Guid? MatchedUnitId { get; set; }
    [MaxLength(20)] public string MatchStatus { get; set; } = "unmatched";
    public Guid? PaymentId { get; set; }
}

/// <summary>Gap filled: referenced by bank_statement_lines.upload_id but not defined in the spec.</summary>
public class BankStatementUpload : Entity
{
    [MaxLength(200)] public string FileName { get; set; } = "";
    [MaxLength(50)] public string? BankAccount { get; set; }
    [MaxLength(450)] public string? UploadedBy { get; set; }
    public int RowCount { get; set; }
}

public class BankStatementLine : Entity
{
    public Guid UploadId { get; set; }
    public DateOnly TxnDate { get; set; }
    [MaxLength(50)] public string? Reference { get; set; }
    [MaxLength(200)] public string? Narration { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(20)] public string MatchStatus { get; set; } = "unmatched";
    public Guid? PaymentId { get; set; }
}

// ---------- schema ops (stage 5) ----------

public class Vendor : Entity
{
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(50)] public string? ServiceType { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(80)] public string? Email { get; set; }
    [MaxLength(20)] public string? KraPin { get; set; }
}

public class MaintenanceRequest : Entity
{
    [MaxLength(30)] public string RequestNo { get; set; } = "";
    public Guid UnitId { get; set; }
    public Guid? TenantId { get; set; }
    [MaxLength(20)] public string Category { get; set; } = "other";
    [MaxLength(20)] public string Priority { get; set; } = "medium";
    [MaxLength(2000)] public string Description { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "logged";
    [MaxLength(450)] public string? AssignedTo { get; set; }
    public Guid? VendorId { get; set; }
    public DateTimeOffset? TargetAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal? Cost { get; set; }
    [MaxLength(20)] public string ChargeTo { get; set; } = "none";
    public Guid? BillId { get; set; }
}

/// <summary>Replaces maintenance_requests.photos text[].</summary>
public class MaintenancePhoto : Entity
{
    public Guid RequestId { get; set; }
    [MaxLength(400)] public string FilePath { get; set; } = "";
    public DateTimeOffset? TakenAt { get; set; }
}

public class PreventiveTask : Entity
{
    public Guid PropertyId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public int EveryMonths { get; set; }
    public DateOnly NextDueDate { get; set; }
    public Guid? AssignedVendorId { get; set; }
}

public class Notice : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? PropertyId { get; set; }
    [MaxLength(20)] public string Channel { get; set; } = "sms";
    [MaxLength(200)] public string? Subject { get; set; }
    [MaxLength(4000)] public string Body { get; set; } = "";
    public DateTimeOffset? SentAt { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "queued";
}

/// <summary>Gap filled: Setup has "SMS/email templates".</summary>
public class NotificationTemplate : Entity
{
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(20)] public string Channel { get; set; } = "sms";
    [MaxLength(200)] public string? Subject { get; set; }
    [Required, MaxLength(4000)] public string Body { get; set; } = "";
    public bool Active { get; set; } = true;
}
