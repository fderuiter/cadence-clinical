using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Notifications.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PushSubscription : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "push_subscriptions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                P256dh = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false), // deid-ignore: VAPID P-256 public key property
                Auth = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_push_subscriptions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_push_subscriptions_Endpoint",
            table: "push_subscriptions",
            column: "Endpoint");

        migrationBuilder.CreateIndex(
            name: "IX_push_subscriptions_SubjectId",
            table: "push_subscriptions",
            column: "SubjectId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "push_subscriptions");
    }
}
