namespace NavEstate.Api.Common;

/// <summary>All dates are Africa/Nairobi (UTC+03:00, no daylight saving).</summary>
public static class Nairobi
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// <summary>IANA id works on Linux and on Windows Server 2019+ (.NET uses ICU); falls back to the Windows id.</summary>
    public static TimeZoneInfo Zone =>
        TimeZoneInfo.TryFindSystemTimeZoneById("Africa/Nairobi", out var z) ? z
        : TimeZoneInfo.FindSystemTimeZoneById("E. Africa Standard Time");
    public static DateTimeOffset Now(this TimeProvider clock) => clock.GetUtcNow().ToOffset(Offset);
    public static DateOnly Today(this TimeProvider clock) => DateOnly.FromDateTime(clock.Now().DateTime);
}
