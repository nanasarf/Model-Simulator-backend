using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Milestone2ClassroomWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "runtime",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "runtime",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                schema: "runtime",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "runtime",
                table: "sessions",
                type: "text",
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<byte[]>(
                name: "ManifestHash",
                schema: "definitions",
                table: "scenario_versions",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "ManifestJson",
                schema: "definitions",
                table: "scenario_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "definitions",
                table: "scenario_versions",
                type: "text",
                nullable: false,
                defaultValue: "Legacy scenario");

            migrationBuilder.AddColumn<string>(
                name: "RoleCode",
                schema: "runtime",
                table: "role_assignments",
                type: "text",
                nullable: false,
                defaultValue: "LEGACY");

            migrationBuilder.AlterColumn<long>(
                name: "Sequence",
                schema: "runtime",
                table: "events",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.Sql("""
                SELECT setval(
                    pg_get_serial_sequence('runtime.events', 'Sequence'),
                    COALESCE((SELECT MAX("Sequence") + 1 FROM runtime.events), 1),
                    false);
                """);

            migrationBuilder.CreateTable(
                name: "session_manifests",
                schema: "runtime",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ManifestHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    FrozenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_manifests", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_session_manifests_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "runtime",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "runtime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teams_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "runtime",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "participants",
                schema: "runtime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_participants_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "runtime",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_participants_teams_TeamId",
                        column: x => x.TeamId,
                        principalSchema: "runtime",
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_SessionId_TeamId_UserId_RoleCode",
                schema: "runtime",
                table: "role_assignments",
                columns: new[] { "SessionId", "TeamId", "UserId", "RoleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_TeamId",
                schema: "runtime",
                table: "role_assignments",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_participants_SessionId_UserId",
                schema: "runtime",
                table: "participants",
                columns: new[] { "SessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_participants_TeamId",
                schema: "runtime",
                table: "participants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_teams_SessionId_Name",
                schema: "runtime",
                table: "teams",
                columns: new[] { "SessionId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_role_assignments_sessions_SessionId",
                schema: "runtime",
                table: "role_assignments",
                column: "SessionId",
                principalSchema: "runtime",
                principalTable: "sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_role_assignments_teams_TeamId",
                schema: "runtime",
                table: "role_assignments",
                column: "TeamId",
                principalSchema: "runtime",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_role_assignments_sessions_SessionId",
                schema: "runtime",
                table: "role_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_role_assignments_teams_TeamId",
                schema: "runtime",
                table: "role_assignments");

            migrationBuilder.DropTable(
                name: "participants",
                schema: "runtime");

            migrationBuilder.DropTable(
                name: "session_manifests",
                schema: "runtime");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "runtime");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_SessionId_TeamId_UserId_RoleCode",
                schema: "runtime",
                table: "role_assignments");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_TeamId",
                schema: "runtime",
                table: "role_assignments");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "runtime",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "runtime",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "runtime",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "runtime",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "ManifestHash",
                schema: "definitions",
                table: "scenario_versions");

            migrationBuilder.DropColumn(
                name: "ManifestJson",
                schema: "definitions",
                table: "scenario_versions");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "definitions",
                table: "scenario_versions");

            migrationBuilder.DropColumn(
                name: "RoleCode",
                schema: "runtime",
                table: "role_assignments");

            migrationBuilder.AlterColumn<long>(
                name: "Sequence",
                schema: "runtime",
                table: "events",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
