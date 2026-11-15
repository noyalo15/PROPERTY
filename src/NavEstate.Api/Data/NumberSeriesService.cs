using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace NavEstate.Api.Data;

/// <summary>
/// Gap-free document numbers (spec rule 10). The counter row is taken with UPDLOCK/HOLDLOCK
/// inside the caller's transaction, so two users never get the same number and a rolled-back
/// save gives the number back.
/// </summary>
public class NumberSeriesService(NavEstateDbContext db)
{
    public const string TenantCode = "TENANT";
    public const string LeaseCode = "LEASE";

    /// <summary>T0nnnn, e.g. T00104 (also the BC customer no.).</summary>
    public async Task<string> NextTenantNoAsync(CancellationToken ct = default) =>
        $"T{await NextAsync(TenantCode, "", ct):D5}";

    /// <summary>LSE-nnnn, e.g. LSE-0287.</summary>
    public async Task<string> NextLeaseNoAsync(CancellationToken ct = default) =>
        $"LSE-{await NextAsync(LeaseCode, "", ct):D4}";

    /// <summary>INV-yyyy-mm-nnnnnn and the other monthly series (WTR, PEN, CRN, RCT, DEP).</summary>
    public async Task<string> NextMonthlyAsync(string prefix, DateOnly date, CancellationToken ct = default) =>
        $"{prefix}-{date:yyyy}-{date:MM}-{await NextAsync(prefix, date.ToString("yyyy-MM"), ct):D6}";

    public async Task<long> NextAsync(string code, string period, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app.number_series WITH (UPDLOCK, HOLDLOCK) SET last_no = last_no + 1
            OUTPUT inserted.last_no WHERE code = @code AND period = @period;
            """;
        const string insert = """
            INSERT INTO app.number_series (code, period, last_no)
            SELECT @code, @period, 1
            WHERE NOT EXISTS (SELECT 1 FROM app.number_series WITH (UPDLOCK, HOLDLOCK) WHERE code = @code AND period = @period);
            SELECT CASE WHEN @@ROWCOUNT = 1 THEN CAST(1 AS bigint) ELSE CAST(-1 AS bigint) END;
            """;

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        var tx = db.Database.CurrentTransaction?.GetDbTransaction();

        await using (var cmd = Cmd(conn, tx, sql, code, period))
        {
            var r = await cmd.ExecuteScalarAsync(ct);
            if (r is long n) return n;
        }
        await using (var cmd = Cmd(conn, tx, insert, code, period))
        {
            var r = await cmd.ExecuteScalarAsync(ct);
            if (Convert.ToInt64(r) == 1) return 1;
        }
        // Another transaction inserted the row between our two statements: increment it.
        await using (var cmd = Cmd(conn, tx, sql, code, period))
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    static System.Data.Common.DbCommand Cmd(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? tx, string sql, string code, string period)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@code", SqlDbType.NVarChar, 20) { Value = code });
        cmd.Parameters.Add(new SqlParameter("@period", SqlDbType.NVarChar, 10) { Value = period });
        return cmd;
    }
}
