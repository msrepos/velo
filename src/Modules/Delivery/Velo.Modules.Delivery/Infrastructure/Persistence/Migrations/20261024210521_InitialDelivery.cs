using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "delivery");

            migrationBuilder.CreateTable(
                name: "Drivers",
                schema: "delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    LocationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                schema: "delivery",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    DropoffJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AmountDue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProofProtected = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ProofExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProofAttempts = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_Shipments_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "delivery",
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_UserId",
                schema: "delivery",
                table: "Drivers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CustomerId_AssignedAt",
                schema: "delivery",
                table: "Shipments",
                columns: new[] { "CustomerId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_DriverId",
                schema: "delivery",
                table: "Shipments",
                column: "DriverId",
                unique: true,
                filter: "[Status] IN ('Assigned', 'OutForDelivery', 'Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "Drivers",
                schema: "delivery");
        }
    }
}
