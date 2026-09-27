using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PCHealthMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Scans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowsVersion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CpuAverage = table.Column<double>(type: "REAL", nullable: true),
                    CpuPeak = table.Column<double>(type: "REAL", nullable: true),
                    CpuMinimum = table.Column<double>(type: "REAL", nullable: true),
                    CpuSampleCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MemoryUsagePercent = table.Column<double>(type: "REAL", nullable: true),
                    MemoryTotalBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MemoryUsedBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    TemperatureCelsius = table.Column<double>(type: "REAL", nullable: true),
                    UptimeSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    CpuStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MemoryStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StorageStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TemperatureStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OverallHealth = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsIncomplete = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanDrives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScanId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileSystem = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FreeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    UsagePercent = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanDrives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanDrives_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScanMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScanId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanMessages_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanDrives_ScanId",
                table: "ScanDrives",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanMessages_ScanId",
                table: "ScanMessages",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_Scans_Hostname_StartedAtUtc",
                table: "Scans",
                columns: new[] { "Hostname", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Scans_StartedAtUtc",
                table: "Scans",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanDrives");

            migrationBuilder.DropTable(
                name: "ScanMessages");

            migrationBuilder.DropTable(
                name: "Scans");
        }
    }
}
