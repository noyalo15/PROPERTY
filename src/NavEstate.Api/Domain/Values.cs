namespace NavEstate.Api.Domain;

/// <summary>
/// Allowed values for the enum-like text columns. Values are exactly as the spec lists them and
/// are enforced in SQL Server with CHECK constraints (see NavEstateDbContext).
/// </summary>
public static class V
{
    public static readonly string[] LandlordType = ["individual", "company"];
    public static readonly string[] PropertyType = ["residential", "commercial", "mixed"];
    public static readonly string[] UnitType = ["bedsitter", "1br", "2br", "3br", "4br", "maisonette", "shop", "office", "warehouse", "parking", "kiosk"];
    public static readonly string[] UnitStatus = ["vacant", "reserved", "occupied", "maintenance", "off_market"];
    public static readonly string[] Currency = ["KES", "USD"];
    public static readonly string[] Frequency = ["monthly", "quarterly", "semi_annual", "annual"];
    public static readonly string[] ChargeFrequency = ["monthly", "quarterly", "semi_annual", "annual", "once"];
    public static readonly string[] MeterType = ["water", "electricity"];
    public static readonly string[] TenantType = ["individual", "company"];
    public static readonly string[] DocType = ["id", "kra_pin", "lease", "guarantor", "other"];
    public static readonly string[] LeaseStatus = ["draft", "active", "ended", "transferred", "terminated"];
    public static readonly string[] EscalationType = ["fixed_percent", "fixed_amount", "stepped", "market_review"];
    public static readonly string[] ScheduleStatus = ["completed", "current", "pending_approval", "approved", "scheduled"];
    public static readonly string[] PenaltyBasis = ["fixed", "percent_overdue_rent", "percent_overdue_balance"];
    public static readonly string[] PenaltyFrequency = ["once", "monthly"];
    public static readonly string[] PenaltyCap = ["none", "amount", "percent_of_rent"];
    public static readonly string[] BillingRunStatus = ["draft", "approved", "posted", "partially_posted"];
    public static readonly string[] BillType = ["rent", "water", "penalty", "repair", "deposit", "other"];
    public static readonly string[] BillStatus = ["draft", "approved", "posted", "partially_paid", "paid", "cancelled"];
    public static readonly string[] CreditStatus = ["draft", "approved", "posted"];
    public static readonly string[] WaterFlag = ["none", "high", "zero", "missing", "lower"];
    public static readonly string[] WaterStatus = ["captured", "verified", "billed"];
    public static readonly string[] Channel = ["mpesa", "bank", "cheque", "cash"];
    public static readonly string[] ChequeStatus = ["pending", "cleared", "bounced"];
    public static readonly string[] PaymentStatus = ["unallocated", "allocated", "posted", "reversed"];
    public static readonly string[] MpesaMatch = ["matched", "fuzzy", "unmatched", "ignored"];
    public static readonly string[] BankMatch = ["matched", "fuzzy", "unmatched", "ignored"];
    public static readonly string[] DepositType = ["rent", "water"];
    public static readonly string[] DepositStatus = ["held", "applied", "refunded", "forfeited"];
    public static readonly string[] DepositMovement = ["receive", "refund", "apply_to_bills", "forfeit", "transfer"];
    public static readonly string[] MaintCategory = ["plumbing", "electrical", "structural", "painting", "appliance", "other"];
    public static readonly string[] Priority = ["low", "medium", "high", "emergency"];
    public static readonly string[] MaintStatus = ["logged", "assigned", "in_progress", "completed", "closed"];
    public static readonly string[] ChargeTo = ["tenant", "landlord", "none"];
    public static readonly string[] BalanceOption = ["carry_forward", "settle_first"];
    public static readonly string[] DepositOption = ["move", "move_and_top_up", "refund_and_new"];
    public static readonly string[] TransferStatus = ["draft", "awaiting_approval", "approved", "completed", "cancelled"];
    public static readonly string[] SlipType = ["transfer", "move_out"];
    public static readonly string[] NoticeChannel = ["sms", "email", "portal"];
    public static readonly string[] NoticeStatus = ["queued", "sent", "failed"];
    public static readonly string[] SyncEntity = ["tenant", "bill", "credit_note", "payment", "reverse_payment", "deposit", "transfer"];
    public static readonly string[] SyncStatus = ["pending", "processing", "success", "failed"];
    public static readonly string[] BcChannel = ["MPESA", "BANK", "CHEQUE", "CASH"];
}
