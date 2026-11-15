using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NavEstate.Api.Domain;

// ---------- schema core ----------

public class Landlord : Entity
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(20)] public string Type { get; set; } = "individual";
    [MaxLength(20)] public string? KraPin { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(80)] public string? Email { get; set; }
    [MaxLength(500)] public string? BankDetails { get; set; }
    [Range(0, 100)] public decimal CommissionPercent { get; set; }
    [MaxLength(20)] public string? BcVendorNo { get; set; }
    [JsonIgnore] public List<Property> Properties { get; set; } = [];
}

public class Property : Entity
{
    public Guid LandlordId { get; set; }
    [JsonIgnore] public Landlord? Landlord { get; set; }
    /// <summary>Unique; equals the BC PROPERTY dimension value.</summary>
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(20)] public string Type { get; set; } = "residential";
    [MaxLength(200)] public string? Location { get; set; }
    [MaxLength(50)] public string? County { get; set; }
    [MaxLength(50)] public string? BulkWaterMeterNo { get; set; }
    [Required, MaxLength(3)] public string DefaultCurrency { get; set; } = "KES";
    [JsonIgnore] public List<Block> Blocks { get; set; } = [];
    [JsonIgnore] public List<Unit> Units { get; set; } = [];
}

public class Block : Entity
{
    public Guid PropertyId { get; set; }
    [JsonIgnore] public Property? Property { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = "";
}

public class Unit : Entity
{
    public Guid PropertyId { get; set; }
    [JsonIgnore] public Property? Property { get; set; }
    public Guid? BlockId { get; set; }
    [JsonIgnore] public Block? Block { get; set; }
    /// <summary>Unique per property; equals the BC UNIT dimension value.</summary>
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(20)] public string UnitType { get; set; } = "1br";
    [MaxLength(10)] public string? Floor { get; set; }
    public decimal? AreaSqft { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "vacant";
    public decimal DefaultRent { get; set; }
    [Required, MaxLength(3)] public string DefaultCurrency { get; set; } = "KES";
    [JsonIgnore] public List<UnitCharge> Charges { get; set; } = [];
    [JsonIgnore] public List<Meter> Meters { get; set; } = [];
}

public class UnitCharge : Entity
{
    public Guid UnitId { get; set; }
    [JsonIgnore] public Unit? Unit { get; set; }
    [Required, MaxLength(20)] public string ChargeCode { get; set; } = "";
    public decimal Amount { get; set; }
    [Required, MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    [Required, MaxLength(20)] public string Frequency { get; set; } = "monthly";
}

public class Meter : Entity
{
    public Guid UnitId { get; set; }
    [JsonIgnore] public Unit? Unit { get; set; }
    [Required, MaxLength(20)] public string MeterType { get; set; } = "water";
    [Required, MaxLength(50)] public string SerialNo { get; set; } = "";
    public decimal? LastReading { get; set; }
    public DateOnly? LastReadingDate { get; set; }
    public bool Active { get; set; } = true;
}

public class Currency : IAudited
{
    [Required, MaxLength(3)] public string Code { get; set; } = "";
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    /// <summary>Code sent to BC: '' for KES (the BC local currency).</summary>
    [MaxLength(10)] public string BcCode { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ExchangeRate : Entity
{
    [Required, MaxLength(3)] public string CurrencyCode { get; set; } = "USD";
    public DateOnly RateDate { get; set; }
    /// <summary>KES per 1 unit of the currency, decimal(18,6).</summary>
    public decimal KesPerUnit { get; set; }
    [Required, MaxLength(20)] public string Source { get; set; } = "BC";
}

public class Setting : IAudited
{
    [Required, MaxLength(100)] public string Key { get; set; } = "";
    /// <summary>JSON value.</summary>
    [Required] public string Value { get; set; } = "null";
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Gap filled: the spec's Setup has "payment channels"; maps to BC NavEstate Payment Channel (52140615).</summary>
public class PaymentChannel : Entity
{
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    /// <summary>MPESA | BANK | CHEQUE | CASH, as sent in channelCode.</summary>
    [Required, MaxLength(20)] public string BcChannelCode { get; set; } = "MPESA";
    [MaxLength(50)] public string? AccountNo { get; set; }
    [Required, MaxLength(3)] public string CurrencyCode { get; set; } = "KES";
    public bool Active { get; set; } = true;
}
