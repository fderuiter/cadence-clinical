using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Notifications.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS audit_log_entries (
                    ""Id"" uuid NOT NULL,
                    ""EntityName"" character varying(255) NOT NULL,
                    ""EntityId"" character varying(255) NOT NULL,
                    ""Action"" character varying(50) NOT NULL,
                    ""Timestamp"" timestamp with time zone NOT NULL,
                    ""UserId"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    ""VersionIndex"" bigint NOT NULL,
                    ""ChangesJson"" text,
                    ""PredecessorHash"" character varying(64) NOT NULL,
                    ""BlockHash"" character varying(64) NOT NULL,
                    CONSTRAINT ""PK_audit_log_entries"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_audit_log_entries_EntityName_Timestamp"" ON audit_log_entries (""EntityName"", ""Timestamp"");
            ");

        migrationBuilder.CreateTable(
            name: "notification_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "notification_deliveries",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                NotificationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RetryEligible = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_deliveries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "notification_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RecipientUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RecipientRoleId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Channels = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                MessageContent = table.Column<string>(type: "text", nullable: false),
                RelatedEntityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RelatedEntityType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DeliveryState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Retries = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_audit_logs_Timestamp",
            table: "notification_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_notification_audit_logs_UserId",
            table: "notification_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_NotificationId",
            table: "notification_deliveries",
            column: "NotificationId");

        migrationBuilder.CreateIndex(
            name: "IX_notification_records_RecipientRoleId",
            table: "notification_records",
            column: "RecipientRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_notification_records_RecipientUserId",
            table: "notification_records",
            column: "RecipientUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "notification_audit_logs");

        migrationBuilder.DropTable(
            name: "notification_deliveries");

        migrationBuilder.DropTable(
            name: "notification_records");
    }
}
