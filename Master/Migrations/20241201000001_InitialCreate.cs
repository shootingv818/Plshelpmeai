using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IvaScanner.Master.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IvaAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    SessionData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedWorkerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    LastUsed = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IvaAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PhoneNumbers = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    TotalTasks = table.Column<int>(type: "int", nullable: false),
                    CompletedTasks = table.Column<int>(type: "int", nullable: false),
                    FailedTasks = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Exception = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkerId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TaskId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentTaskId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TasksCompleted = table.Column<int>(type: "int", nullable: false),
                    TasksFailed = table.Column<int>(type: "int", nullable: false),
                    ProxyUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IvaAccountId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Latency = table.Column<TimeSpan>(type: "time", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workers_IvaAccounts_IvaAccountId",
                        column: x => x.IvaAccountId,
                        principalTable: "IvaAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScanTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    JobId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RangeStart = table.Column<int>(type: "int", nullable: false),
                    RangeEnd = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProcessingTime = table.Column<TimeSpan>(type: "time", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanTasks_ScanJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ScanJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScanTasks_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IvaAccounts_AssignedWorkerId",
                table: "IvaAccounts",
                column: "AssignedWorkerId",
                unique: true,
                filter: "[AssignedWorkerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IvaAccounts_PhoneNumber",
                table: "IvaAccounts",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IvaAccounts_Status",
                table: "IvaAccounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_CreatedAt",
                table: "ScanJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_Status",
                table: "ScanJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScanTasks_JobId",
                table: "ScanTasks",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanTasks_LeaseExpiry",
                table: "ScanTasks",
                column: "LeaseExpiry");

            migrationBuilder.CreateIndex(
                name: "IX_ScanTasks_Status",
                table: "ScanTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScanTasks_WorkerId",
                table: "ScanTasks",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_JobId",
                table: "SystemLogs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_Level",
                table: "SystemLogs",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_TaskId",
                table: "SystemLogs",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_Timestamp",
                table: "SystemLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_SystemLogs_WorkerId",
                table: "SystemLogs",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_Workers_IvaAccountId",
                table: "Workers",
                column: "IvaAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Workers_LastHeartbeat",
                table: "Workers",
                column: "LastHeartbeat");

            migrationBuilder.CreateIndex(
                name: "IX_Workers_Status",
                table: "Workers",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_IvaAccounts_Workers_AssignedWorkerId",
                table: "IvaAccounts",
                column: "AssignedWorkerId",
                principalTable: "Workers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IvaAccounts_Workers_AssignedWorkerId",
                table: "IvaAccounts");

            migrationBuilder.DropTable(
                name: "ScanTasks");

            migrationBuilder.DropTable(
                name: "SystemLogs");

            migrationBuilder.DropTable(
                name: "ScanJobs");

            migrationBuilder.DropTable(
                name: "Workers");

            migrationBuilder.DropTable(
                name: "IvaAccounts");
        }
    }
}