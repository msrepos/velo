using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Velo.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerOtpAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "identity",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "AuthThrottles",
                schema: "identity",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    WindowStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthThrottles", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ClientKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RefreshVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Roles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { new Guid("2b0e772a-85bd-4830-b989-1674cc30d678"), "velo-customer-role-v1", "Customer", "CUSTOMER" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_ExpiresAt",
                schema: "identity",
                table: "OtpChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_PhoneNumber_CreatedAt",
                schema: "identity",
                table: "OtpChallenges",
                columns: new[] { "PhoneNumber", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId_ExpiresAt",
                schema: "identity",
                table: "Sessions",
                columns: new[] { "UserId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthThrottles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OtpChallenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "identity");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("2b0e772a-85bd-4830-b989-1674cc30d678"));

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "identity",
                table: "Users");
        }
    }
}
