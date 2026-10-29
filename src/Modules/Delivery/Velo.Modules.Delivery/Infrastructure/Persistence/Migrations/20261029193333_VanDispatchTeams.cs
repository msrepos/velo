using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VanDispatchTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrewMembers",
                schema: "delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrewMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VanDispatches",
                schema: "delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VanCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VanName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrewMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VanDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VanDispatches_CrewMembers_CrewMemberId",
                        column: x => x.CrewMemberId,
                        principalSchema: "delivery",
                        principalTable: "CrewMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VanDispatches_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalSchema: "delivery",
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrewMembers_UserId",
                schema: "delivery",
                table: "CrewMembers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VanDispatches_CrewMemberId",
                schema: "delivery",
                table: "VanDispatches",
                column: "CrewMemberId",
                unique: true,
                filter: "[EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VanDispatches_DriverId",
                schema: "delivery",
                table: "VanDispatches",
                column: "DriverId",
                unique: true,
                filter: "[EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VanDispatches_WarehouseId",
                schema: "delivery",
                table: "VanDispatches",
                column: "WarehouseId",
                unique: true,
                filter: "[EndedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VanDispatches",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "CrewMembers",
                schema: "delivery");
        }
    }
}
