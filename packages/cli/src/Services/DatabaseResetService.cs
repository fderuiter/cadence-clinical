using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Npgsql;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class DatabaseResetService
{
    public static async Task<int> ResetDatabasesAsync(bool allowOffline = true)
    {
        AnsiConsole.MarkupLine("[bold cyan]Resetting local databases (PostgreSQL/SQLite) via native C#...[/]\n");

        // 1. Safety Guard
        ValidateLocalEnvironment();

        string pgConnStr = GetPostgresConnectionString();
        ValidateLocalOnly("Postgres", pgConnStr);

        bool allOk = true;

        // 2. PostgreSQL Reset
        try
        {
            AnsiConsole.MarkupLine("  1. Purging and resetting PostgreSQL database...");
            await ResetPostgresAsync(pgConnStr);
            AnsiConsole.MarkupLine("  [green][[OK]] PostgreSQL reset complete.[/]");
        }
        catch (Exception ex)
        {
            if (allowOffline)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARNING: PostgreSQL reset skipped or failed: {ex.Message}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"  [red]ERROR: PostgreSQL reset failed: {ex.Message}[/]");
                allOk = false;
            }
        }

        // 3. SQLite Reset
        try
        {
            AnsiConsole.MarkupLine("\n  2. Purging local SQLite databases...");
            ResetSQLite();
            AnsiConsole.MarkupLine("  [green][[OK]] SQLite reset complete.[/]");
        }
        catch (Exception ex)
        {
            if (allowOffline)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARNING: SQLite reset skipped or failed: {ex.Message}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"  [red]ERROR: SQLite reset failed: {ex.Message}[/]");
                allOk = false;
            }
        }

        if (allOk)
        {
            AnsiConsole.MarkupLine("\n[bold green]Multi-Database Reset Complete.[/]");
            return 0;
        }

        return 1;
    }

    public static void ValidateLocalEnvironment()
    {
        string envName = (Environment.GetEnvironmentVariable("CADENCE_ENV") ??
                          Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                          Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "Development").Trim();

        if (envName.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
            envName.Equals("Prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Safety Guardrail Violation: Database reset cannot execute in a Production environment.");
        }
    }

    public static void ValidateLocalOnly(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        string lowerUrl = url.ToLowerInvariant();
        string[] prodKeywords = ["production", "prod", "live", "secure", "aws", "rds", "azure", "gcp", "cloud"];

        var detectedKeyword = prodKeywords.FirstOrDefault(kw => lowerUrl.Contains(kw));
        if (detectedKeyword != null)
        {
            throw new InvalidOperationException($"Safety Guardrail Violation: Production/Non-local keyword '{detectedKeyword}' detected in connection string for {name}. Aborting.");
        }

        if (url.Contains("://"))
        {
            var parts = url.Split("://", 2);
            string scheme = parts[0];
            if (scheme.StartsWith("sqlite")) return;

            string remainder = parts[1];
            try
            {
                var uri = new Uri($"http://{remainder}");
                string host = uri.Host;
                string[] localHosts = ["localhost", "127.0.0.1", "0.0.0.0", "postgres", "db", "host.docker.internal"];

                if (!localHosts.Contains(host, StringComparer.OrdinalIgnoreCase) && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Safety Guardrail Violation: Database host '{host}' for {name} is non-local.");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // URL parsing fallback check
            }
        }
    }

    private static string GetPostgresConnectionString()
    {
        string? connStr = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connStr))
        {
            connStr = Environment.GetEnvironmentVariable("DATABASE_URL");
        }

        if (string.IsNullOrEmpty(connStr))
        {
            throw new InvalidOperationException(
                "POSTGRES_CONNECTION_STRING (or DATABASE_URL) is not set. Export it (see .env.example) before resetting databases.");
        }

        if (connStr.StartsWith("postgresql+asyncpg://"))
        {
            connStr = connStr.Replace("postgresql+asyncpg://", "postgresql://");
        }

        if (connStr.StartsWith("postgresql://"))
        {
            try
            {
                var uri = new Uri(connStr);
                string userInfo = uri.UserInfo;
                string user = userInfo.Contains(':') ? userInfo.Split(':')[0] : userInfo;
                string pass = userInfo.Contains(':') ? userInfo.Split(':')[1] : "";
                string host = uri.Host;
                int port = uri.Port > 0 ? uri.Port : 5432;
                string db = uri.AbsolutePath.TrimStart('/');

                return $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
            }
            catch
            {
                // Return fallback
            }
        }

        return connStr;
    }

    private static async Task ResetPostgresAsync(string connStr)
    {
        using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public;
            DROP SCHEMA IF EXISTS audit_schema CASCADE;
            CREATE SCHEMA audit_schema;

            CREATE OR REPLACE FUNCTION audit_schema.prevent_audit_mutation()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION '21 CFR Part 11 Compliance Violation: Audit trail entries in audit_schema are strictly immutable and cannot be updated or deleted.';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                ""MigrationId"" character varying(150) NOT NULL,
                ""ProductVersion"" character varying(32) NOT NULL,
                CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
            );

            CREATE TABLE IF NOT EXISTS ""AuditLogEntries"" (
                ""Id"" uuid NOT NULL,
                ""EntityName"" text NOT NULL,
                ""EntityId"" text NOT NULL,
                ""Action"" text NOT NULL,
                ""Timestamp"" timestamp with time zone NOT NULL,
                ""UserId"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                ""ChangesJson"" text,
                ""PredecessorHash"" text NOT NULL,
                ""BlockHash"" text NOT NULL,
                CONSTRAINT ""PK_AuditLogEntries"" PRIMARY KEY (""Id"")
            );

            CREATE TABLE IF NOT EXISTS ""clinical_subjects"" (
                ""Id"" uuid NOT NULL,
                ""SubjectCode"" character varying(255) NOT NULL,
                ""StudyCode"" character varying(255) NOT NULL,
                ""SiteCode"" text,
                ""TreatmentGroupCiphertext"" text,
                ""Status"" character varying(32) NOT NULL,
                ""ActiveProtocolVersion"" text NOT NULL,
                ""EncryptedDemographics"" text,
                ""EnrollmentIndex"" integer NOT NULL,
                ""IsUnblinded"" boolean NOT NULL,
                ""UnblindedBy"" text,
                ""UnblindedReason"" text,
                ""UnblindedAt"" timestamp with time zone,
                ""WithdrawalReason"" text,
                ""WithdrawnAt"" timestamp with time zone,
                ""RandomizationId"" uuid,
                ""KitReference"" text,
                ""strat_factors"" jsonb NOT NULL,
                ""_version"" bigint NOT NULL DEFAULT 0,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""CreatedBy"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                CONSTRAINT ""PK_clinical_subjects"" PRIMARY KEY (""Id"")
            );

            CREATE TABLE IF NOT EXISTS ""site_compliance_caches"" (
                ""Id"" uuid NOT NULL,
                ""StudyCode"" character varying(255) NOT NULL,
                ""SiteCode"" character varying(255) NOT NULL,
                ""Milestone"" character varying(255) NOT NULL,
                ""IsComplete"" boolean NOT NULL,
                ""Version"" integer NOT NULL,
                ""missing_documents"" text[] NOT NULL,
                ""_version"" bigint NOT NULL DEFAULT 0,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""CreatedBy"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                CONSTRAINT ""PK_site_compliance_caches"" PRIMARY KEY (""Id"")
            );

            CREATE TABLE IF NOT EXISTS ""subject_consents"" (
                ""Id"" uuid NOT NULL,
                ""SubjectCode"" character varying(255) NOT NULL,
                ""IcfSigned"" boolean NOT NULL,
                ""IcfSignedDate"" timestamp with time zone,
                ""RequiresReconsent"" boolean NOT NULL,
                ""IsPaperOverride"" boolean NOT NULL,
                ""_version"" bigint NOT NULL DEFAULT 0,
                ""ProtocolVersion_StudyCode"" character varying(255) NOT NULL,
                ""ProtocolVersion_VersionIndex"" integer NOT NULL,
                ""ProtocolVersion_VersionTag"" text NOT NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""CreatedBy"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                CONSTRAINT ""PK_subject_consents"" PRIMARY KEY (""Id"")
            );

            CREATE TABLE IF NOT EXISTS ""clinical_observations"" (
                ""Id"" uuid NOT NULL,
                ""ObservationId"" text NOT NULL,
                ""SubjectCode"" character varying(255) NOT NULL,
                ""StudyCode"" character varying(255) NOT NULL,
                ""SiteCode"" text,
                ""TestCode"" character varying(255) NOT NULL,
                ""DomainCode"" character varying(255) NOT NULL,
                ""ResultValue"" text NOT NULL,
                ""ResultUnit"" text,
                ""ObservationTime"" timestamp with time zone NOT NULL,
                ""IsOutlier"" boolean NOT NULL DEFAULT false,
                ""AdditionalPropertiesJson"" text,
                ""_version"" bigint NOT NULL DEFAULT 0,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""CreatedBy"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                CONSTRAINT ""PK_clinical_observations"" PRIMARY KEY (""Id"")
            );

            CREATE TABLE IF NOT EXISTS ""form_submissions"" (
                ""Id"" uuid NOT NULL,
                ""SubmissionId"" text NOT NULL,
                ""FormId"" text NOT NULL,
                ""SubjectCode"" character varying(255) NOT NULL,
                ""StudyCode"" character varying(255) NOT NULL,
                ""SiteCode"" text,
                ""Status"" character varying(32) NOT NULL,
                ""FormDataJson"" text NOT NULL,
                ""SignatureManifestJson"" text,
                ""_version"" bigint NOT NULL DEFAULT 0,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""CreatedBy"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                CONSTRAINT ""PK_form_submissions"" PRIMARY KEY (""Id"")
            );

            CREATE TABLE IF NOT EXISTS ""clinical_queries"" (
                ""Id"" uuid NOT NULL,
                ""QueryId"" text NOT NULL,
                ""SubjectCode"" character varying(255) NOT NULL,
                ""StudyCode"" character varying(255) NOT NULL,
                ""SiteCode"" text,
                ""ObservationId"" text,
                ""QueryText"" text NOT NULL,
                ""Status"" character varying(32) NOT NULL,
                ""ResolutionText"" text,
                ""_version"" bigint NOT NULL DEFAULT 0,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""CreatedBy"" text NOT NULL,
                ""ReasonForChange"" text NOT NULL,
                ""VersionIndex"" bigint NOT NULL,
                CONSTRAINT ""PK_clinical_queries"" PRIMARY KEY (""Id"")
            );

            INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
            VALUES
                ('20260905174215_AddExecutionSubjectsSlice', '10.0.0'),
                ('20260905230921_AddMassTransitOutbox', '10.0.0'),
                ('20260910120000_AddExecutionObservationsSlice', '10.0.0')
            ON CONFLICT (""MigrationId"") DO NOTHING;

            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_tables WHERE tablename = 'AuditLogEntries') THEN
                    DROP TRIGGER IF EXISTS trg_prevent_audit_mutation_audit_log_entries ON ""AuditLogEntries"";
                    CREATE TRIGGER trg_prevent_audit_mutation_audit_log_entries
                    BEFORE UPDATE OR DELETE ON ""AuditLogEntries""
                    FOR EACH ROW EXECUTE FUNCTION audit_schema.prevent_audit_mutation();
                END IF;
            END $$;
        ";

        await cmd.ExecuteNonQueryAsync();
    }

    private static void ResetSQLite()
    {
        string repoRoot = FindRepoRoot();
        var dbFiles = Directory.GetFiles(repoRoot, "*.db", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(repoRoot, "*.sqlite", SearchOption.AllDirectories))
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"))
            .ToList();

        foreach (var file in dbFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Ignore transient file lock issues
            }
        }
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
