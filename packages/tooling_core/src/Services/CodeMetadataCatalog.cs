using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Extracts authoritative service topology, port, profile, database module schema, electronic signature, durable
/// token-store, and audit-interceptor metadata directly from repository source files without runtime reflection.
/// Shared by <c>GateDocs</c> (Markdown marker synthesis) and the fail-closed CLI sentinels.
/// </summary>
public static partial class CodeMetadataCatalog
{
    public sealed record TopologySnapshot(
        int GatewayPort,
        IReadOnlyList<CadenceService> Services,
        int WebPort,
        int SubjectPortalPort,
        int PostgresPort,
        int KeycloakPort,
        IReadOnlyList<string> CoreBackendHosts,
        IReadOnlyList<string> CoreWebApps,
        IReadOnlyList<int> CheckedPorts);

    public sealed record DatabaseModuleSchema(
        string Module,
        string Host,
        string Schema,
        string DbContext,
        string ProjectRelativePath);

    public sealed record SignedEndpointMetadata(
        string Host,
        string Method,
        string Route,
        string? AliasRoute,
        string SemanticAction,
        string ActionTemplate,
        bool UsesBatchBinding,
        string RelativeFilePath);

    public sealed record DurableSigTokenStoreMetadata(
        string Host,
        string DbContext,
        string Schema,
        string Table,
        string RelativeFilePath);

    public sealed record AuditHostMetadata(
        string Host,
        string DbContext,
        string Schema,
        string RelativeFilePath);

    [GeneratedRegex(@"public\s+const\s+int\s+GatewayPort\s*=\s*(\d+)\s*;")]
    private static partial Regex GatewayPortConstRegex();

    [GeneratedRegex(@"new\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*\[([^\]]*)\]\s*,\s*Database:\s*(true|false)\s*,\s*Core:\s*(true|false)\s*\)")]
    private static partial Regex TopologyEntryRegex();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex QuotedStringRegex();

    [GeneratedRegex(@"^\|\s*\[([a-z0-9_-]+)\]\([a-z0-9_-]+\.md\)\s*\|\s*`([A-Za-z0-9_]+)`\s*(?:\|\s*\d+\s*)?\|$", RegexOptions.Multiline)]
    private static partial Regex SchemaReadmeRowRegex();

    [GeneratedRegex(@"\.AddCadenceDurableSigTokenStore<\s*([A-Za-z0-9_]+)\s*>\s*\(\s*\)")]
    private static partial Regex AddDurableSigTokenStoreRegex();

    [GeneratedRegex(@"\bnew\s+(?:Cadence\.Database\.)?GxPAuditInterceptor\s*\(")]
    private static partial Regex NewGxpAuditInterceptorRegex();

    [GeneratedRegex(@"AddCadencePostgresDbContext<\s*([A-Za-z0-9_]+)\s*>")]
    private static partial Regex AddPostgresDbContextRegex();

    public static TopologySnapshot GetTopology(string repoRoot)
    {
        int gatewayPort = ServiceTopology.GatewayPort;
        IReadOnlyList<CadenceService> services = ServiceTopology.Services;

        string topologyPath = Path.Combine(repoRoot, "packages", "topology", "src", "ServiceTopology.cs");
        if (File.Exists(topologyPath))
        {
            string topologySource = File.ReadAllText(topologyPath);
            var gwMatch = GatewayPortConstRegex().Match(topologySource);
            if (gwMatch.Success && int.TryParse(gwMatch.Groups[1].Value, CultureInfo.InvariantCulture, out int parsedGw))
            {
                gatewayPort = parsedGw;
            }

            var parsedServices = new List<CadenceService>();
            foreach (GroupCollection groups in TopologyEntryRegex().Matches(topologySource).Select(m => m.Groups))
            {
                string name = groups[1].Value;
                string dir = groups[2].Value;
                int port = int.Parse(groups[3].Value, CultureInfo.InvariantCulture);
                string urlVar = groups[4].Value;
                string[] routes = QuotedStringRegex().Matches(groups[5].Value).Select(q => q.Groups[1].Value).ToArray();
                bool database = string.Equals(groups[6].Value, "true", StringComparison.Ordinal);
                bool core = string.Equals(groups[7].Value, "true", StringComparison.Ordinal);
                parsedServices.Add(new CadenceService(name, dir, port, urlVar, routes, database, core));
            }

            if (parsedServices.Count > 0)
            {
                services = parsedServices;
            }
        }

        int webPort = ParseVitePort(Path.Combine(repoRoot, "apps", "web", "vite.config.js"), defaultPort: 3000);
        int subjectPortalPort = ParseVitePort(Path.Combine(repoRoot, "apps", "subject-portal", "vite.config.js"), defaultPort: 5174);
        const int postgresPort = 5432;
        int keycloakPort = 8080;

        var coreBackends = new List<string> { "gateway" };
        coreBackends.AddRange(services.Where(s => s.Core).Select(s => s.Name));
        var coreWebApps = new List<string> { "web" };

        string appHostPath = Path.Combine(repoRoot, "apps", "apphost", "src", "Program.cs");
        if (File.Exists(appHostPath))
        {
            string appHostSource = File.ReadAllText(appHostPath);
            var kcMatch = AppHostKeycloakPortRegex().Match(appHostSource);
            if (kcMatch.Success && int.TryParse(kcMatch.Groups[1].Value, CultureInfo.InvariantCulture, out int parsedKc))
            {
                keycloakPort = parsedKc;
            }

            var (parsedCoreBackends, parsedCoreWebApps) = ParseAppHostCoreProfile(appHostSource);
            if (parsedCoreBackends.Count > 0)
            {
                coreBackends = parsedCoreBackends;
            }

            if (parsedCoreWebApps.Count > 0)
            {
                coreWebApps = parsedCoreWebApps;
            }
        }

        int[] checkedPorts = [gatewayPort, .. services.Select(s => s.Port), webPort, subjectPortalPort, postgresPort, keycloakPort];
        return new TopologySnapshot(
            gatewayPort,
            services,
            webPort,
            subjectPortalPort,
            postgresPort,
            keycloakPort,
            coreBackends,
            coreWebApps,
            checkedPorts);
    }

    public static IReadOnlyList<DatabaseModuleSchema> GetDatabaseModules(string repoRoot)
    {
        string schemaDir = Path.Combine(repoRoot, "docs", "schema");
        string readmePath = Path.Combine(schemaDir, "README.md");
        if (!File.Exists(readmePath))
        {
            return [];
        }

        string readme = File.ReadAllText(readmePath).ReplaceLineEndings("\n");
        var results = new List<DatabaseModuleSchema>();

        foreach (GroupCollection rowGroups in SchemaReadmeRowRegex().Matches(readme).Select(row => row.Groups))
        {
            string module = rowGroups[1].Value;
            string dbContext = rowGroups[2].Value;
            PersistenceModule? catalogEntry = PersistenceModuleCatalog.Find(module);

            string moduleDocPath = Path.Combine(schemaDir, module + ".md");
            string schema = catalogEntry?.Schema ?? module;
            string projectRelativePath = catalogEntry?.ProjectRelativePath
                ?? $"apps/{module}/src/Cadence.Apps.{char.ToUpperInvariant(module[0])}{module[1..]}.csproj";
            string host = ResolveHostFromRelativePath(projectRelativePath);
            if (string.Equals(host, projectRelativePath, StringComparison.Ordinal))
            {
                host = module;
            }

            if (File.Exists(moduleDocPath))
            {
                string doc = File.ReadAllText(moduleDocPath).ReplaceLineEndings("\n");
                schema = ResolveSchemaFromModuleDoc(schema, doc);
                host = ResolveHostFromModuleDoc(host, doc);
            }

            results.Add(new DatabaseModuleSchema(module, host, schema, dbContext, projectRelativePath));
        }

        return results.OrderBy(m => m.Module, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<DurableSigTokenStoreMetadata> GetDurableSigTokenStores(string repoRoot)
    {
        var dbModules = GetDatabaseModules(repoRoot);
        var results = new List<DurableSigTokenStoreMetadata>();

        foreach (string file in EnumerateProductionCSharpFiles(repoRoot))
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.StartsWith("packages/database/", StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(file);
            foreach (Match match in AddDurableSigTokenStoreRegex().Matches(source))
            {
                string dbContext = match.Groups[1].Value;
                string host = ResolveHostFromRelativePath(rel);
                string schema = ResolveSchemaForDbContext(repoRoot, dbContext, host, dbModules);
                results.Add(new DurableSigTokenStoreMetadata(
                    host,
                    dbContext,
                    schema,
                    $"{schema}.consumed_signature_tokens",
                    rel));
            }
        }

        return results
            .OrderBy(s => s.Host, StringComparer.Ordinal)
            .ThenBy(s => s.DbContext, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<AuditHostMetadata> GetAuditInterceptorHosts(string repoRoot)
    {
        var dbModules = GetDatabaseModules(repoRoot);
        var results = new List<AuditHostMetadata>();
        string dbExtPath = Path.Combine(repoRoot, "packages", "database", "src", "CadenceDatabaseExtensions.cs");
        bool defaultPostgresAuditEnabled = File.Exists(dbExtPath) &&
            NewGxpAuditInterceptorRegex().IsMatch(File.ReadAllText(dbExtPath));

        foreach (string file in EnumerateProductionCSharpFiles(repoRoot))
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (!rel.StartsWith("apps/", StringComparison.Ordinal) || !rel.EndsWith("/Program.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(file);
            var dbMatch = AddPostgresDbContextRegex().Match(source);
            bool hasDefaultAudit = defaultPostgresAuditEnabled &&
                dbMatch.Success &&
                !source.Contains("enableDefaultGxPAudit: false", StringComparison.Ordinal);
            if (!hasDefaultAudit && !NewGxpAuditInterceptorRegex().IsMatch(source))
            {
                continue;
            }

            string host = ResolveHostFromRelativePath(rel);
            string dbContext = dbMatch.Success
                ? dbMatch.Groups[1].Value
                : $"{char.ToUpperInvariant(host[0])}{host[1..]}DbContext";
            string schema = ResolveSchemaForDbContext(repoRoot, dbContext, host, dbModules);
            results.Add(new AuditHostMetadata(host, dbContext, schema, rel));
        }

        return results.OrderBy(h => h.Host, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<SignedEndpointMetadata> GetSignedEndpoints(string repoRoot)
    {
        var results = new List<SignedEndpointMetadata>();

        foreach (string file in EnumerateProductionCSharpFiles(repoRoot))
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.StartsWith("packages/security/", StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(file).ReplaceLineEndings("\n");
            if (!source.Contains(".RequireElectronicSignature", StringComparison.Ordinal))
            {
                continue;
            }

            results.AddRange(ParseSignedEndpointsFromFile(source, rel));
        }

        return results
            .OrderBy(e => e.Host, StringComparer.Ordinal)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .ToList();
    }
}
