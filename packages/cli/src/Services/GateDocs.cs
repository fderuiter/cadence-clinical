using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Cadence.ToolingCore.Services;
using Cadence.Topology;

namespace Cadence.Cli.Services;

public sealed record GeneratedRegion(
    string RelativePath,
    string RegionId,
    string OpeningMarker,
    string ClosingMarker,
    Func<string, string> Render);

/// <summary>
/// Renders standalone <c>docs/cli-gates.md</c> from <see cref="QualityGates"/> using unpadded Markdown tables without
/// aggregate gate counts (so adding or modifying a gate produces a single-line diff), rejects reintroduced inline
/// <c>quality-gates:begin</c> blocks in <c>docs/cli.md</c> and <c>docs/CI.md</c>, and synthesizes code-metadata marker
/// regions across portal, architecture, database, and SDLC guides (<see cref="ServiceTopology"/>,
/// <see cref="PortCheckerService"/>, Vite configs, EF Core schema docs, <c>RequireElectronicSignature</c> endpoints,
/// durable signature token stores, and <c>GxPAuditInterceptor</c> host registrations).
/// </summary>
public static class GateDocs
{
    /// <summary>Standalone generated quality-gates reference path, relative to the repository root.</summary>
    public const string RelativePath = "docs/cli-gates.md";

    /// <summary>Prefix of the retired inline quality-gates marker block rejected in prose docs.</summary>
    public const string BeginMarkerPrefix = "<!-- quality-gates:begin";

    public const string CheckPortsBeginMarker = "<!-- check-ports:begin (generated from packages/tooling_core/src/Services/PortCheckerService.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string CheckPortsEndMarker = "<!-- check-ports:end -->";

    public const string DevServicePortsBeginMarker = "<!-- dev-service-ports:begin (generated from packages/topology/src/ServiceTopology.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string DevServicePortsEndMarker = "<!-- dev-service-ports:end -->";

    public const string RunningServicesBeginMarker = "<!-- running-services:begin (generated from packages/topology/src/ServiceTopology.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string RunningServicesEndMarker = "<!-- running-services:end -->";

    public const string RuntimeComponentsBeginMarker = "<!-- runtime-components:begin (generated from packages/topology/src/ServiceTopology.cs and apps/*/vite.config.js by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string RuntimeComponentsEndMarker = "<!-- runtime-components:end -->";

    public const string ArchitectureSignaturesBeginMarker = "<!-- architecture-signatures:begin (generated from RequireElectronicSignature and AddCadenceDurableSigTokenStore call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string ArchitectureSignaturesEndMarker = "<!-- architecture-signatures:end -->";

    public const string ArchitectureAuditHostsBeginMarker = "<!-- architecture-audit-hosts:begin (generated from GxPAuditInterceptor registrations by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string ArchitectureAuditHostsEndMarker = "<!-- architecture-audit-hosts:end -->";

    public const string DatabaseModulesBeginMarker = "<!-- database-module-schemas:begin (generated from docs/schema/README.md and PersistenceModuleCatalog.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string DatabaseModulesEndMarker = "<!-- database-module-schemas:end -->";

    public const string SignedEndpointsBeginMarker = "<!-- signed-endpoints:begin (generated from RequireElectronicSignature call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string SignedEndpointsEndMarker = "<!-- signed-endpoints:end -->";

    public const string DurableSigStoresBeginMarker = "<!-- durable-sig-token-stores:begin (generated from AddCadenceDurableSigTokenStore call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string DurableSigStoresEndMarker = "<!-- durable-sig-token-stores:end -->";

    /// <summary>Prose documents that previously carried inline quality-gate tables and now link to <see cref="RelativePath"/>.</summary>
    public static IReadOnlyList<string> LegacyEmbeddedPaths { get; } =
    [
        "docs/cli.md",
        "docs/CI.md",
    ];

    private sealed record ArchitectureServiceDescriptor(
        string Directory,
        string Project,
        string PersistenceOverride,
        string Today,
        string Disposition);

    private static readonly IReadOnlyList<ArchitectureServiceDescriptor> ArchitectureServiceOrder =
    [
        new("execution", "Cadence.Apps.Execution", "`ExecutionDbContext`", "Implemented (EDC core); eCRF forms and their queries moved to the Execution module in the kernel host (ADR-2622, #5461)", "Module (#5378)"),
        new("ctms", "Cadence.Apps.Ctms", "`CtmsDbContext`", "Features on its own schema", "Module (#5378)"),
        new("etmf", "Cadence.Apps.Etmf", "`EtmfDbContext`", "Features on its own schema", "Module (#5378); absorbs eisf and fileshare sharing"),
        new("payments", "Cadence.Apps.Payments", "`PaymentsDbContext`", "Features on its own schema", "Module (#5378)"),
        new("interop", "Cadence.Apps.Interop", "`InteropDbContext`", "Features on its own schema", "Module (#5378)"),
        new("imaging", "Cadence.Apps.Imaging", "`ImagingDbContext`", "Features on its own schema (no segmentation model; the route refuses, #5426)", "Module (#5378)"),
        new("safety", "Cadence.Apps.Safety", "`SafetyDbContext`", "Features on its own schema", "Module (#5378)"),
        new("econsent", "Cadence.Apps.Econsent", "`EconsentDbContext`", "Features on its own schema", "Module (#5378)"),
        new("knowledge", "Cadence.Apps.Knowledge", "`KnowledgeDbContext`", "Features on its own schema", "Module (#5378)"),
        new("designer", "Cadence.Apps.Designer", "none yet", "Scaffold", "Module (#5378)"),
        new("tickets", "Cadence.Apps.Tickets", "`TicketsDbContext`", "Scaffold", "Module, built out in the host (#5377)"),
        new("quality", "Cadence.Apps.Quality", "`QualityDbContext`", "Scaffold", "Module, built out in the host (#5376)"),
        new("org", "Cadence.Apps.Org", "`OrgDbContext`", "Scaffold", "Module over kernel Grants and Qualifications (#5375)"),
        new("eisf", "Cadence.Apps.Eisf", "`EisfDbContext`", "Scaffold", "Merges into etmf (#5371)"),
        new("fileshare", "Cadence.Apps.Fileshare", "`FileshareDbContext` + MinIO", "Scaffold (two background workers)", "Storage and conversion become a host capability; sharing merges into etmf (#5372)"),
        new("notifications", "Cadence.Apps.Notifications", "`NotificationsDbContext`", "Scaffold (one render route)", "Host capability (#5373)"),
        new(
            "kernel-api",
            "Cadence.Kernel.Host",
            "`KernelDbContext` (schema `kernel`); the Intelligence module's `IntelligenceDbContext` (schema `intelligence`) and the Execution module's `ExecutionModuleDbContext` (schema `execution`); both with Row-Level Security",
            "Canvas reads ([ADR-2540](docs/adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md)) and the observation commands (ADR-2620); decisions and visibility from grants (ADR-2455, ADR-2457); the Intelligence module ([apps/intelligence](apps/intelligence/README.md)): the governed AI agent runtime, its run ledger and controls, and the command-palette intent router ([ADR-2630](docs/adr/2026-10-20-ai-agents-act-through-proposals-in-one-governed-runtime.md)); the Execution module ([apps/execution/module](apps/execution/module/README.md)): eCRF forms over kernel form instances (ADR-2622)", // deid-ignore
            "The kernel, hosted in-process by the composition host (#5315)"),
    ];

    /// <summary>Synthesized non-gate documentation regions across portal, architecture, database, and SDLC guides.</summary>
    public static IReadOnlyList<GeneratedRegion> AllRegions { get; } =
    [
        new("docs/cli.md", "check-ports", CheckPortsBeginMarker, CheckPortsEndMarker, RenderCliCheckPortsRegion),
        new("docs/LOCAL_DEV_ENVIRONMENT.md", "dev-service-ports", DevServicePortsBeginMarker, DevServicePortsEndMarker, RenderLocalDevPortsTable),
        new("ARCHITECTURE.md", "running-services", RunningServicesBeginMarker, RunningServicesEndMarker, RenderArchitectureServicesTable),
        new("ARCHITECTURE.md", "runtime-components", RuntimeComponentsBeginMarker, RuntimeComponentsEndMarker, RenderArchitectureRuntimeComponentsTable),
        new("ARCHITECTURE.md", "architecture-signatures", ArchitectureSignaturesBeginMarker, ArchitectureSignaturesEndMarker, RenderArchitectureSignaturesRegion),
        new("ARCHITECTURE.md", "architecture-audit-hosts", ArchitectureAuditHostsBeginMarker, ArchitectureAuditHostsEndMarker, RenderArchitectureAuditHostsRegion),
        new("docs/OPERATIONAL_DATABASE.md", "database-module-schemas", DatabaseModulesBeginMarker, DatabaseModulesEndMarker, RenderOperationalDatabaseModulesTable),
        new("docs/SDLC/Signature_Token_Cryptographic_Contract.md", "signed-endpoints", SignedEndpointsBeginMarker, SignedEndpointsEndMarker, RenderSignatureContractEndpointsTable),
        new("docs/SDLC/Signature_Token_Cryptographic_Contract.md", "durable-sig-token-stores", DurableSigStoresBeginMarker, DurableSigStoresEndMarker, RenderSignatureContractStoresTable),
    ];

    /// <summary>Renders the CLI aliases, file-scoping, and pre-commit behaviour table without padding.</summary>
    public static string RenderCliTable() =>
        RenderTable(
            ["Gate", "Also accepted", "Takes `[files]`", "Pre-commit hook (`--pre-commit`)"],
            QualityGates.All.Select(g => new[]
            {
                $"`{g.Name}`",
                string.Join(", ", g.Aliases.Select(a => $"`{a}`")),
                g.TakesFiles ? "yes" : "",
                g.PreCommit switch
                {
                    PreCommitMode.Skipped => $"Skipped: {g.PreCommitNote}",
                    PreCommitMode.Quick => $"Quick: {g.PreCommitNote}",
                    _ => g.TakesFiles ? "Runs on the staged files" : "Runs",
                },
            }));

    /// <summary>Renders the gate checks table without padding or any aggregate gate count header.</summary>
    public static string RenderCiTable() =>
        RenderTable(["Gate", "What it checks"], QualityGates.All.Select(g => new[] { $"`{g.Name}`", g.Checks }));

    /// <summary>Renders the full standalone <c>docs/cli-gates.md</c> reference document.</summary>
    public static string RenderDocument() =>
        "# Quality Gates Reference\n\n" +
        "Generated from `packages/cli/src/Services/QualityGates.cs` by `pnpm cadence generate-cli-docs`. Don't edit this file by hand. " +
        "See [CLI Reference](cli.md#cadence-check) and [Continuous Integration](CI.md#quality-gates) for usage context.\n\n" +
        "## Gate Checks\n\n" +
        RenderCiTable() +
        "\n## CLI Aliases and Pre-Commit Modes\n\n" +
        RenderCliTable();

    /// <summary>The <c>docs/cli.md</c> port summary derived from <see cref="PortCheckerService.DefaultPorts"/>.</summary>
    public static string RenderCliCheckPortsRegion(string repoRoot)
    {
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        string formatted = PortCheckerService.FormatPortRanges(topology.CheckedPorts);
        return $"`check-ports` probes these {topology.CheckedPorts.Count.ToString(CultureInfo.InvariantCulture)} default ports on `127.0.0.1` (`PortCheckerService.DefaultPorts`): {formatted}.";
    }

    /// <summary>The <c>docs/LOCAL_DEV_ENVIRONMENT.md</c> service, port, and schema table.</summary>
    public static string RenderLocalDevPortsTable(string repoRoot)
    {
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        var dbModules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);
        string kernelSchemas = ResolveKernelSchemasDisplay(dbModules);

        var left = new List<(string Name, int Port, string Schema)>
        {
            ("front proxy (nginx) → gateway", topology.GatewayPort, "none"),
        };

        var orderedServices = topology.Services.OrderBy(s => s.Port).ToList();
        foreach (var svc in orderedServices.Take(9))
        {
            left.Add((FormatLocalDevServiceName(svc), svc.Port, ResolveServiceSchemaDisplay(svc, dbModules, kernelSchemas)));
        }

        left.Add(("PostgreSQL", topology.PostgresPort, "`cadence` (database)"));

        var right = new List<(string Name, int Port, string Schema)>();
        foreach (var svc in orderedServices.Skip(9))
        {
            right.Add((FormatLocalDevServiceName(svc), svc.Port, ResolveServiceSchemaDisplay(svc, dbModules, kernelSchemas)));
        }

        right.Add(("Keycloak", topology.KeycloakPort, "(own store)"));
        right.Add(("web (`apps/web`)", topology.WebPort, "none"));
        right.Add(("subject-portal (`apps/subject-portal`)", topology.SubjectPortalPort, "none"));

        int rowCount = Math.Max(left.Count, right.Count);
        var rows = new List<string[]>(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            (string Name, int Port, string Schema) l = i < left.Count ? left[i] : ("", 0, "");
            (string Name, int Port, string Schema) r = i < right.Count ? right[i] : ("", 0, "");
            rows.Add(
            [
                l.Name,
                l.Name.Length == 0 ? "" : l.Port.ToString(CultureInfo.InvariantCulture),
                l.Schema,
                r.Name,
                r.Name.Length == 0 ? "" : r.Port.ToString(CultureInfo.InvariantCulture),
                r.Schema,
            ]);
        }

        return RenderPaddedTable(
            ["Service", "Host port", "Database schema", "Service", "Host port", "Database schema"],
            rows);
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §3 running-services table derived from <see cref="ServiceTopology"/>.</summary>
    public static string RenderArchitectureServicesTable(string repoRoot)
    {
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        var byDirectory = topology.Services.ToDictionary(s => s.Directory, StringComparer.Ordinal);
        var descriptorsByDir = ArchitectureServiceOrder.ToDictionary(d => d.Directory, StringComparer.Ordinal);

        var rows = new List<string[]>
        {
            new[]
            {
                "[gateway](apps/gateway/README.md)",
                "`Cadence.Apps.Gateway`",
                $"{topology.GatewayPort.ToString(CultureInfo.InvariantCulture)} (behind the nginx front proxy in compose)",
                "all of the below; each route is tested against the endpoints its service maps ([ADR-2448](docs/adr/2026-09-29-gateway-routes-verified-against-service-endpoints.md)); a stalled service gets a 504 and an unhealthy one a 503 without forwarding ([ADR-2451](docs/adr/2026-09-29-gateway-timeouts-and-health-checked-clusters.md))",
                "none",
                "Implemented",
                "Stays: OIDC edge in front of the composition host",
            },
        };

        foreach (var descriptor in ArchitectureServiceOrder)
        {
            if (!byDirectory.TryGetValue(descriptor.Directory, out var svc))
            {
                continue;
            }

            rows.Add(
            [
                $"[{svc.Directory}](apps/{svc.Directory}/README.md)",
                $"`{descriptor.Project}`",
                svc.Port.ToString(CultureInfo.InvariantCulture),
                FormatArchitectureRoutes(svc),
                descriptor.PersistenceOverride,
                descriptor.Today,
                descriptor.Disposition,
            ]);
        }

        foreach (var extra in topology.Services.Where(s => !descriptorsByDir.ContainsKey(s.Directory)))
        {
            rows.Add(
            [
                $"[{extra.Directory}](apps/{extra.Directory}/README.md)",
                $"`Cadence.Apps.{char.ToUpperInvariant(extra.Name[0])}{extra.Name[1..]}`",
                extra.Port.ToString(CultureInfo.InvariantCulture),
                FormatArchitectureRoutes(extra),
                extra.Database ? $"`{char.ToUpperInvariant(extra.Name[0])}{extra.Name[1..]}DbContext`" : "none",
                "Implemented",
                "Module (#5378)",
            ]);
        }

        return RenderPaddedTable(
            ["Service", "Project", "Port", "Gateway routes (`/api/v1/…`)", "Persistence", "Today", "ADR-2610 disposition"],
            rows);
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §3 runtime-components table derived from topology, AppHost, and Vite configs.</summary>
    public static string RenderArchitectureRuntimeComponentsTable(string repoRoot)
    {
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        var dbModules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);
        int totalDotnetHosts = topology.Services.Count + 1;
        string coreHosts = string.Join(", ", topology.CoreBackendHosts.Select(h => $"`{h}`"));
        string schemaCountText = dbModules.Count > 0
            ? $"One `cadence` database partitioned across {dbModules.Count.ToString(CultureInfo.InvariantCulture)} EF Core module schemas (ADR-2447)"
            : "One `cadence` database with a schema per module (ADR-2447)";

        string[][] rows =
        [
            ["Web client", "`apps/web`", $"Vue 3 + Vite (port {topology.WebPort.ToString(CultureInfo.InvariantCulture)}, compose `web`); signs in through Keycloak"],
            ["Subject portal", "`apps/subject-portal`", $"Participant PWA (port {topology.SubjectPortalPort.ToString(CultureInfo.InvariantCulture)}, compose `subject-portal`)"],
            ["Aspire AppHost", "`apps/apphost`", $"Local orchestration of PostgreSQL (database `cadence`), MinIO, Keycloak, all {totalDotnetHosts.ToString(CultureInfo.InvariantCulture)} .NET hosts and the web apps; the `core` profile runs the {topology.CoreBackendHosts.Count.ToString(CultureInfo.InvariantCulture)} core backend hosts ({coreHosts}) and `web` only, optionally on an existing PostgreSQL and Keycloak"],
            ["Keycloak", "`docker/cadence-realm.json`", $"OIDC identity provider (port {topology.KeycloakPort.ToString(CultureInfo.InvariantCulture)}), realm `cadence`"],
            ["PostgreSQL 18", "compose `postgres`", schemaCountText],
            ["MinIO, ClamAV", "compose profile `storage`", "Object storage (fileshare, through `packages/storage`) and upload scanning (no service calls ClamAV yet)"],
            ["Front proxy", "compose `front-proxy` (nginx)", $"Port {topology.GatewayPort.ToString(CultureInfo.InvariantCulture)}; forwards the web apps' API calls to the gateway"],
            ["Other compose services", "`docker/docker-compose.yml`", "`dotnet-dev` (profile `dotnet` SDK container with isolated NuGet and MSBuild volumes)"],
        ];

        return RenderPaddedTable(["Component", "Path", "Notes"], rows);
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §4 electronic-signature routes and durable token-store summary.</summary>
    public static string RenderArchitectureSignaturesRegion(string repoRoot)
    {
        var endpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        var stores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);

        string storeSummary = string.Join(
            ", ",
            stores.Select(s => $"`{s.Host}` (`{s.DbContext}` → `{s.Table}`)"));

        string endpointTable = RenderPaddedTable(
            ["Host", "Endpoint", "`semantic_action`", "`action`"],
            endpoints.Select(FormatSignedEndpointRow));

        return
            $"Signature-gated routes (`{endpoints.Count.ToString(CultureInfo.InvariantCulture)}` `RequireElectronicSignature` endpoints across `{stores.Count.ToString(CultureInfo.InvariantCulture)}` durable `AddCadenceDurableSigTokenStore<TContext>()` registrations: {storeSummary}):\n\n" +
            endpointTable;
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §5 <c>GxPAuditInterceptor</c> host registrations table.</summary>
    public static string RenderArchitectureAuditHostsRegion(string repoRoot)
    {
        var hosts = CodeMetadataCatalog.GetAuditInterceptorHosts(repoRoot);
        return RenderPaddedTable(
            ["Host", "DbContext", "Schema", "Registration"],
            hosts.Select(h => new[]
            {
                $"`{h.Host}`",
                $"`{h.DbContext}`",
                $"`{h.Schema}`",
                $"`{h.RelativeFilePath}`",
            }));
    }

    /// <summary>The <c>docs/OPERATIONAL_DATABASE.md</c> table synchronized with all 18 modules in <c>docs/schema/README.md</c> and <c>PersistenceModuleCatalog</c>.</summary>
    public static string RenderOperationalDatabaseModulesTable(string repoRoot)
    {
        var modules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);
        return RenderPaddedTable(
            ["Module", "Host", "Schema", "DbContext", "Project"],
            modules.Select(m => new[]
            {
                $"[{m.Module}](schema/{m.Module}.md)",
                string.Equals(m.Host, "kernel", StringComparison.Ordinal) ? "`kernel` (`apps/kernel-api`)" : $"`{m.Host}`",
                $"`{m.Schema}`",
                $"`{m.DbContext}`",
                $"`{m.ProjectRelativePath}`",
            }));
    }

    /// <summary>The <c>docs/SDLC/Signature_Token_Cryptographic_Contract.md</c> §2 signature-gated endpoints table.</summary>
    public static string RenderSignatureContractEndpointsTable(string repoRoot)
    {
        var endpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        return RenderPaddedTable(
            ["Host", "Endpoint", "`semantic_action`", "`action`"],
            endpoints.Select(FormatSignedEndpointRow));
    }

    /// <summary>The <c>docs/SDLC/Signature_Token_Cryptographic_Contract.md</c> §3 durable signature token stores table.</summary>
    public static string RenderSignatureContractStoresTable(string repoRoot)
    {
        var stores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);
        return RenderPaddedTable(
            ["Host", "DbContext", "Table", "Registration"],
            stores.Select(s => new[]
            {
                $"`{s.Host}`",
                $"`{s.DbContext}`",
                $"`{s.Table}`",
                $"`{s.RelativeFilePath}`",
            }));
    }

    /// <summary>Writes standalone <c>docs/cli-gates.md</c> and updates synthesized marker regions when code metadata is present.</summary>
    public static int Write(string repoRoot)
    {
        string gatesPath = Path.Combine(repoRoot, RelativePath);
        string? gatesDir = Path.GetDirectoryName(gatesPath);
        if (!string.IsNullOrEmpty(gatesDir))
        {
            Directory.CreateDirectory(gatesDir);
        }

        File.WriteAllText(gatesPath, RenderDocument());

        if (!ShouldProcessSynthesizedRegions(repoRoot))
        {
            return 0;
        }

        int exitCode = 0;
        foreach (var group in AllRegions.GroupBy(r => r.RelativePath, StringComparer.Ordinal))
        {
            string path = Path.Combine(repoRoot, group.Key);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[ERROR] {group.Key} is missing and cannot receive generated documentation regions.");
                exitCode = 1;
                continue;
            }

            string originalText = File.ReadAllText(path);
            string normalizedOriginal = originalText.ReplaceLineEndings("\n");
            string text = normalizedOriginal;
            bool fileOk = true;
            foreach (var region in group)
            {
                string? next = ReplaceRegion(text, region.OpeningMarker, region.ClosingMarker, region.Render(repoRoot));
                if (next is null)
                {
                    Console.WriteLine($"[ERROR] {group.Key} has no '{region.RegionId}' marker region ('{region.OpeningMarker}' .. '{region.ClosingMarker}').");
                    exitCode = 1;
                    fileOk = false;
                    continue;
                }

                text = next;
            }

            if (fileOk && !string.Equals(text, normalizedOriginal, StringComparison.Ordinal))
            {
                File.WriteAllText(path, text);
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>docs/cli-gates.md</c> matches the registry, no prose doc embeds legacy
    /// <c>quality-gates:begin</c> markers, and (when repository topology/guide files are present) every synthesized
    /// marker region matches live code metadata.
    /// </summary>
    public static bool Check(string repoRoot)
    {
        bool passed = true;
        string gatesPath = Path.Combine(repoRoot, RelativePath);
        if (!File.Exists(gatesPath))
        {
            Console.WriteLine($"[ERROR] {RelativePath} not found; run 'pnpm cadence generate-cli-docs' to generate it.");
            passed = false;
        }
        else
        {
            string content = File.ReadAllText(gatesPath).ReplaceLineEndings("\n");
            string expected = RenderDocument();
            if (!string.Equals(content, expected, StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] {RelativePath} does not match packages/cli/src/Services/QualityGates.cs. Run 'pnpm cadence generate-cli-docs'.");
                passed = false;
            }
        }

        foreach (string legacyRelativePath in LegacyEmbeddedPaths)
        {
            string legacyPath = Path.Combine(repoRoot, legacyRelativePath);
            if (File.Exists(legacyPath) &&
                File.ReadAllText(legacyPath).Contains(BeginMarkerPrefix, StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] {legacyRelativePath} still embeds an inline '{BeginMarkerPrefix}' table; replace it with a link to cli-gates.md.");
                passed = false;
            }
        }

        if (!ShouldProcessSynthesizedRegions(repoRoot))
        {
            return passed;
        }

        foreach (var group in AllRegions.GroupBy(r => r.RelativePath, StringComparer.Ordinal))
        {
            string path = Path.Combine(repoRoot, group.Key);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[ERROR] {group.Key} not found; it must carry its generated documentation regions.");
                passed = false;
                continue;
            }

            string content = File.ReadAllText(path).ReplaceLineEndings("\n");
            foreach (var region in group)
            {
                string? expected = ReplaceRegion(content, region.OpeningMarker, region.ClosingMarker, region.Render(repoRoot));
                if (expected is null)
                {
                    Console.WriteLine($"[ERROR] {group.Key} has no generated '{region.RegionId}' region (the '{region.OpeningMarker}' and '{region.ClosingMarker}' lines).");
                    passed = false;
                }
                else if (!string.Equals(expected, content, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[ERROR] The '{region.RegionId}' generated region in {group.Key} is out of date. Run 'pnpm cadence generate-cli-docs'.");
                    passed = false;
                }
            }
        }

        return passed;
    }

    /// <summary>
    /// Puts <paramref name="body"/> between <paramref name="beginMarker"/> and <paramref name="endMarker"/> in
    /// <paramref name="document"/>, or returns <c>null</c> when the markers are missing or out of order.
    /// </summary>
    public static string? ReplaceRegion(string document, string beginMarker, string endMarker, string body)
    {
        string text = document.ReplaceLineEndings("\n");
        int begin = text.IndexOf(beginMarker + "\n", StringComparison.Ordinal);
        int end = text.IndexOf("\n" + endMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            return null;
        }

        int bodyStart = begin + beginMarker.Length + 1;
        return text[..bodyStart] + "\n" + body.TrimEnd('\n') + "\n" + text[end..];
    }

    /// <summary>Renders an unpadded Markdown table so single-row additions never rewrite adjacent rows.</summary>
    public static string RenderTable(IReadOnlyList<string> header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        AppendUnpaddedRow(sb, header.Select(Escape).ToArray());
        AppendUnpaddedRow(sb, Enumerable.Repeat("---", header.Count).ToArray());
        foreach (string[] row in rows)
        {
            AppendUnpaddedRow(sb, row.Select(Escape).ToArray());
        }

        return sb.ToString();
    }

    private static bool ShouldProcessSynthesizedRegions(string repoRoot) =>
        Directory.Exists(Path.Combine(repoRoot, "packages", "topology")) ||
        AllRegions.Any(r =>
            !string.Equals(r.RelativePath, "docs/cli.md", StringComparison.Ordinal) &&
            File.Exists(Path.Combine(repoRoot, r.RelativePath)));

    private static string RenderPaddedTable(IReadOnlyList<string> header, IEnumerable<string[]> rows)
    {
        string[] escapedHeader = header.Select(Escape).ToArray();
        List<string[]> escapedRows = rows.Select(r => r.Select(Escape).ToArray()).ToList();

        int[] widths = new int[escapedHeader.Length];
        for (int i = 0; i < widths.Length; i++)
        {
            widths[i] = Math.Max(3, escapedHeader[i].Length);
        }

        foreach (string[] row in escapedRows)
        {
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        var sb = new StringBuilder();
        AppendPaddedRow(sb, escapedHeader, widths);
        AppendPaddedRow(sb, widths.Select(w => new string('-', w)).ToArray(), widths);
        foreach (string[] row in escapedRows)
        {
            AppendPaddedRow(sb, row, widths);
        }

        return sb.ToString();
    }

    private static string[] FormatSignedEndpointRow(CodeMetadataCatalog.SignedEndpointMetadata endpoint)
    {
        string routeCell = endpoint.AliasRoute is { Length: > 0 } alias
            ? $"`{endpoint.Method} {endpoint.Route}` (alias `{alias}`)"
            : $"`{endpoint.Method} {endpoint.Route}`";
        string actionCell = endpoint.UsesBatchBinding
            ? $"`{endpoint.ActionTemplate}` (`batch_id` SHA-256)"
            : $"`{endpoint.ActionTemplate}`";
        return [$"`{endpoint.Host}`", routeCell, $"`{endpoint.SemanticAction}`", actionCell];
    }

    private static string FormatLocalDevServiceName(CadenceService service) =>
        string.Equals(service.Name, "kernel", StringComparison.Ordinal)
            ? $"kernel (`apps/{service.Directory}`)"
            : service.Name;

    private static string ResolveKernelSchemasDisplay(IReadOnlyList<CodeMetadataCatalog.DatabaseModuleSchema> dbModules)
    {
        string[] schemas = dbModules
            .Where(m => string.Equals(m.Host, "kernel", StringComparison.Ordinal))
            .Select(m => m.Schema)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s switch
            {
                "kernel" => 0,
                "intelligence" => 1,
                "execution" => 2,
                _ => 3,
            })
            .ThenBy(s => s, StringComparer.Ordinal)
            .Select(s => $"`{s}`")
            .ToArray();
        return schemas.Length > 0 ? string.Join(", ", schemas) : "`kernel`, `intelligence`, `execution`";
    }

    private static string ResolveServiceSchemaDisplay(
        CadenceService service,
        IReadOnlyList<CodeMetadataCatalog.DatabaseModuleSchema> dbModules,
        string kernelSchemas)
    {
        if (!service.Database)
        {
            return "none";
        }

        if (string.Equals(service.Name, "kernel", StringComparison.Ordinal))
        {
            return kernelSchemas;
        }

        var module = dbModules.FirstOrDefault(m => string.Equals(m.Module, service.Name, StringComparison.Ordinal));
        string schema = module?.Schema ?? (string.Equals(service.Name, "execution", StringComparison.Ordinal) ? "edc" : service.Name);
        return $"`{schema}`";
    }

    private static string FormatArchitectureRoutes(CadenceService service)
    {
        if (string.Equals(service.Directory, "kernel-api", StringComparison.Ordinal))
        {
            return string.Join(", ", service.RoutePrefixes.Select(prefix => prefix switch
            {
                "ai" => "`ai` (the Intelligence module)",
                "execution/forms" => "`execution/forms` (the Execution module)",
                _ => $"`{prefix}`",
            }));
        }

        return string.Join(", ", service.RoutePrefixes.Select(p => $"`{p}`"));
    }

    private static void AppendUnpaddedRow(StringBuilder sb, string[] row)
    {
        sb.Append('|');
        for (int i = 0; i < row.Length; i++)
        {
            if (row[i].Length == 0)
            {
                sb.Append(" |");
            }
            else
            {
                sb.Append(' ').Append(row[i]).Append(" |");
            }
        }

        sb.Append('\n');
    }

    private static void AppendPaddedRow(StringBuilder sb, string[] row, int[] widths)
    {
        sb.Append('|');
        for (int i = 0; i < widths.Length; i++)
        {
            sb.Append(' ').Append(row[i].PadRight(widths[i])).Append(" |");
        }

        sb.Append('\n');
    }

    private static string Escape(string cell) =>
        cell.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
}
