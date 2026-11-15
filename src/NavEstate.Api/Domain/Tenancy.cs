using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NavEstate.Api.Domain;

// ---------- schema tenancy ----------

public class Tenant : Entity
{
    /// <summary>Unique, e.g. T00104; also the BC customer no.</summary>
    [MaxLength(20)] public string TenantNo { get; set; } = "";
    [MaxLength(20)] public string Type { get; set; } = "individual";
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(30)] public string? IdOrPassportNo { get; set; }
    [MaxLength(20)] public string? KraPin { get; set; }
    [MaxLength(30)] public string Phone { get; set; } = "";
    [MaxLength(30)] public string? AltPhone { get; set; }
    [MaxLength(80)] public string? Email { get; set; }
    [MaxLength(100)] public string? NextOfKinName { get; set; }
    [MaxLength(30)] public string? NextOfKinPhone { get; set; }
    [MaxLength(30)] public string? CompanyRegNo { get; set; }
    [MaxLength(100)] public string? ContactPerson { get; set; }
    public bool Blacklisted { get; set; }
    [MaxLength(500)] public string? BlacklistReason { get; set; }
    [MaxLength(450)] public string? PortalUserId { get; set; }
    public DateTimeOffset? BcSyncedAt { get; set; }
    [JsonIgnore] public List<TenantDocument> Documents { get; set; } = [];
    [JsonIgnore] public List<Lease> Leases { get; set; } = [];
}

public class TenantDocument : Entity
{
    public Guid TenantId { get; set; }
    [JsonIgnore] public Tenant? Tenant { get; set; }
    [MaxLength(20)] public string DocType { get; set; } = "other";
    [MaxLength(400)] public string FilePath { get; set; } = "";
    [MaxLength(200)] public string FileName { get; set; } = "";
    public DateOnly? ExpiryDate { get; set; }
}

public class ChargeCode : IAudited
{
    /// <summary>PK, e.g. RENT; must match BC "NavEstate Charge Mapping" (52140614).</summary>
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(50)] public string Description { get; set; } = "";
    public bool IsRent { get; set; }
    public int AllocationPriority { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class Lease : Entity
{
    [MaxLength(20)] public string LeaseNo { get; set; } = "";
    public Guid TenantId { get; set; }
    [JsonIgnore] public Tenant? Tenant { get; set; }
    public Guid UnitId { get; set; }
    [JsonIgnore] public Unit? Unit { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "draft";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    public int BillingDay { get; set; } = 1;
    [MaxLength(20)] public string Frequency { get; set; } = "monthly";
    public decimal DepositAmount { get; set; }
    public decimal WaterDepositAmount { get; set; }
    public int GraceDays { get; set; } = 5;
    public Guid? PenaltyRuleId { get; set; }
    [JsonIgnore] public PenaltyRule? PenaltyRule { get; set; }
    public bool VatApplicable { get; set; }
    /// <summary>Tenant withholds 10% WHT on rent.</summary>
    public bool WhtAgent { get; set; }
    [MaxLength(200)] public string? EndReason { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? TransferredToLeaseId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<LeaseCharge> Charges { get; set; } = [];
}

public class LeaseCharge : Entity
{
    public Guid LeaseId { get; set; }
    [JsonIgnore] public Lease? Lease { get; set; }
    [MaxLength(20)] public string ChargeCode { get; set; } = "";
    [MaxLength(50)] public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    [MaxLength(20)] public string Frequency { get; set; } = "monthly";
    public bool Active { get; set; } = true;
}

public class RentEscalationRule : Entity
{
    public Guid LeaseId { get; set; }
    [MaxLength(20)] public string Type { get; set; } = "fixed_percent";
    public decimal Value { get; set; }
    public int EveryMonths { get; set; } = 12;
    public bool Compounding { get; set; } = true;
    public int NoticeDays { get; set; } = 90;
    public bool RequiresApproval { get; set; } = true;
}

public class RentSchedule : Entity
{
    public Guid LeaseId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal? MonthlyRent { get; set; }
    [MaxLength(50)] public string? ChangeLabel { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "scheduled";
    public DateTimeOffset? NoticeSentAt { get; set; }
    [MaxLength(450)] public string? ApprovedBy { get; set; }
}

public class PenaltyRule : Entity
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(30)] public string Basis { get; set; } = "fixed";
    [Range(0, 999999999)] public decimal Value { get; set; }
    [Required, MaxLength(20)] public string Frequency { get; set; } = "once";
    [Required, MaxLength(20)] public string CapType { get; set; } = "none";
    public decimal? CapValue { get; set; }
    [Range(0, 365)] public int GraceDays { get; set; } = 5;
}

public class Deposit : Entity
{
    public Guid TenantId { get; set; }
    public Guid LeaseId { get; set; }
    [MaxLength(20)] public string DepositType { get; set; } = "rent";
    [MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    public decimal AmountHeld { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "held";
}

public class DepositMovementRow : Entity
{
    public Guid DepositId { get; set; }
    [MaxLength(20)] public string Movement { get; set; } = "receive";
    public decimal Amount { get; set; }
    [MaxLength(50)] public string? Reference { get; set; }
    [MaxLength(450)] public string? ApprovedBy { get; set; }
    [MaxLength(20)] public string? BcDocumentNo { get; set; }
}

public class Transfer : Entity
{
    [MaxLength(20)] public string TransferNo { get; set; } = "";
    public Guid TenantId { get; set; }
    public Guid FromLeaseId { get; set; }
    public Guid ToUnitId { get; set; }
    public Guid? ToLeaseId { get; set; }
    public DateOnly TransferDate { get; set; }
    [MaxLength(20)] public string BalanceOption { get; set; } = "carry_forward";
    [MaxLength(20)] public string DepositOption { get; set; } = "move";
    [MaxLength(20)] public string Status { get; set; } = "draft";
    public Guid? AdviceSlipId { get; set; }
    [MaxLength(450)] public string? ApprovedBy { get; set; }
}

public class AdviceSlip : Entity
{
    [MaxLength(20)] public string SlipNo { get; set; } = "";
    public Guid TenantId { get; set; }
    public Guid LeaseId { get; set; }
    [MaxLength(20)] public string SlipType { get; set; } = "transfer";
    public DateOnly SlipDate { get; set; }
    public string Snapshot { get; set; } = "{}";
    [MaxLength(400)] public string? PdfPath { get; set; }
}
