using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NavEstate.Api.Domain;

// ---------- schema billing (tables created now; logic arrives in stage 3) ----------

public class BillingRun : Entity
{
    [MaxLength(30)] public string RunNo { get; set; } = "";
    [MaxLength(7)] public string Period { get; set; } = "";
    public Guid? PropertyId { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "draft";
    [MaxLength(450)] public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

public class Bill : Entity
{
    [MaxLength(30)] public string BillNo { get; set; } = "";
    public Guid LeaseId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UnitId { get; set; }
    public Guid? BillingRunId { get; set; }
    [MaxLength(20)] public string BillType { get; set; } = "rent";
    [MaxLength(7)] public string Period { get; set; } = "";
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal WhtAmount { get; set; }
    public decimal AmountPaid { get; set; }
    /// <summary>Gap filled: the spec's balance formula uses it but does not list it.</summary>
    public decimal AmountCredited { get; set; }
    /// <summary>Computed, persisted: total - paid - credited.</summary>
    public decimal Balance { get; private set; }
    [MaxLength(20)] public string Status { get; set; } = "draft";
    [MaxLength(20)] public string? BcDocumentNo { get; set; }
    [MaxLength(20)] public string? BcOpenDocumentNo { get; set; }
    [MaxLength(20)] public string? BcSyncStatus { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<BillLine> Lines { get; set; } = [];
}

public class BillLine : Entity
{
    public Guid BillId { get; set; }
    [JsonIgnore] public Bill? Bill { get; set; }
    [MaxLength(20)] public string ChargeCode { get; set; } = "";
    [MaxLength(50)] public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
}

public class CreditNote : Entity
{
    [MaxLength(30)] public string CreditNo { get; set; } = "";
    public Guid BillId { get; set; }
    public Guid TenantId { get; set; }
    [MaxLength(200)] public string Reason { get; set; } = "";
    public decimal Amount { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    [MaxLength(20)] public string Status { get; set; } = "draft";
    [MaxLength(450)] public string? ApprovedBy { get; set; }
    [MaxLength(20)] public string? BcDocumentNo { get; set; }
}

public class WaterReading : Entity
{
    public Guid MeterId { get; set; }
    public Guid UnitId { get; set; }
    [MaxLength(7)] public string Period { get; set; } = "";
    public decimal PreviousReading { get; set; }
    public decimal CurrentReading { get; set; }
    /// <summary>Computed, persisted: current - previous.</summary>
    public decimal Consumption { get; private set; }
    public decimal RatePerM3 { get; set; }
    public decimal StandingCharge { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(400)] public string? PhotoPath { get; set; }
    [MaxLength(450)] public string? CapturedBy { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    [MaxLength(20)] public string Flag { get; set; } = "none";
    [MaxLength(20)] public string Status { get; set; } = "captured";
}

public class WaterTariff : Entity
{
    public Guid? PropertyId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Range(0, 999999999)] public decimal StandingCharge { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public List<WaterTariffBand> Bands { get; set; } = [];
}

/// <summary>Replaces water_tariffs.bands jsonb.</summary>
public class WaterTariffBand : Entity
{
    public Guid TariffId { get; set; }
    [JsonIgnore] public WaterTariff? Tariff { get; set; }
    public decimal FromM3 { get; set; }
    public decimal? ToM3 { get; set; }
    public decimal Rate { get; set; }
}
