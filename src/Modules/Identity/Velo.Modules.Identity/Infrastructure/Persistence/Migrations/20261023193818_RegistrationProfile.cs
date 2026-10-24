using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "identity",
                table: "Users",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationEmail",
                schema: "identity",
                table: "OtpChallenges",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationName",
                schema: "identity",
                table: "OtpChallenges",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RegistrationEmail",
                schema: "identity",
                table: "OtpChallenges");

            migrationBuilder.DropColumn(
                name: "RegistrationName",
                schema: "identity",
                table: "OtpChallenges");
        }
    }
}
