using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NavEstate.Api.Data.Migrations
{
    /// <summary>
    /// SQL Server row-level security on core.properties and core.units, keyed on
    /// app.user_property_assignments. The API sets SESSION_CONTEXT user_id and rls_bypass on every
    /// connection (SessionContextInterceptor); super admins and background jobs bypass.
    /// This backs up the role checks in the API, so one bug cannot leak another landlord's data.
    /// </summary>
    public partial class RowLevelSecurity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA rls;");
            migrationBuilder.Sql("""
                CREATE FUNCTION rls.fn_property_access(@property_id uniqueidentifier, @landlord_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS RETURN
                SELECT 1 AS allowed
                WHERE CAST(SESSION_CONTEXT(N'rls_bypass') AS bit) = 1
                   OR EXISTS (SELECT 1 FROM app.user_property_assignments a
                              WHERE a.user_id = CAST(SESSION_CONTEXT(N'user_id') AS nvarchar(450))
                                AND (a.property_id = @property_id OR a.landlord_id = @landlord_id));
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION rls.fn_unit_access(@property_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS RETURN
                SELECT 1 AS allowed
                WHERE CAST(SESSION_CONTEXT(N'rls_bypass') AS bit) = 1
                   OR EXISTS (SELECT 1 FROM core.properties p
                              JOIN app.user_property_assignments a
                                ON a.user_id = CAST(SESSION_CONTEXT(N'user_id') AS nvarchar(450))
                               AND (a.property_id = p.id OR a.landlord_id = p.landlord_id)
                              WHERE p.id = @property_id);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY rls.property_policy
                    ADD FILTER PREDICATE rls.fn_property_access(id, landlord_id) ON core.properties,
                    ADD BLOCK PREDICATE rls.fn_property_access(id, landlord_id) ON core.properties AFTER UPDATE,
                    ADD FILTER PREDICATE rls.fn_unit_access(property_id) ON core.units,
                    ADD BLOCK PREDICATE rls.fn_unit_access(property_id) ON core.units AFTER INSERT,
                    ADD BLOCK PREDICATE rls.fn_unit_access(property_id) ON core.units AFTER UPDATE
                WITH (STATE = ON, SCHEMABINDING = ON);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS rls.property_policy;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rls.fn_unit_access;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rls.fn_property_access;");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS rls;");
        }
    }
}
