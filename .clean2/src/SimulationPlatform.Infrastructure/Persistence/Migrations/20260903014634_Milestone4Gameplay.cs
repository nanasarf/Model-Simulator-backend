using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Milestone4Gameplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmittedPhase",
                schema: "runtime",
                table: "action_submissions",
                type: "text",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateTable(
                name: "round_readiness",
                schema: "runtime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<string>(type: "text", nullable: false),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_round_readiness", x => x.Id);
                    table.ForeignKey(
                        name: "FK_round_readiness_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "runtime",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_round_readiness_SessionId_RoundNumber_Phase_UserId",
                schema: "runtime",
                table: "round_readiness",
                columns: new[] { "SessionId", "RoundNumber", "Phase", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "round_readiness",
                schema: "runtime");

            migrationBuilder.DropColumn(
                name: "SubmittedPhase",
                schema: "runtime",
                table: "action_submissions");
        }
    }
}
