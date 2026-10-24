using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FulfillmentAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                schema: "orders",
                table: "Orders",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: false,
                defaultValue: "Unpaid");

            migrationBuilder.AddColumn<string>(
                name: "RestockDecisionJson",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RestockDecisionJson",
                schema: "orders",
                table: "Orders");
        }
    }
}
