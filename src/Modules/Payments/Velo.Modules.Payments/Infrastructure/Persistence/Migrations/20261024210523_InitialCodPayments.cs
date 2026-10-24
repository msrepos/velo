using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCodPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "CashSettlements",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSettlements", x => x.Id);
                    table.CheckConstraint("CK_CashSettlements_Amount", "[Amount] > 0");
                });

            migrationBuilder.CreateTable(
                name: "CodPayments",
                schema: "payments",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DriverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CollectionOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodPayments", x => x.OrderId);
                    table.CheckConstraint("CK_CodPayments_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CodPayments_CashSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalSchema: "payments",
                        principalTable: "CashSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashSettlements_Reference",
                schema: "payments",
                table: "CashSettlements",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodPayments_CollectionOperationId",
                schema: "payments",
                table: "CodPayments",
                column: "CollectionOperationId",
                unique: true,
                filter: "[CollectionOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CodPayments_CustomerId",
                schema: "payments",
                table: "CodPayments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CodPayments_DriverId_SettlementId",
                schema: "payments",
                table: "CodPayments",
                columns: new[] { "DriverId", "SettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodPayments_SettlementId",
                schema: "payments",
                table: "CodPayments",
                column: "SettlementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodPayments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "CashSettlements",
                schema: "payments");
        }
    }
}
