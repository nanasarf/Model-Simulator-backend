using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClassroomAdmissionAndAiMvp1Persistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "runtime",
                table: "idempotency_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseBody",
                schema: "runtime",
                table: "idempotency_records",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "classroom_join_codes",
                schema: "education",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedCode = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_join_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_classroom_join_codes_classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalSchema: "education",
                        principalTable: "classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classroom_join_requests",
                schema: "education",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionReason = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classroom_join_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_classroom_join_requests_classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalSchema: "education",
                        principalTable: "classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_proposal_revisions",
                schema: "definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BlueprintJson = table.Column<string>(type: "jsonb", nullable: false),
                    RevisionSource = table.Column<string>(type: "text", nullable: false),
                    Instruction = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_proposal_revisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scenario_proposal_validations",
                schema: "definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ProposalVersion = table.Column<long>(type: "bigint", nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false),
                    BlockersJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ModelIdentifier = table.Column<string>(type: "text", nullable: false),
                    ModelVersion = table.Column<string>(type: "text", nullable: false),
                    ValidatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_proposal_validations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scenario_proposals",
                schema: "definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerInstructorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelIdentifier = table.Column<string>(type: "text", nullable: false),
                    ModelVersion = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CurrentBlueprintJson = table.Column<string>(type: "jsonb", nullable: false),
                    LinkedDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_proposals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_codes_ClassroomId",
                schema: "education",
                table: "classroom_join_codes",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_codes_NormalizedCode_IsActive",
                schema: "education",
                table: "classroom_join_codes",
                columns: new[] { "NormalizedCode", "IsActive" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_requests_ClassroomId_StudentUserId_Status",
                schema: "education",
                table: "classroom_join_requests",
                columns: new[] { "ClassroomId", "StudentUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_scenario_proposal_revisions_ProposalId_RevisionNumber",
                schema: "definitions",
                table: "scenario_proposal_revisions",
                columns: new[] { "ProposalId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scenario_proposal_validations_ProposalId_RevisionNumber",
                schema: "definitions",
                table: "scenario_proposal_validations",
                columns: new[] { "ProposalId", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_scenario_proposals_OwnerInstructorUserId_Status",
                schema: "definitions",
                table: "scenario_proposals",
                columns: new[] { "OwnerInstructorUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classroom_join_codes",
                schema: "education");

            migrationBuilder.DropTable(
                name: "classroom_join_requests",
                schema: "education");

            migrationBuilder.DropTable(
                name: "scenario_proposal_revisions",
                schema: "definitions");

            migrationBuilder.DropTable(
                name: "scenario_proposal_validations",
                schema: "definitions");

            migrationBuilder.DropTable(
                name: "scenario_proposals",
                schema: "definitions");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "runtime",
                table: "idempotency_records");

            migrationBuilder.DropColumn(
                name: "ResponseBody",
                schema: "runtime",
                table: "idempotency_records");
        }
    }
}
