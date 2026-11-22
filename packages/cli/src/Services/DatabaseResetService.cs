using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Cadence.ToolingCore.Services;

using Npgsql;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static partial class DatabaseResetService
{
    private static readonly string[] ProdKeywords =
        ["production", "prod", "live", "secure", "aws", "rds", "azure", "gcp", "cloud"];

    private static readonly string[] LocalHosts =
        ["localhost", "127.0.0.1", "0.0.0.0", "::1", "postgres", "db", "host.docker.internal"];

    private static readonly string[] RetiredPublicTables =
    [
        "__EFMigrationsHistory",
        "__ef_migrations_history",
        "AuditLogEntries",
        "clinical_subjects",
        "site_compliance_caches",
        "subject_consents",
        "clinical_observations",
        "form_submissions",
        "clinical_queries",
    ];

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSchemaIdentifierRegex();

    public static Task<int> ResetDatabasesAsync(bool allowOffline = false) =>
        ResetDatabasesAsync(allowOffline, getEnvironmentVariable: null);

    internal static async Task<int> ResetDatabasesAsync(bool allowOffline, Func<string, string?>? getEnvironmentVariable)
    {
        AnsiConsole.MarkupLine("[bold cyan]Resetting local PostgreSQL module schemas via native C#...[/]\n");

        // 1. Safety Guard (always enforced regardless of --allow-offline)
        ValidateLocalEnvironment(getEnvironmentVariable);

        if (!TryResolvePostgresConnectionString(out string rawConnStr, getEnvironmentVariable))
        {
            const string missingMessage =
                "Set ConnectionStrings__cadence, POSTGRES_CONNECTION_STRING, or DATABASE_URL (see .env.example) before resetting databases.";
            if (allowOffline)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARNING: PostgreSQL reset skipped: {Markup.Escape(missingMessage)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"  [red]ERROR: PostgreSQL reset failed: {Markup.Escape(missingMessage)}[/]");
            return 1;
        }

        ValidateLocalOnly("Postgres", rawConnStr);
        string pgConnStr = NormalizePostgresConnectionString(rawConnStr);

        // 2. Schema-Scoped PostgreSQL Reset
        try
        {
            AnsiConsole.MarkupLine(
                $"  Purging and recreating {PersistenceModuleRegistry.AllSchemas.Count} module schemas (preserving public extensions)...");
            await ResetPostgresAsync(pgConnStr);
            AnsiConsole.MarkupLine("  [green][[OK]] PostgreSQL module schemas reset complete.[/]");
            AnsiConsole.MarkupLine("\n[bold green]PostgreSQL Schema Reset Complete.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            if (allowOffline)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARNING: PostgreSQL reset skipped or failed: {Markup.Escape(ex.Message)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"  [red]ERROR: PostgreSQL reset failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    public static void ValidateLocalEnvironment() => ValidateLocalEnvironment(getEnvironmentVariable: null);

    internal static void ValidateLocalEnvironment(Func<string, string?>? getEnvironmentVariable)
    {
        string? cadenceEnv = getEnvironmentVariable is not null
            ? getEnvironmentVariable("CADENCE_ENV")
            : Environment.GetEnvironmentVariable("CADENCE_ENV");
        string? aspNetEnv = getEnvironmentVariable is not null
            ? getEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            : Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string? genericEnv = getEnvironmentVariable is not null
            ? getEnvironmentVariable("ENVIRONMENT")
            : Environment.GetEnvironmentVariable("ENVIRONMENT");

        string envName = (cadenceEnv ?? aspNetEnv ?? genericEnv ?? "Development").Trim();

        if (envName.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
            envName.Equals("Prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Safety Guardrail Violation: Database reset cannot execute in a Production environment.");
        }
    }

    public static void ValidateLocalOnly(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string lowerUrl = url.ToLowerInvariant();
        string? detectedKeyword = ProdKeywords.FirstOrDefault(kw => lowerUrl.Contains(kw, StringComparison.Ordinal));
        if (detectedKeyword is not null)
        {
            throw new InvalidOperationException(
                $"Safety Guardrail Violation: Production/Non-local keyword '{detectedKeyword}' detected in connection string for {name}. Aborting.");
        }

        if (url.Contains("://", StringComparison.Ordinal))
        {
            string[] parts = url.Split("://", 2);
            try
            {
                var uri = new Uri($"http://{parts[1]}");
                EnsureHostIsLocal(name, uri.Host);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // Ignore malformed URI wrapper; Npgsql will reject invalid syntax on connect.
            }

            return;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(url);
            if (!string.IsNullOrWhiteSpace(builder.Host))
            {
                foreach (string host in builder.Host.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string cleanHost = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[')
                        ? host.Split(':', 2)[0]
                        : host.Trim('[', ']');
                    EnsureHostIsLocal(name, cleanHost);
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Ignore malformed key-value strings here; Npgsql reports syntax errors when opening the connection.
        }
    }

    internal static bool TryResolvePostgresConnectionString(
        out string connStr,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        string? primary = getEnvironmentVariable is not null
            ? getEnvironmentVariable("ConnectionStrings__cadence")
            : Environment.GetEnvironmentVariable("ConnectionStrings__cadence");
        if (!string.IsNullOrEmpty(primary))
        {
            connStr = primary;
            return true;
        }

        string? legacy = getEnvironmentVariable is not null
            ? getEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            : Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(legacy))
        {
            connStr = legacy;
            return true;
        }

        string? databaseUrl = getEnvironmentVariable is not null
            ? getEnvironmentVariable("DATABASE_URL")
            : Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrEmpty(databaseUrl))
        {
            connStr = databaseUrl;
            return true;
        }

        connStr = string.Empty;
        return false;
    }

    internal static string NormalizePostgresConnectionString(string connStr)
    {
        if (connStr.StartsWith("postgresql+asyncpg://", StringComparison.OrdinalIgnoreCase))
        {
            connStr = "postgresql://" + connStr["postgresql+asyncpg://".Length..];
        }

        if (connStr.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            connStr.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(connStr);
                string userInfo = uri.UserInfo;
                string user = userInfo.Contains(':', StringComparison.Ordinal)
                    ? Uri.UnescapeDataString(userInfo.Split(':', 2)[0])
                    : Uri.UnescapeDataString(userInfo);
                string pass = userInfo.Contains(':', StringComparison.Ordinal)
                    ? Uri.UnescapeDataString(userInfo.Split(':', 2)[1])
                    : string.Empty;
                string host = uri.Host;
                int port = uri.Port > 0 ? uri.Port : 5432;
                string db = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Database = db,
                    Username = user,
                    Password = pass,
                };
                return builder.ConnectionString;
            }
            catch
            {
                return connStr;
            }
        }

        return connStr;
    }

    internal static string BuildResetPostgresSql()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE SCHEMA IF NOT EXISTS public;");
        sb.AppendLine("DROP SCHEMA IF EXISTS audit_schema CASCADE;");

        foreach (string schema in PersistenceModuleRegistry.AllSchemas)
        {
            if (!SafeSchemaIdentifierRegex().IsMatch(schema))
            {
                throw new InvalidOperationException($"Refusing to interpolate invalid schema identifier '{schema}'.");
            }

            sb.AppendLine($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
            sb.AppendLine($"CREATE SCHEMA \"{schema}\";");
        }

        foreach (string retiredTable in RetiredPublicTables)
        {
            sb.AppendLine($"DROP TABLE IF EXISTS public.\"{retiredTable}\" CASCADE;");
        }

        // Remove any remaining stray user/legacy base tables in public without touching extension-owned objects (e.g. btree_gist).
        sb.AppendLine(
            """
            DO $$
            DECLARE
                r record;
            BEGIN
                FOR r IN
                    SELECT c.relname
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relkind IN ('r', 'p')
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pg_depend d
                          WHERE d.objid = c.oid
                            AND d.deptype = 'e'
                      )
                LOOP
                    EXECUTE format('DROP TABLE IF EXISTS public.%I CASCADE', r.relname);
                END LOOP;
            END $$;
            """);

        return sb.ToString();
    }

    private static async Task ResetPostgresAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildResetPostgresSql();
        await cmd.ExecuteNonQueryAsync();
    }

    private static void EnsureHostIsLocal(string name, string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        if (!LocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase) &&
            !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Safety Guardrail Violation: Database host '{host}' for {name} is non-local.");
        }
    }
}
