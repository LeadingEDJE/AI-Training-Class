using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadingEDJE.Leap.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "compass");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    effective_roles = table.Column<List<string>>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_type",
                schema: "compass",
                columns: table => new
                {
                    employee_type_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_type", x => x.employee_type_id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_frequency_type",
                schema: "compass",
                columns: table => new
                {
                    invoice_frequency_type_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_frequency_type", x => x.invoice_frequency_type_id);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    notification_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    period_week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    slack_user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    from_address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    from_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    edje_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    first_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    last_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    title = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    edje_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee",
                schema: "compass",
                columns: table => new
                {
                    employee_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    employee_type_id = table.Column<int>(type: "integer", nullable: false),
                    coach_employee_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    state_of_residence = table.Column<string>(type: "char(2)", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "America/New_York"),
                    timesheet_required = table.Column<bool>(type: "boolean", nullable: false),
                    can_submit_under_40 = table.Column<bool>(type: "boolean", nullable: false),
                    include_in_payroll = table.Column<bool>(type: "boolean", nullable: false),
                    is_delivery_team = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    legacy_tps_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee", x => x.employee_id);
                    table.CheckConstraint("ck_employee_state_of_residence_us", "state_of_residence IN ('AL', 'AK', 'AZ', 'AR', 'CA', 'CO', 'CT', 'DE', 'DC', 'FL', 'GA', 'HI', 'ID', 'IL', 'IN', 'IA', 'KS', 'KY', 'LA', 'ME', 'MD', 'MA', 'MI', 'MN', 'MS', 'MO', 'MT', 'NE', 'NV', 'NH', 'NJ', 'NM', 'NY', 'NC', 'ND', 'OH', 'OK', 'OR', 'PA', 'RI', 'SC', 'SD', 'TN', 'TX', 'UT', 'VT', 'VA', 'WA', 'WV', 'WI', 'WY')");
                    table.ForeignKey(
                        name: "fk_employee_employee_coach_employee_id",
                        column: x => x.coach_employee_id,
                        principalSchema: "compass",
                        principalTable: "employee",
                        principalColumn: "employee_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_employee_type_employee_type_id",
                        column: x => x.employee_type_id,
                        principalSchema: "compass",
                        principalTable: "employee_type",
                        principalColumn: "employee_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client",
                schema: "compass",
                columns: table => new
                {
                    client_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    msa_signed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    nda_signed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    invoice_frequency_type_id = table.Column<int>(type: "integer", nullable: true),
                    legacy_tps_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client", x => x.client_id);
                    table.ForeignKey(
                        name: "fk_client_invoice_frequency_type_invoice_frequency_type_id",
                        column: x => x.invoice_frequency_type_id,
                        principalSchema: "compass",
                        principalTable: "invoice_frequency_type",
                        principalColumn: "invoice_frequency_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billable_time_category",
                schema: "compass",
                columns: table => new
                {
                    billable_time_category_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    category_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    legacy_tps_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billable_time_category", x => x.billable_time_category_id);
                    table.ForeignKey(
                        name: "fk_billable_time_category_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "compass",
                        principalTable: "client",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_assignment",
                schema: "compass",
                columns: table => new
                {
                    client_assignment_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    client_id = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    invoice_frequency_type_id = table.Column<int>(type: "integer", nullable: true),
                    legacy_tps_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_assignment", x => x.client_assignment_id);
                    table.CheckConstraint("ck_client_assignment_end_on_or_after_start", "end_date IS NULL OR end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_client_assignment_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "compass",
                        principalTable: "client",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_assignment_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "compass",
                        principalTable: "employee",
                        principalColumn: "employee_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_assignment_invoice_frequency_type_invoice_frequency_",
                        column: x => x.invoice_frequency_type_id,
                        principalSchema: "compass",
                        principalTable: "invoice_frequency_type",
                        principalColumn: "invoice_frequency_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sow",
                schema: "compass",
                columns: table => new
                {
                    sow_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    client_assignment_id = table.Column<int>(type: "integer", nullable: false),
                    sow_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rate_increase = table.Column<bool>(type: "boolean", nullable: false),
                    has_passed_application_validation = table.Column<bool>(type: "boolean", nullable: false),
                    sow_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sow_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    legacy_tps_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sow", x => x.sow_id);
                    table.CheckConstraint("ck_sow_end_on_or_after_start", "sow_type = 'LegacyMigrated' OR sow_end_date >= sow_start_date");
                    table.CheckConstraint("ck_sow_rate_increase_requires_extension", "NOT rate_increase OR sow_type = 'SowExtension'");
                    table.CheckConstraint("ck_sow_type_is_known", "sow_type IN ('InitialContract', 'SowExtension', 'LegacyMigrated')");
                    table.ForeignKey(
                        name: "fk_sow_client_assignment_client_assignment_id",
                        column: x => x.client_assignment_id,
                        principalSchema: "compass",
                        principalTable: "client_assignment",
                        principalColumn: "client_assignment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ux_billable_time_category_client_id_category_name",
                schema: "compass",
                table: "billable_time_category",
                columns: new[] { "client_id", "category_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_billable_time_category_legacy_tps_id",
                schema: "compass",
                table: "billable_time_category",
                column: "legacy_tps_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_invoice_frequency_type_id",
                schema: "compass",
                table: "client",
                column: "invoice_frequency_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_client_client_name",
                schema: "compass",
                table: "client",
                column: "client_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_client_legacy_tps_id",
                schema: "compass",
                table: "client",
                column: "legacy_tps_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_assignment_client_id",
                schema: "compass",
                table: "client_assignment",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_assignment_employee_id",
                schema: "compass",
                table: "client_assignment",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_assignment_invoice_frequency_type_id",
                schema: "compass",
                table: "client_assignment",
                column: "invoice_frequency_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_client_assignment_legacy_tps_id",
                schema: "compass",
                table: "client_assignment",
                column: "legacy_tps_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_coach_employee_id",
                schema: "compass",
                table: "employee",
                column: "coach_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_employee_type_id",
                schema: "compass",
                table: "employee",
                column: "employee_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_last_name",
                schema: "compass",
                table: "employee",
                column: "last_name");

            migrationBuilder.CreateIndex(
                name: "ux_employee_legacy_tps_id",
                schema: "compass",
                table: "employee",
                column: "legacy_tps_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_employee_type_type_name",
                schema: "compass",
                table: "employee_type",
                column: "type_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoice_frequency_type_type_name",
                schema: "compass",
                table: "invoice_frequency_type",
                column: "type_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_idempotency",
                table: "notification_logs",
                columns: new[] { "employee_id", "notification_type", "period_week_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_email",
                table: "people",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_sow_client_assignment_id_sow_end_date",
                schema: "compass",
                table: "sow",
                columns: new[] { "client_assignment_id", "sow_end_date" });

            migrationBuilder.CreateIndex(
                name: "ux_sow_legacy_tps_id",
                schema: "compass",
                table: "sow",
                column: "legacy_tps_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_edje_id_role",
                table: "user_roles",
                columns: new[] { "edje_id", "role" },
                unique: true);

            // Case-insensitive email uniqueness on people. EF's HasIndex takes properties, not
            // expressions, so a functional index cannot be declared in PersonConfiguration; ported
            // by hand from the pre-trim InitialCreate/AddAbsorbedTpsEntities migration.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_people_email_lower ON people (LOWER(email)) WHERE email IS NOT NULL;");

            // compass.employee's case-insensitive email uniqueness (BR-9). Same reason as above: EF
            // cannot express a functional index. Ported from the pre-trim AddCompassEmployeeCaseInsensitiveEmailIndex
            // migration; EmployeeConfiguration deliberately declares no Email index (see its remarks).
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_employee_email_ci
                    ON compass.employee (lower(btrim(email)));
                """);

            // SOW non-overlap, per assignment (ex_sow_no_overlap_per_assignment). HAND-WRITTEN because
            // EF Core has no fluent API for an exclusion constraint; ported from the pre-trim
            // ReplaceSowIsExtensionWithSowType migration (the final form — AddCompassErdSchema's
            // original unconditional version was superseded by that one). Partial (WHERE sow_type <>
            // 'LegacyMigrated') so a TPS-migrated row whose end precedes its start can still load
            // (FR-053); btree_gist is required because the constraint mixes an equality test on a
            // plain integer with an overlap test on a range.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                """
                ALTER TABLE "compass"."sow"
                      ADD CONSTRAINT "ex_sow_no_overlap_per_assignment"
                      EXCLUDE USING gist (
                          client_assignment_id WITH =,
                          daterange(sow_start_date, sow_end_date, '[]') WITH &&
                      )
                      WHERE (sow_type <> 'LegacyMigrated');
                """);

            // Append-only enforcement for audit_logs (ADR-007 / issue #317). Ported from the pre-trim
            // AddAuditLogAppendOnlyTriggers + AllowAuditRetentionPurge migrations: DELETE is refused
            // unless the deleting transaction opted in via SET LOCAL leap.audit_retention_purge = 'on'
            // (AuditRetentionService's purge does this); UPDATE is refused unconditionally.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.audit_logs_prevent_change() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF coalesce(current_setting('leap.audit_retention_purge', true), '') = 'on' THEN
                            RETURN OLD;
                        END IF;

                        RAISE EXCEPTION 'Audit log entries cannot be deleted'
                            USING ERRCODE = 'raise_exception';
                    ELSE
                        RAISE EXCEPTION 'Audit log entries cannot be modified'
                            USING ERRCODE = 'raise_exception';
                    END IF;
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_logs_no_update BEFORE UPDATE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit_logs_prevent_change();
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER audit_logs_no_delete BEFORE DELETE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit_logs_prevent_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_logs_no_update ON audit_logs;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_logs_no_delete ON audit_logs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_logs_prevent_change();");

            // btree_gist is deliberately NOT dropped: it is database-wide, and dropping it here could
            // break an unrelated object that has since come to depend on it.
            migrationBuilder.Sql(
                """
                ALTER TABLE "compass"."sow" DROP CONSTRAINT IF EXISTS "ex_sow_no_overlap_per_assignment";
                """);

            migrationBuilder.Sql("DROP INDEX IF EXISTS compass.ux_employee_email_ci;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_people_email_lower;");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "billable_time_category",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "notification_logs");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "sow",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "client_assignment",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "client",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "employee",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "invoice_frequency_type",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "employee_type",
                schema: "compass");
        }
    }
}
