using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Milestone5ScenarioAuthoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scenario_drafts",
                schema: "definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SimulationDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PublishedScenarioVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scenario_drafts_simulation_definitions_SimulationDefinition~",
                        column: x => x.SimulationDefinitionId,
                        principalSchema: "definitions",
                        principalTable: "simulation_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scenario_drafts_SimulationDefinitionId_Name",
                schema: "definitions",
                table: "scenario_drafts",
                columns: new[] { "SimulationDefinitionId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scenario_drafts",
                schema: "definitions");
        }
    }
}
