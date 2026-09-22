using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeclarativeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rules",
                schema: "definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Effect = table.Column<string>(type: "text", nullable: false),
                    ConditionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rules_scenario_versions_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalSchema: "definitions",
                        principalTable: "scenario_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rules_ScenarioVersionId_Priority",
                schema: "definitions",
                table: "rules",
                columns: new[] { "ScenarioVersionId", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rules",
                schema: "definitions");
        }
    }
}
