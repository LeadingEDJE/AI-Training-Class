using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeadingEDJE.Leap.Api.Platform.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "skill",
                schema: "compass",
                columns: table => new
                {
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skill", x => x.skill_id);
                });

            migrationBuilder.CreateTable(
                name: "employee_skill",
                schema: "compass",
                columns: table => new
                {
                    employee_skill_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    employee_id = table.Column<int>(type: "integer", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_skill", x => x.employee_skill_id);
                    table.ForeignKey(
                        name: "fk_employee_skill_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "compass",
                        principalTable: "employee",
                        principalColumn: "employee_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_skill_skill_skill_id",
                        column: x => x.skill_id,
                        principalSchema: "compass",
                        principalTable: "skill",
                        principalColumn: "skill_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_skill_employee_id",
                schema: "compass",
                table: "employee_skill",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_skill_skill_id",
                schema: "compass",
                table: "employee_skill",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ux_employee_skill_employee_id_skill_id",
                schema: "compass",
                table: "employee_skill",
                columns: new[] { "employee_id", "skill_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_skill_name",
                schema: "compass",
                table: "skill",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_skill",
                schema: "compass");

            migrationBuilder.DropTable(
                name: "skill",
                schema: "compass");
        }
    }
}
