using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimulationPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimId",
                schema: "integration",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedUntil",
                schema: "integration",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimId",
                schema: "integration",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "ClaimedUntil",
                schema: "integration",
                table: "outbox_messages");
        }
    }
}
