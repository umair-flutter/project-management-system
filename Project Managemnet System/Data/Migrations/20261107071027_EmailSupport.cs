using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_Managemnet_System.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueReminderFor",
                table: "Todos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverdueReminderFor",
                table: "Todos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailedAt",
                table: "ProjectInvitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "ProjectInvitations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailedAt",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotifications",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInvitations_TokenHash",
                table: "ProjectInvitations",
                column: "TokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectInvitations_TokenHash",
                table: "ProjectInvitations");

            migrationBuilder.DropColumn(
                name: "DueReminderFor",
                table: "Todos");

            migrationBuilder.DropColumn(
                name: "OverdueReminderFor",
                table: "Todos");

            migrationBuilder.DropColumn(
                name: "EmailedAt",
                table: "ProjectInvitations");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "ProjectInvitations");

            migrationBuilder.DropColumn(
                name: "EmailedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EmailNotifications",
                table: "AspNetUsers");
        }
    }
}
