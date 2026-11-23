using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class DatabaseResetService
{
    public const string PrimaryConnectionStringVariable = "ConnectionStrings__cadence";
    public const string LegacyConnectionStringVariable = "POSTGRES_CONNECTION_STRING";

    private const string MigratorProject =
        "packages/tooling_core/src/Cadence.DatabaseMigrator/Cadence.DatabaseMigrator.csproj";

    private static readonly string[] ProdKeywords =
    [
        "production",
        "prod",
        "live",
        "secure",
        "aws",
        "rds",
        "azure",
        "gcp",
        "cloud",
    ];

    private static readonly string[] LocalHosts =
    [
        "localhost",
        "127.0.0.1",
        "::1",
        "0.0.0.0",
        "postgres",
        "db",
        "host.docker.internal",
    ];

    public static async Task<int> ResetDatabasesAsync(
        bool allowOffline = false,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        AnsiConsole.MarkupLine("[bold cyan]Resetting local PostgreSQL database via native C# and EF Core migrations...[/]\n");

        ValidateLocalEnvironment(getEnvironmentVariable);

        if (!TryGetConfiguredPostgresConnectionString(out string rawConnStr, getEnvironmentVariable))
        {
            string missingMessage =
                $"No PostgreSQL connection string configured. Set {PrimaryConnectionStringVariable} or {LegacyConnectionStringVariable}.";
            if (allowOffline)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARNING: PostgreSQL reset skipped: {Markup.Escape(missingMessage)}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"  [red]ERROR: PostgreSQL reset failed: {Markup.Escape(missingMessage)}[/]");
            return 1;
        }

        ValidateLocalOnly("Postgres", rawConnStr);
        string pgConnStr = ParsePostgresConnectionString("Postgres", rawConnStr).ConnectionString;

        try
        {
            AnsiConsole.MarkupLine("  1. Dropping non-system PostgreSQL schemas and purging public tables (preserving extensions)...");
            await ResetPostgresAsync(pgConnStr);
            AnsiConsole.MarkupLine("  [green][[OK]] Non-system schemas purged and public schema cleaned.[/]");

            AnsiConsole.MarkupLine("  2. Applying EF Core migrations across all PostgreSQL schemas...");
            int migrateExitCode = RunDatabaseMigrator(pgConnStr);
            if (migrateExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Cadence.DatabaseMigrator exited with code {migrateExitCode}.");
            }

            AnsiConsole.MarkupLine("  [green][[OK]] All PostgreSQL schemas migrated.[/]");
            AnsiConsole.MarkupLine("\n[bold green]PostgreSQL Database Reset Complete.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            string escapedMessage = Markup.Escape(ex.Message);
            if (allowOffline)
            {
                AnsiConsole.MarkupLine($"  [yellow]WARNING: PostgreSQL reset skipped or failed: {escapedMessage}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"  [red]ERROR: PostgreSQL reset failed: {escapedMessage}[/]");
            return 1;
        }
    }

    public static void ValidateLocalEnvironment(Func<string, string?>? getEnv = null)
    {
        string? rawEnv = getEnv is not null
            ? getEnv("CADENCE_ENV") ?? getEnv("ASPNETCORE_ENVIRONMENT") ?? getEnv("ENVIRONMENT")
            : Environment.GetEnvironmentVariable("CADENCE_ENV") ??
              Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
              Environment.GetEnvironmentVariable("ENVIRONMENT");

        string envName = (rawEnv ?? "Development").Trim();

        if (envName.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
            envName.Equals("Prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Safety Guardrail Violation: Database reset cannot execute in a Production environment.");
        }
    }

    public static void ValidateLocalOnly(string name, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Safety Guardrail Violation: Connection string for {name} is empty.");
        }

        string lower = connectionString.ToLowerInvariant();
        string? detectedKeyword = ProdKeywords.FirstOrDefault(kw => lower.Contains(kw, StringComparison.Ordinal));
        if (detectedKeyword is not null)
        {
            throw new InvalidOperationException(
                $"Safety Guardrail Violation: Production/Non-local keyword '{detectedKeyword}' detected in connection string for {name}. Aborting.");
        }

        NpgsqlConnectionStringBuilder builder = ParsePostgresConnectionString(name, connectionString);
        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            throw new InvalidOperationException(
                $"Safety Guardrail Violation: Connection string for {name} must specify an explicit local Host.");
        }

        foreach (string rawSegment in builder.Host.Split(','))
        {
            string host = ExtractHostName(name, rawSegment);
            if (!IsLocalHost(host))
            {
                throw new InvalidOperationException(
                    $"Safety Guardrail Violation: Database host '{host}' for {name} is non-local.");
            }
        }
    }

    public static string ResolvePostgresConnectionString(Func<string, string?>? getEnv = null)
    {
        string raw = GetConfiguredPostgresConnectionString(getEnv);
        return ParsePostgresConnectionString("Postgres", raw).ConnectionString;
    }

    public static string GetConfiguredPostgresConnectionString(Func<string, string?>? getEnv = null)
    {
        if (TryGetConfiguredPostgresConnectionString(out string configured, getEnv))
        {
            return configured;
        }

        throw new InvalidOperationException(
            $"No PostgreSQL connection string configured. Set {PrimaryConnectionStringVariable} or {LegacyConnectionStringVariable}.");
    }

    public static NpgsqlConnectionStringBuilder ParsePostgresConnectionString(string name, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string for {name} cannot be empty.");
        }

        string trimmed = connectionString.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            try
            {
                return new NpgsqlConnectionStringBuilder(trimmed);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid PostgreSQL connection string for {name}: {ex.Message}",
                    ex);
            }
        }

        if (!trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported URI scheme in connection string for {name}; expected postgres:// or postgresql://.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException($"Invalid PostgreSQL URI for {name}.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder { Host = uri.Host };
            if (!uri.IsDefaultPort && uri.Port > 0)
            {
                builder.Port = uri.Port;
            }

            string database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            if (!string.IsNullOrWhiteSpace(database))
            {
                builder.Database = database;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                int sep = uri.UserInfo.IndexOf(':', StringComparison.Ordinal);
                builder.Username = Uri.UnescapeDataString(sep >= 0 ? uri.UserInfo[..sep] : uri.UserInfo);
                if (sep >= 0)
                {
                    builder.Password = Uri.UnescapeDataString(uri.UserInfo[(sep + 1)..]);
                }
            }

            foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                string key = Uri.UnescapeDataString(eq >= 0 ? pair[..eq] : pair).Replace('_', ' ');
                string val = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..]) : string.Empty;
                builder[key] = val;
            }

            return builder;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Invalid PostgreSQL URI options for {name}: {ex.Message}",
                ex);
        }
    }

    public static async Task ResetPostgresAsync(string connStr, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        var nonPublicSchemas = new List<string>();
        await using (var selectCmd = conn.CreateCommand())
        {
            selectCmd.CommandText = """
                SELECT nspname
                FROM pg_catalog.pg_namespace
                WHERE nspname NOT LIKE 'pg_%'
                  AND nspname <> 'information_schema'
                  AND nspname <> 'public'
                ORDER BY nspname;
                """;

            await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                nonPublicSchemas.Add(reader.GetString(0));
            }
        }

        await using var resetCmd = conn.CreateCommand();
        resetCmd.CommandText = BuildResetPostgresSql(nonPublicSchemas);
        await resetCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string BuildResetPostgresSql(IEnumerable<string> nonPublicSchemas)
    {
        var ddl = new StringBuilder();
        foreach (string schema in nonPublicSchemas)
        {
            if (string.Equals(schema, "public", StringComparison.Ordinal))
            {
                continue;
            }

            string quoted = "\"" + schema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            ddl.Append("DROP SCHEMA IF EXISTS ").Append(quoted).Append(" CASCADE;\n");
        }

        ddl.Append("CREATE SCHEMA IF NOT EXISTS public;\n");
        ddl.Append(
            """
            DO $$
            DECLARE
                r record;
            BEGIN
                FOR r IN
                    SELECT c.relname, c.relkind
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public'
                      AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pg_depend d
                          WHERE d.objid = c.oid
                            AND d.deptype = 'e'
                      )
                LOOP
                    IF r.relkind = 'm' THEN
                        EXECUTE format('DROP MATERIALIZED VIEW IF EXISTS public.%I CASCADE', r.relname);
                    ELSIF r.relkind = 'v' THEN
                        EXECUTE format('DROP VIEW IF EXISTS public.%I CASCADE', r.relname);
                    ELSIF r.relkind = 'f' THEN
                        EXECUTE format('DROP FOREIGN TABLE IF EXISTS public.%I CASCADE', r.relname);
                    ELSIF r.relkind = 'S' THEN
                        EXECUTE format('DROP SEQUENCE IF EXISTS public.%I CASCADE', r.relname);
                    ELSE
                        EXECUTE format('DROP TABLE IF EXISTS public.%I CASCADE', r.relname);
                    END IF;
                END LOOP;
            END $$;
            """);

        return ddl.ToString();
    }

    public static int RunDatabaseMigrator(string connectionString, string? repoRoot = null, string? moduleName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        repoRoot ??= FindRepoRoot();

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string arg in new[] { "run", "--project", MigratorProject, "--", "--connection", connectionString })
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(moduleName) &&
            !string.Equals(moduleName.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("--module");
            psi.ArgumentList.Add(moduleName.Trim());
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            AnsiConsole.MarkupLine("[red]ERROR: Could not start 'dotnet run' for Cadence.DatabaseMigrator.[/]");
            return 1;
        }

        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            Console.WriteLine(stdout.TrimEnd());
        }

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine(stderr.TrimEnd());
        }

        return process.ExitCode;
    }

    private static bool TryGetConfiguredPostgresConnectionString(
        out string connectionString,
        Func<string, string?>? getEnv)
    {
        string? configured = getEnv is not null
            ? getEnv(PrimaryConnectionStringVariable)
            : Environment.GetEnvironmentVariable("ConnectionStrings__cadence");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            connectionString = configured.Trim();
            return true;
        }

        configured = getEnv is not null
            ? getEnv(LegacyConnectionStringVariable)
            : Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            connectionString = configured.Trim();
            return true;
        }

        connectionString = string.Empty;
        return false;
    }

    private static string ExtractHostName(string name, string rawSegment)
    {
        string entry = rawSegment.Trim();
        if (entry.Length == 0)
        {
            throw new InvalidOperationException(
                $"Safety Guardrail Violation: Empty host entry in connection string for {name}.");
        }

        if (entry.StartsWith('['))
        {
            int closeBracket = entry.IndexOf(']', StringComparison.Ordinal);
            if (closeBracket <= 1)
            {
                throw new InvalidOperationException(
                    $"Safety Guardrail Violation: Malformed bracketed host '{entry}' for {name}.");
            }

            string trailing = entry[(closeBracket + 1)..];
            if (trailing.Length > 0 && !trailing.StartsWith(':'))
            {
                throw new InvalidOperationException(
                    $"Safety Guardrail Violation: Malformed bracketed host '{entry}' for {name}.");
            }

            return entry[1..closeBracket];
        }

        int firstColon = entry.IndexOf(':', StringComparison.Ordinal);
        if (firstColon >= 0 && firstColon == entry.LastIndexOf(':'))
        {
            string hostPart = entry[..firstColon];
            if (string.IsNullOrWhiteSpace(hostPart))
            {
                throw new InvalidOperationException(
                    $"Safety Guardrail Violation: Missing host before port in '{entry}' for {name}.");
            }

            return hostPart;
        }

        return entry;
    }

    private static bool IsLocalHost(string host) =>
        LocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase) ||
        (host.Length > ".local".Length &&
         host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
         !host.Contains('/', StringComparison.Ordinal));

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
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }
}
