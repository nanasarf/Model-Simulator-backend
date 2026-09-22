using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreventDuplicatePendingClassroomJoinRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_classroom_join_requests_ClassroomId_StudentUserId",
                schema: "education",
                table: "classroom_join_requests",
                columns: new[] { "ClassroomId", "StudentUserId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_classroom_join_requests_ClassroomId_StudentUserId",
                schema: "education",
                table: "classroom_join_requests");
        }
    }
}
