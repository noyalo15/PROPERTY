using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NavEstate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stage2Leases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rent_schedule_lease_id",
                schema: "tenancy",
                table: "rent_schedule");

            migrationBuilder.DropIndex(
                name: "ix_rent_escalation_rules_lease_id",
                schema: "tenancy",
                table: "rent_escalation_rules");

            migrationBuilder.CreateIndex(
                name: "ix_rent_schedule_lease_id_effective_from",
                schema: "tenancy",
                table: "rent_schedule",
                columns: new[] { "lease_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rent_schedule_status",
                schema: "tenancy",
                table: "rent_schedule",
                column: "status",
                filter: "[status] IN ('scheduled', 'pending_approval', 'approved')");

            migrationBuilder.CreateIndex(
                name: "ix_rent_escalation_rules_lease_id",
                schema: "tenancy",
                table: "rent_escalation_rules",
                column: "lease_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rent_schedule_lease_id_effective_from",
                schema: "tenancy",
                table: "rent_schedule");

            migrationBuilder.DropIndex(
                name: "ix_rent_schedule_status",
                schema: "tenancy",
                table: "rent_schedule");

            migrationBuilder.DropIndex(
                name: "ix_rent_escalation_rules_lease_id",
                schema: "tenancy",
                table: "rent_escalation_rules");

            migrationBuilder.CreateIndex(
                name: "ix_rent_schedule_lease_id",
                schema: "tenancy",
                table: "rent_schedule",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_rent_escalation_rules_lease_id",
                schema: "tenancy",
                table: "rent_escalation_rules",
                column: "lease_id");
        }
    }
}
