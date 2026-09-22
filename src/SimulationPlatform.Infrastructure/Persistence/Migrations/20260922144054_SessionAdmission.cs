using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_join_codes",
                schema: "runtime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedCode = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_join_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_join_codes_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "runtime",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_join_requests",
                schema: "runtime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_join_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_join_requests_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "runtime",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_join_codes_NormalizedCode_IsActive",
                schema: "runtime",
                table: "session_join_codes",
                columns: new[] { "NormalizedCode", "IsActive" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_session_join_codes_SessionId",
                schema: "runtime",
                table: "session_join_codes",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_session_join_requests_SessionId_StudentUserId",
                schema: "runtime",
                table: "session_join_requests",
                columns: new[] { "SessionId", "StudentUserId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_session_join_requests_SessionId_StudentUserId_Status",
                schema: "runtime",
                table: "session_join_requests",
                columns: new[] { "SessionId", "StudentUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_join_codes",
                schema: "runtime");

            migrationBuilder.DropTable(
                name: "session_join_requests",
                schema: "runtime");
        }
    }
}
