using System;

using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOutboxQuarantineAndLockCertificates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "quarantined_at",
            schema: "kernel",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "stream_key",
            schema: "kernel",
            table: "outbox_messages",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "certificate_json",
            schema: "kernel",
            table: "assertions",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "milestone",
            schema: "kernel",
            table: "assertions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "lock_certificates",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                certificate_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                lock_id = table.Column<Guid>(type: "uuid", nullable: false),
                milestone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                attestation_id = table.Column<Guid>(type: "uuid", nullable: false),
                signed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                payload_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                fingerprint_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                signature_base64 = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                certificate_json = table.Column<string>(type: "jsonb", nullable: false),
                revoked = table.Column<bool>(type: "boolean", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lock_certificates", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_quarantine_messages",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                stream_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                envelope = table.Column<string>(type: "jsonb", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                quarantined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_quarantine_messages", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_tenant_id_stream_key_enqueued_at",
            schema: "kernel",
            table: "outbox_messages",
            columns: new[] { "tenant_id", "stream_key", "enqueued_at" },
            filter: "dispatched_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_lock_certificates_tenant_id_certificate_id",
            schema: "kernel",
            table: "lock_certificates",
            columns: new[] { "tenant_id", "certificate_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_lock_certificates_tenant_id_lock_id",
            schema: "kernel",
            table: "lock_certificates",
            columns: new[] { "tenant_id", "lock_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_lock_certificates_tenant_id_study_id_issued_at",
            schema: "kernel",
            table: "lock_certificates",
            columns: new[] { "tenant_id", "study_id", "issued_at" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_quarantine_messages_tenant_id_outbox_message_id",
            schema: "kernel",
            table: "outbox_quarantine_messages",
            columns: new[] { "tenant_id", "outbox_message_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_quarantine_messages_tenant_id_stream_key_quarantined~",
            schema: "kernel",
            table: "outbox_quarantine_messages",
            columns: new[] { "tenant_id", "stream_key", "quarantined_at" });

        migrationBuilder.EnableTenantIsolation("kernel", "lock_certificates", "outbox_quarantine_messages");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantIsolation("kernel", "lock_certificates", "outbox_quarantine_messages");

        migrationBuilder.DropTable(
            name: "lock_certificates",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "outbox_quarantine_messages",
            schema: "kernel");

        migrationBuilder.DropIndex(
            name: "IX_outbox_messages_tenant_id_stream_key_enqueued_at",
            schema: "kernel",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "quarantined_at",
            schema: "kernel",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "stream_key",
            schema: "kernel",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "certificate_json",
            schema: "kernel",
            table: "assertions");

        migrationBuilder.DropColumn(
            name: "milestone",
            schema: "kernel",
            table: "assertions");
    }
}
