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

    [GeneratedRegex(@"\bconst\s+DEV_PORT\s*=\s*(\d+)\s*;")]
    private static partial Regex ViteDevPortRegex();

    [GeneratedRegex(@"AddProject<[^>]+>\s*\(\s*""([^""]+)""\s*\)")]
    private static partial Regex AppHostAddProjectRegex();

    [GeneratedRegex(@"AddViteApp\s*\(\s*""([^""]+)""")]
    private static partial Regex AppHostAddViteAppRegex();

    [GeneratedRegex(@"AddContainer\s*\(\s*""keycloak""[\s\S]{0,200}?\.WithHttpEndpoint\s*\(\s*port:\s*(\d+)")]
    private static partial Regex AppHostKeycloakPortRegex();

    [GeneratedRegex(@"^\|\s*\[([a-z0-9_-]+)\]\([a-z0-9_-]+\.md\)\s*\|\s*`([A-Za-z0-9_]+)`\s*(?:\|\s*\d+\s*)?\|$", RegexOptions.Multiline)]
    private static partial Regex SchemaReadmeRowRegex();

    [GeneratedRegex(@"Generated from the EF Core model of `([^`]+)`")]
    private static partial Regex SchemaModelTypeRegex();

    [GeneratedRegex(@"Unqualified tables live in the `([^`]+)` schema\.")]
    private static partial Regex SchemaDefaultRegex();

    [GeneratedRegex(@"(?:^##\s+([a-z0-9_]+)\.[A-Za-z0-9_]+|^\|\s*\[`([a-z0-9_]+)\.[A-Za-z0-9_]+`\])", RegexOptions.Multiline)]
    private static partial Regex SchemaQualifiedTableRegex();

    [GeneratedRegex(@"\.AddCadenceDurableSigTokenStore<\s*([A-Za-z0-9_]+)\s*>\s*\(\s*\)")]
    private static partial Regex AddDurableSigTokenStoreRegex();

    [GeneratedRegex(@"HasDefaultSchema\s*\(\s*(?:""([^""]+)""|([A-Za-z0-9_]+DbContext)\.Schema)\s*\)")]
    private static partial Regex HasDefaultSchemaRegex();

    [GeneratedRegex(@"\bnew\s+(?:Cadence\.Database\.)?GxPAuditInterceptor\s*\(")]
    private static partial Regex NewGxpAuditInterceptorRegex();

    [GeneratedRegex(@"AddCadencePostgresDbContext<\s*([A-Za-z0-9_]+)\s*>")]
    private static partial Regex AddPostgresDbContextRegex();

    [GeneratedRegex(@"(?:public|private|internal)\s+const\s+string\s+([A-Za-z0-9_]+)\s*=\s*(\$?""[^""]+"")\s*;")]
    private static partial Regex ConstStringRegex();

    [GeneratedRegex(@"public\s+static\s+string\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)\s*=>\s*(\$?""[^""]+"")\s*;")]
    private static partial Regex ActionTemplateMethodRegex();

    [GeneratedRegex(@"\b(?:var|RouteGroupBuilder)\s+([A-Za-z0-9_]+)\s*=\s*([A-Za-z0-9_]+)\.MapGroup\s*\(\s*([^)]+?)\s*\)")]
    private static partial Regex MapGroupRegex();

    [GeneratedRegex(@"(?:([A-Za-z0-9_]+)\.MapGroup\s*\(\s*""([^""]+)""\s*\)(?:(?![;{])[\s\S]){0,120}?|([A-Za-z0-9_]+))\s*\.Map(Post|Put|Patch|Delete)\s*\(\s*(?:""([^""]*)""|(string\.Empty))(?:""[^""]*""|[^;])*?;")]
    private static partial Regex MapEndpointStatementRegex();

    [GeneratedRegex(@"\.RequireElectronicSignature\s*\(\s*([A-Za-z0-9_]+)\s*(?:,\s*([\s\S]*?))?\)\s*(?:;|\.)")]
    private static partial Regex RequireElectronicSignatureRegex();

    [GeneratedRegex(@"new\s+SigTokenBinding\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*,")]
    private static partial Regex SigTokenBindingCtorRegex();

    [GeneratedRegex(@"private\s+static\s+void\s+([A-Za-z0-9_]+)\s*\(\s*RouteHandlerBuilder\s+\w+\s*\)\s*=>[\s\S]*?\.RequireElectronicSignature\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*\)\s*;")]
    private static partial Regex WrapperRequireSignatureRegex();

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

        foreach (string file in EnumerateProductionCSharpFiles(repoRoot))
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (!rel.StartsWith("apps/", StringComparison.Ordinal) || !rel.EndsWith("/Program.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(file);
            var dbMatch = AddPostgresDbContextRegex().Match(source);
            if (!dbMatch.Success && !NewGxpAuditInterceptorRegex().IsMatch(source))
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
            if (!source.Contains(".RequireElectronicSignature(", StringComparison.Ordinal))
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

    private static IReadOnlyList<SignedEndpointMetadata> ParseSignedEndpointsFromFile(string source, string relativePath)
    {
        string host = ResolveHostFromRelativePath(relativePath);
        var constants = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KernelApiEndpointRouteBuilderExtensions.RoutePrefix"] = "/api/v1/kernel",
            ["FormEndpoints.RoutePrefix"] = "/api/v1/execution/forms",
        };

        foreach (GroupCollection constGroups in ConstStringRegex().Matches(source).Select(cm => cm.Groups))
        {
            string name = constGroups[1].Value;
            string rawValue = StripQuotesAndInterpolation(constGroups[2].Value, constants);
            constants[name] = rawValue;
        }

        var actionMethods = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GroupCollection actionGroups in ActionTemplateMethodRegex().Matches(source).Select(am => am.Groups))
        {
            string methodName = actionGroups[1].Value;
            string template = NormalizeActionTemplate(StripQuotesAndInterpolation(actionGroups[3].Value, constants));
            actionMethods[methodName] = template;
        }

        string defaultBaseGroup = ResolveDefaultGroupPrefix(relativePath);
        var groups = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app"] = string.Empty,
            ["routes"] = string.Empty,
            ["endpoints"] = string.Empty,
            ["kernel"] = "/api/v1/kernel",
            ["group"] = defaultBaseGroup,
        };

        foreach (GroupCollection groupMatch in MapGroupRegex().Matches(source).Select(gm => gm.Groups))
        {
            string targetVar = groupMatch[1].Success ? groupMatch[1].Value : groupMatch[2].Value;
            string parentVar = groupMatch[2].Value;
            string argExpr = groupMatch[3].Value.Trim();
            string segment = argExpr == "string.Empty"
                ? string.Empty
                : StripQuotesAndInterpolation(argExpr, constants);
            string parentPrefix = groups.GetValueOrDefault(parentVar, defaultBaseGroup);
            groups[targetVar] = CombineRoute(parentPrefix, segment);
        }

        var list = new List<SignedEndpointMetadata>();

        // Case A: local wrapper helper like `MapApprove(group.MapPost(...))` that attaches `.RequireElectronicSignature(...)`
        var wrapperMatch = WrapperRequireSignatureRegex().Match(source);
        string? wrapperName = null;
        if (wrapperMatch.Success)
        {
            wrapperName = wrapperMatch.Groups[1].Value;
            string semanticIdent = wrapperMatch.Groups[2].Value;
            string semanticAction = constants.GetValueOrDefault(semanticIdent, semanticIdent);
            string actionTemplate = ResolveActionFromHelper(source, wrapperMatch.Groups[3].Value, constants, actionMethods);

            var wrappedRoutes = new List<(string Method, string Route)>();
            var callRegex = new Regex(
                $@"{Regex.Escape(wrapperName)}\s*\(\s*([A-Za-z0-9_]+)\.Map(Post|Put|Patch|Delete)\s*\(\s*""([^""]+)""",
                RegexOptions.CultureInvariant);
            foreach (GroupCollection callGroups in callRegex.Matches(source).Select(call => call.Groups))
            {
                string grp = groups.GetValueOrDefault(callGroups[1].Value, defaultBaseGroup);
                string verb = callGroups[2].Value.ToUpperInvariant();
                string fullRoute = NormalizeRouteParameters(CombineRoute(grp, callGroups[3].Value));
                wrappedRoutes.Add((verb, fullRoute));
            }

            if (wrappedRoutes.Count > 0)
            {
                string? alias = wrappedRoutes.Count > 1 ? $"{wrappedRoutes[1].Method} {wrappedRoutes[1].Route}" : null;
                list.Add(new SignedEndpointMetadata(
                    host,
                    wrappedRoutes[0].Method,
                    wrappedRoutes[0].Route,
                    alias,
                    semanticAction,
                    actionTemplate.Length > 0 ? actionTemplate : wrappedRoutes[0].Route,
                    UsesBatchBinding: false,
                    relativePath));
            }
        }

        // Case B: direct fluent `.MapPost(...).RequireElectronicSignature(...)` chains
        foreach (Match stmt in MapEndpointStatementRegex().Matches(source))
        {
            string statementText = stmt.Value;
            if (wrapperName is not null && statementText.Contains(wrapperName + "(", StringComparison.Ordinal))
            {
                continue;
            }

            var sigMatch = RequireElectronicSignatureRegex().Match(statementText);
            if (!sigMatch.Success)
            {
                continue;
            }

            string basePrefix = stmt.Groups[1].Success
                ? CombineRoute(groups.GetValueOrDefault(stmt.Groups[1].Value, defaultBaseGroup), stmt.Groups[2].Value)
                : groups.GetValueOrDefault(stmt.Groups[3].Value, defaultBaseGroup);
            string verb = stmt.Groups[4].Value.ToUpperInvariant();
            string subPath = stmt.Groups[6].Success ? string.Empty : stmt.Groups[5].Value;
            string fullRoute = NormalizeRouteParameters(CombineRoute(basePrefix, subPath));

            string firstArg = sigMatch.Groups[1].Value.Trim();
            string? secondArg = sigMatch.Groups[2].Success ? sigMatch.Groups[2].Value.Trim() : null;

            string semanticAction;
            string actionTemplate;
            bool usesBatch = false;

            if (secondArg is null)
            {
                // Overload `RequireElectronicSignature(DelegationSignOffBinding)` returning `new SigTokenBinding(action, semanticAction, batchId)`
                var bindingMatch = ResolveSigTokenBindingFromHelper(source, firstArg);
                if (bindingMatch.Success)
                {
                    actionTemplate = constants.GetValueOrDefault(bindingMatch.Groups[1].Value, fullRoute);
                    semanticAction = constants.GetValueOrDefault(bindingMatch.Groups[2].Value, firstArg);
                    usesBatch = true;
                }
                else
                {
                    semanticAction = constants.GetValueOrDefault(firstArg, firstArg);
                    actionTemplate = fullRoute;
                }
            }
            else
            {
                semanticAction = constants.GetValueOrDefault(firstArg, StripQuotesAndInterpolation(firstArg, constants));
                actionTemplate = ResolveSecondArgAction(source, secondArg, fullRoute, constants, actionMethods);
            }

            list.Add(new SignedEndpointMetadata(
                host,
                verb,
                fullRoute,
                AliasRoute: null,
                semanticAction,
                NormalizeActionPathAgainstRoute(actionTemplate, fullRoute),
                usesBatch,
                relativePath));
        }

        return list;
    }

    private static string ResolveSecondArgAction(
        string source,
        string secondArg,
        string fallbackRoute,
        Dictionary<string, string> constants,
        Dictionary<string, string> actionMethods)
    {
        // e.g. `_ => ApplyLockAction`
        int arrow = secondArg.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            string rhs = secondArg[(arrow + 2)..].Trim();
            if (constants.TryGetValue(rhs, out string? constVal))
            {
                return constVal;
            }

            foreach (var (methodName, tmpl) in actionMethods)
            {
                if (rhs.StartsWith(methodName + "(", StringComparison.Ordinal))
                {
                    return tmpl;
                }
            }
        }

        if (Regex.IsMatch(secondArg, "^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
        {
            string resolved = ResolveActionFromHelper(source, secondArg, constants, actionMethods);
            if (resolved.Length > 0)
            {
                return resolved;
            }
        }

        return fallbackRoute;
    }

    private static Match ResolveSigTokenBindingFromHelper(string source, string helperName)
    {
        if (Regex.IsMatch(helperName, "^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
        {
            var scopedRegex = new Regex(
                $@"SigTokenBinding(?:\?|>)\s+{Regex.Escape(helperName)}\s*\([\s\S]*?new\s+SigTokenBinding\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*,",
                RegexOptions.CultureInvariant);
            var scopedMatch = scopedRegex.Match(source);
            if (scopedMatch.Success)
            {
                return scopedMatch;
            }
        }

        return SigTokenBindingCtorRegex().Match(source);
    }

    private static string ResolveActionFromHelper(
        string source,
        string helperName,
        Dictionary<string, string> constants,
        Dictionary<string, string> actionMethods)
    {
        var helperRegex = new Regex(
            $@"private\s+static\s+string\s+{Regex.Escape(helperName)}\s*\([^)]*\)\s*(?:=>|\{{)([\s\S]{{0,320}}?);",
            RegexOptions.CultureInvariant);
        var m = helperRegex.Match(source);
        if (m.Success)
        {
            string body = m.Groups[1].Value;
            foreach (var (methodName, tmpl) in actionMethods)
            {
                if (body.Contains(methodName + "(", StringComparison.Ordinal))
                {
                    return tmpl;
                }
            }

            foreach (var (constName, val) in constants)
            {
                if (body.Contains(constName, StringComparison.Ordinal))
                {
                    return val;
                }
            }
        }

        return string.Empty;
    }

    private static string StripQuotesAndInterpolation(string raw, Dictionary<string, string> constants)
    {
        string trimmed = raw.Trim();
        if (trimmed.StartsWith("$\"", StringComparison.Ordinal) && trimmed.EndsWith('"'))
        {
            string inner = trimmed[2..^1];
            foreach (var (key, val) in constants)
            {
                inner = inner.Replace($"{{{key}}}", val, StringComparison.Ordinal);
            }

            return inner;
        }

        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            return trimmed[1..^1];
        }

        return constants.GetValueOrDefault(trimmed, trimmed);
    }

    private static string NormalizeActionTemplate(string template) =>
        Regex.Replace(template, @"\{([A-Za-z0-9_]+):[^}]+\}", "{$1}");

    private static string NormalizeRouteParameters(string route) =>
        Regex.Replace(route, @"\{([A-Za-z0-9_]+):[^}]+\}", "{$1}");

    private static string NormalizeActionPathAgainstRoute(string actionTemplate, string route)
    {
        string normalized = NormalizeActionTemplate(actionTemplate);
        string[] routeSegments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] actionSegments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (routeSegments.Length != actionSegments.Length)
        {
            return normalized;
        }

        for (int i = 0; i < actionSegments.Length; i++)
        {
            if (actionSegments[i].StartsWith('{') && actionSegments[i].EndsWith('}') &&
                routeSegments[i].StartsWith('{') && routeSegments[i].EndsWith('}'))
            {
                actionSegments[i] = routeSegments[i];
            }
        }

        return "/" + string.Join('/', actionSegments);
    }

    private static string CombineRoute(string prefix, string suffix)
    {
        string p = prefix.Trim().TrimEnd('/');
        string s = suffix.Trim();
        if (s == "/" || s.Length == 0)
        {
            return p.Length == 0 ? "/" : p;
        }

        if (s.StartsWith("/api/", StringComparison.Ordinal))
        {
            return s.TrimEnd('/');
        }

        if (!s.StartsWith('/'))
        {
            s = "/" + s;
        }

        return p + s;
    }

    private static string ResolveDefaultGroupPrefix(string relativePath)
    {
        if (relativePath.StartsWith("apps/ctms/", StringComparison.Ordinal))
        {
            return "/api/v1/ctms";
        }

        if (relativePath.StartsWith("apps/notifications/", StringComparison.Ordinal))
        {
            return "/api/v1/notifications";
        }

        if (relativePath.StartsWith("apps/safety/", StringComparison.Ordinal))
        {
            return "/api/v1/safety";
        }

        if (relativePath.StartsWith("apps/execution/module/", StringComparison.Ordinal))
        {
            return "/api/v1/execution/forms";
        }

        if (relativePath.StartsWith("packages/kernel/", StringComparison.Ordinal))
        {
            return "/api/v1/kernel";
        }

        return string.Empty;
    }

    private static string ResolveHostFromRelativePath(string relativePath)
    {
        if (relativePath.StartsWith("apps/execution/module/", StringComparison.Ordinal) ||
            relativePath.StartsWith("apps/intelligence/", StringComparison.Ordinal) ||
            relativePath.StartsWith("apps/kernel-api/", StringComparison.Ordinal) ||
            relativePath.StartsWith("packages/kernel/", StringComparison.Ordinal))
        {
            return "kernel";
        }

        if (relativePath.StartsWith("apps/", StringComparison.Ordinal))
        {
            string rest = relativePath["apps/".Length..];
            int slash = rest.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                return rest[..slash];
            }
        }

        return relativePath;
    }

    private static string ResolveSchemaForDbContext(
        string repoRoot,
        string dbContext,
        string host,
        IReadOnlyList<DatabaseModuleSchema> dbModules)
    {
        var known = dbModules.FirstOrDefault(m => string.Equals(m.DbContext, dbContext, StringComparison.Ordinal));
        if (known is not null)
        {
            return known.Schema;
        }

        foreach (string file in EnumerateProductionCSharpFiles(repoRoot))
        {
            if (!Path.GetFileName(file).Equals(dbContext + ".cs", StringComparison.Ordinal))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            var m = HasDefaultSchemaRegex().Match(content);
            if (m.Success)
            {
                if (m.Groups[1].Success)
                {
                    return m.Groups[1].Value;
                }

                if (m.Groups[2].Success)
                {
                    var referenced = dbModules.FirstOrDefault(mod => string.Equals(mod.DbContext, m.Groups[2].Value, StringComparison.Ordinal));
                    if (referenced is not null)
                    {
                        return referenced.Schema;
                    }
                }
            }
        }

        return string.Equals(host, "execution", StringComparison.Ordinal) ? "edc" : host;
    }

    private static string ResolveSchemaFromModuleDoc(string fallbackSchema, string doc)
    {
        var defaultMatch = SchemaDefaultRegex().Match(doc);
        if (defaultMatch.Success && !string.Equals(defaultMatch.Groups[1].Value, "public", StringComparison.Ordinal))
        {
            return defaultMatch.Groups[1].Value;
        }

        var qualified = SchemaQualifiedTableRegex().Matches(doc)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .FirstOrDefault(s => !string.IsNullOrEmpty(s) && !string.Equals(s, "public", StringComparison.Ordinal));
        return qualified ?? fallbackSchema;
    }

    private static string ResolveHostFromModuleDoc(string fallbackHost, string doc)
    {
        var typeMatch = SchemaModelTypeRegex().Match(doc);
        if (typeMatch.Success)
        {
            string fullType = typeMatch.Groups[1].Value;
            if (fullType.StartsWith("Cadence.Kernel.", StringComparison.Ordinal) ||
                fullType.StartsWith("Cadence.Modules.", StringComparison.Ordinal))
            {
                return "kernel";
            }
        }

        return fallbackHost;
    }

    private static int ParseVitePort(string viteConfigPath, int defaultPort)
    {
        if (!File.Exists(viteConfigPath))
        {
            return defaultPort;
        }

        var m = ViteDevPortRegex().Match(File.ReadAllText(viteConfigPath));
        return m.Success && int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out int port)
            ? port
            : defaultPort;
    }

    private static (List<string> CoreBackends, List<string> CoreWebApps) ParseAppHostCoreProfile(string appHostSource)
    {
        string[] lines = appHostSource.ReplaceLineEndings("\n").Split('\n');
        var coreProjects = new List<string>();
        var coreWeb = new List<string>();
        int fullBlockDepth = 0;
        bool pendingIfFull = false;

        foreach (string rawLine in lines)
        {
            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (fullBlockDepth == 0 && Regex.IsMatch(trimmed, @"^if\s*\(\s*full\s*\)", RegexOptions.CultureInvariant))
            {
                pendingIfFull = true;
            }

            if (pendingIfFull || fullBlockDepth > 0)
            {
                int opens = trimmed.Count(c => c == '{');
                int closes = trimmed.Count(c => c == '}');
                if (pendingIfFull && opens > 0)
                {
                    fullBlockDepth += opens - closes;
                    pendingIfFull = false;
                }
                else if (!pendingIfFull)
                {
                    fullBlockDepth += opens - closes;
                    if (fullBlockDepth < 0)
                    {
                        fullBlockDepth = 0;
                    }
                }

                continue;
            }

            foreach (Match pm in AppHostAddProjectRegex().Matches(trimmed))
            {
                string name = pm.Groups[1].Value;
                if (!coreProjects.Contains(name, StringComparer.Ordinal))
                {
                    coreProjects.Add(name);
                }
            }

            foreach (Match wm in AppHostAddViteAppRegex().Matches(trimmed))
            {
                string name = wm.Groups[1].Value;
                if (!coreWeb.Contains(name, StringComparer.Ordinal))
                {
                    coreWeb.Add(name);
                }
            }
        }

        // Order core backends with gateway first when present, matching documentation order.
        if (coreProjects.Remove("gateway"))
        {
            coreProjects.Insert(0, "gateway");
        }

        return (coreProjects, coreWeb);
    }

    private static IEnumerable<string> EnumerateProductionCSharpFiles(string repoRoot)
    {
        foreach (string rootSub in new[] { "apps", "packages" })
        {
            string baseDir = Path.Combine(repoRoot, rootSub);
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(baseDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (rel.Contains("/bin/", StringComparison.Ordinal) ||
                    rel.Contains("/obj/", StringComparison.Ordinal) ||
                    rel.Contains("/Migrations/", StringComparison.Ordinal) ||
                    rel.Contains("/consumer-tests/", StringComparison.Ordinal) ||
                    rel.Contains("/benchmarks/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}
