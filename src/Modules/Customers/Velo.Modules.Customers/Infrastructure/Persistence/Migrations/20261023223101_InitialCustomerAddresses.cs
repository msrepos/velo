using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCustomerAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customers");

            migrationBuilder.CreateTable(
                name: "Addresses",
                schema: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PhoneNumber = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Street = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Building = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Floor = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Apartment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addresses", x => x.Id);
                    table.CheckConstraint("CK_Addresses_Coordinates", "[Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_CustomerId",
                schema: "customers",
                table: "Addresses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_CustomerId_IsDefault",
                schema: "customers",
                table: "Addresses",
                columns: new[] { "CustomerId", "IsDefault" },
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Addresses",
                schema: "customers");
        }
    }
}
