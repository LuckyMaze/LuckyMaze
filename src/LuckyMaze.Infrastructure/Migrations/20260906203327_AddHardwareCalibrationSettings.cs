using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuckyMaze.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHardwareCalibrationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccelerationMmPerSec2",
                table: "Settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InvertX",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "InvertY",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginOffsetXMm",
                table: "Settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginOffsetYMm",
                table: "Settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PixelPitchMm",
                table: "Settings",
                type: "numeric",
                nullable: false,
                defaultValue: 3.0m);

            migrationBuilder.AddColumn<int>(
                name: "StepFeedRateMmPerMin",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 2400);

            migrationBuilder.AddColumn<int>(
                name: "TravelFeedRateMmPerMin",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 3000);

            // Carries forward what's currently live via Hardware:InvertX/InvertY env vars on the
            // real rig (see docs/deployment.md history) - defaulting these to false here would
            // silently undo that calibration the moment this deploys, since the app switches from
            // reading IConfiguration to reading this row in the same release.
            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "AccelerationMmPerSec2", "InvertX", "InvertY", "OriginOffsetXMm", "OriginOffsetYMm", "PixelPitchMm", "StepFeedRateMmPerMin", "TravelFeedRateMmPerMin" },
                values: new object[] { null, true, true, 0m, 0m, 3.0m, 2400, 3000 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccelerationMmPerSec2",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "InvertX",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "InvertY",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OriginOffsetXMm",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OriginOffsetYMm",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PixelPitchMm",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "StepFeedRateMmPerMin",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TravelFeedRateMmPerMin",
                table: "Settings");
        }
    }
}
