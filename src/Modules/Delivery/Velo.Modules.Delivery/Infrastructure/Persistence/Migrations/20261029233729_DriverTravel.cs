using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverTravel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArrivedAt",
                schema: "delivery",
                table: "Shipments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TravelStartedAt",
                schema: "delivery",
                table: "Shipments",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivedAt",
                schema: "delivery",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "TravelStartedAt",
                schema: "delivery",
                table: "Shipments");
        }
    }
}
