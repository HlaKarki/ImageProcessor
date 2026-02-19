using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageProcessor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnalysisToJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "AiAnalysis",
                table: "Jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiCompletedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiErrorMessage",
                table: "Jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiRetryCount",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiStartedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiStatus",
                table: "Jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAnalysis",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiCompletedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiErrorMessage",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiRetryCount",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiStartedAt",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AiStatus",
                table: "Jobs");
        }
    }
}
