using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Cadence.ToolingCore.Services;
using Cadence.Topology;

namespace Cadence.Cli.Services;

/// <summary>
/// Synthesizes Prettier-aligned Markdown tables and marker regions across portal and SDLC guides from authoritative
/// code metadata (<see cref="QualityGates"/>, <see cref="ServiceTopology"/>, <see cref="PortCheckerService"/>, Vite
/// configs, EF Core schema docs, <c>RequireElectronicSignature</c> endpoints, durable signature token stores, and
/// <c>GxPAuditInterceptor</c> host registrations). Rewritten by <c>cadence generate-cli-docs</c> and <c>cadence fix</c>;
/// checked fail-closed by the <c>cli-docs</c> quality gate.
/// </summary>
public static class GateDocs
{
    public sealed record GeneratedRegion(
        string RelativePath,
        string RegionId,
        string OpeningMarker,
        string ClosingMarker,
        Func<string, string> Render);

    /// <summary>The line that opens a generated quality-gate table.</summary>
    public const string BeginMarker = "<!-- quality-gates:begin (generated from packages/cli/src/Services/QualityGates.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";

    /// <summary>The line that closes a generated quality-gate table.</summary>
    public const string EndMarker = "<!-- quality-gates:end -->";

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

    public const string ArchitectureSignedProseBeginMarker = "<!-- architecture-signed-prose:begin (generated from RequireElectronicSignature and AddCadenceDurableSigTokenStore call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string ArchitectureSignedProseEndMarker = "<!-- architecture-signed-prose:end -->";

    public const string ArchitectureAuditHostsBeginMarker = "<!-- architecture-audit-hosts:begin (generated from GxPAuditInterceptor registrations by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string ArchitectureAuditHostsEndMarker = "<!-- architecture-audit-hosts:end -->";

    public const string ArchitectureAuditProseBeginMarker = "<!-- architecture-audit-prose:begin (generated from GxPAuditInterceptor registrations in CadenceDatabaseExtensions.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string ArchitectureAuditProseEndMarker = "<!-- architecture-audit-prose:end -->";

    public const string ClinicalCanvasTopologyProseBeginMarker = "<!-- clinical-canvas-topology-prose:begin (generated from ServiceTopology.cs, PersistenceModuleCatalog.cs, and RequireElectronicSignature call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string ClinicalCanvasTopologyProseEndMarker = "<!-- clinical-canvas-topology-prose:end -->";

    public const string DatabaseModulesBeginMarker = "<!-- database-module-schemas:begin (generated from docs/schema/README.md and PersistenceModuleCatalog.cs by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string DatabaseModulesEndMarker = "<!-- database-module-schemas:end -->";

    public const string SignedEndpointsBeginMarker = "<!-- signed-endpoints:begin (generated from RequireElectronicSignature call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string SignedEndpointsEndMarker = "<!-- signed-endpoints:end -->";

    public const string DurableSigStoresBeginMarker = "<!-- durable-sig-token-stores:begin (generated from AddCadenceDurableSigTokenStore call sites by `pnpm cadence generate-cli-docs`; do not edit) -->";
    public const string DurableSigStoresEndMarker = "<!-- durable-sig-token-stores:end -->";

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
        new("tickets", "Cadence.Apps.Tickets", "`TicketsDbContext`", "Features on its own schema", "Module, built out in the host (#5377)"),
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

    /// <summary>The documents that carry a generated quality-gate table, preserved for existing callers.</summary>
    public static IReadOnlyList<(string RelativePath, Func<string> Render)> Regions { get; } =
    [
        ("docs/cli.md", RenderCliTable),
        ("docs/CI.md", RenderCiTable),
    ];

    /// <summary>All synthesized documentation regions across portal, architecture, database, and SDLC guides.</summary>
    public static IReadOnlyList<GeneratedRegion> AllRegions { get; } =
    [
        new("docs/cli.md", "quality-gates", BeginMarker, EndMarker, _ => RenderCliTable()),
        new("docs/cli.md", "check-ports", CheckPortsBeginMarker, CheckPortsEndMarker, RenderCliCheckPortsRegion),
        new("docs/CI.md", "quality-gates", BeginMarker, EndMarker, _ => RenderCiTable()),
        new("docs/LOCAL_DEV_ENVIRONMENT.md", "dev-service-ports", DevServicePortsBeginMarker, DevServicePortsEndMarker, RenderLocalDevPortsTable),
        new("ARCHITECTURE.md", "running-services", RunningServicesBeginMarker, RunningServicesEndMarker, RenderArchitectureServicesTable),
        new("ARCHITECTURE.md", "runtime-components", RuntimeComponentsBeginMarker, RuntimeComponentsEndMarker, RenderArchitectureRuntimeComponentsTable),
        new("ARCHITECTURE.md", "architecture-signed-prose", ArchitectureSignedProseBeginMarker, ArchitectureSignedProseEndMarker, RenderArchitectureSignedProseRegion),
        new("ARCHITECTURE.md", "architecture-signatures", ArchitectureSignaturesBeginMarker, ArchitectureSignaturesEndMarker, RenderArchitectureSignaturesRegion),
        new("ARCHITECTURE.md", "architecture-audit-prose", ArchitectureAuditProseBeginMarker, ArchitectureAuditProseEndMarker, RenderArchitectureAuditProseRegion),
        new("ARCHITECTURE.md", "architecture-audit-hosts", ArchitectureAuditHostsBeginMarker, ArchitectureAuditHostsEndMarker, RenderArchitectureAuditHostsRegion),
        new("docs/ux/CLINICAL_CANVAS.md", "clinical-canvas-topology-prose", ClinicalCanvasTopologyProseBeginMarker, ClinicalCanvasTopologyProseEndMarker, RenderClinicalCanvasTopologyProseRegion),
        new("docs/OPERATIONAL_DATABASE.md", "database-module-schemas", DatabaseModulesBeginMarker, DatabaseModulesEndMarker, RenderOperationalDatabaseModulesTable),
        new("docs/SDLC/Signature_Token_Cryptographic_Contract.md", "signed-endpoints", SignedEndpointsBeginMarker, SignedEndpointsEndMarker, RenderSignatureContractEndpointsTable),
        new("docs/SDLC/Signature_Token_Cryptographic_Contract.md", "durable-sig-token-stores", DurableSigStoresBeginMarker, DurableSigStoresEndMarker, RenderSignatureContractStoresTable),
    ];

    /// <summary>The <c>docs/cli.md</c> table: names, whether a gate takes files, and what the pre-commit hook does.</summary>
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

    /// <summary>The <c>docs/CI.md</c> table: what each gate checks, preceded by the gate count.</summary>
    public static string RenderCiTable() =>
        $"`pnpm cadence check` runs these {QualityGates.All.Count} gates in this order:\n\n" +
        RenderTable(["Gate", "What it checks"], QualityGates.All.Select(g => new[] { $"`{g.Name}`", g.Checks }));

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

        return RenderTable(
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

        return RenderTable(
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

        return RenderTable(["Component", "Path", "Notes"], rows);
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §4 prose bullet summarizing step-up single-use tokens across signed routes and durable stores.</summary>
    public static string RenderArchitectureSignedProseRegion(string repoRoot)
    {
        var endpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        var stores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);
        string storeHosts = JoinOxfordList(
            stores.Select(s => string.Equals(s.Host, "kernel", StringComparison.Ordinal)
                ? $"`{s.Host}` via `{s.DbContext}`"
                : $"`{s.Host}`").ToList());

        return
            "- Signing a regulated action needs a fresh re-authentication: the gateway re-checks the signer's\n" +
            "  password with Keycloak and issues a 60-second `X-Sig-Token` bound to the user, resource and meaning\n" +
            "  ([ADR-2259](docs/adr/2026-09-29-electronic-signature-step-up-re-authentication.md)). Multi-target actions such as\n" + // deid-ignore
            "  delegation-log bulk sign-off also bind the token to the exact targets (`batch_id`). Each token signs once: every host\n" +
            $"  that maps any of the `{endpoints.Count.ToString(CultureInfo.InvariantCulture)}` `RequireElectronicSignature` endpoints registers `AddCadenceDurableSigTokenStore<TContext>()`\n" +
            $"  (`{stores.Count.ToString(CultureInfo.InvariantCulture)}` registrations across {storeHosts}, writing `consumed_signature_tokens` in its own schema) so single use holds across instances and restarts,\n" +
            "  and a host that maps a signed route without a durable store refuses to start outside Development (#5434)\n" +
            "  ([ADR-2515](docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md))."; // deid-ignore
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §4 electronic-signature routes and durable token-store summary.</summary>
    public static string RenderArchitectureSignaturesRegion(string repoRoot)
    {
        var endpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        var stores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);

        string storeSummary = string.Join(
            ", ",
            stores.Select(s => $"`{s.Host}` (`{s.DbContext}` → `{s.Table}`)"));

        string endpointTable = RenderTable(
            ["Host", "Endpoint", "`semantic_action`", "`action`"],
            endpoints.Select(FormatSignedEndpointRow));

        return
            $"Signature-gated routes (`{endpoints.Count.ToString(CultureInfo.InvariantCulture)}` `RequireElectronicSignature` endpoints across `{stores.Count.ToString(CultureInfo.InvariantCulture)}` durable `AddCadenceDurableSigTokenStore<TContext>()` registrations: {storeSummary}):\n\n" +
            endpointTable;
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §5 prose bullet describing default <c>GxPAuditInterceptor</c> registration and outbox flushing.</summary>
    public static string RenderArchitectureAuditProseRegion(string repoRoot)
    {
        var hosts = CodeMetadataCatalog.GetAuditInterceptorHosts(repoRoot);
        return
            "- **Audit:** `AddCadencePostgresDbContext<TDbContext>` (`packages/database/src/CadenceDatabaseExtensions.cs`) registers the EF Core\n" +
            $"  `GxPAuditInterceptor` (`packages/database/src/GxPAuditInterceptor.cs`) by default via `AddCadenceGxPAuditInterceptor` across all `{hosts.Count.ToString(CultureInfo.InvariantCulture)}` standalone\n" +
            "  database-backed service hosts (listed below; `designer` is stateless), while the `kernel` host writes its own\n" +
            "  command-pipeline audit chain (`kernel`). In `SavingChangesAsync`, `GxPAuditInterceptor` increments standalone\n" +
            "  concurrency tokens, flushes pending `IStatefulAggregate` domain events into `TransactionalOutboxMessage` rows in the\n" +
            "  same transaction when the context maps an outbox set, populates `CreatedAt`, `CreatedBy`, `ReasonForChange` and\n" +
            "  `VersionIndex` (21 CFR Part 11), and attributes every audited write to a real user or a named `svc:` service account\n" +
            "  ([ADR-2257](docs/adr/2026-09-29-attributable-audit-actors-and-named-service-accounts.md))."; // deid-ignore
    }

    /// <summary>The <c>ARCHITECTURE.md</c> §5 <c>GxPAuditInterceptor</c> host registrations table.</summary>
    public static string RenderArchitectureAuditHostsRegion(string repoRoot)
    {
        var hosts = CodeMetadataCatalog.GetAuditInterceptorHosts(repoRoot);
        return RenderTable(
            ["Host", "DbContext", "Schema", "Registration"],
            hosts.Select(h => new[]
            {
                $"`{h.Host}`",
                $"`{h.DbContext}`",
                $"`{h.Schema}`",
                $"`{h.RelativeFilePath}`",
            }));
    }

    /// <summary>The <c>docs/ux/CLINICAL_CANVAS.md</c> §8 topology, module-schema, and electronic-signature prose summary.</summary>
    public static string RenderClinicalCanvasTopologyProseRegion(string repoRoot)
    {
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        var dbModules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);
        var endpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        var stores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);

        int serviceCount = topology.Services.Count;
        int productServiceCount = topology.Services.Count(s => !string.Equals(s.Directory, "kernel-api", StringComparison.Ordinal));
        string[] kernelSchemas = dbModules
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
        if (kernelSchemas.Length == 0)
        {
            kernelSchemas = ["`kernel`", "`intelligence`", "`execution`"];
        }

        string kernelSchemasList = JoinOxfordList(kernelSchemas);
        string signedHostList = JoinOxfordList(
            stores.Select(s => $"`{s.Host}`").Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal).ToList());

        return
            $"`apps/web` is a Vue 3 client backed by the `.NET 10` service topology (`{serviceCount.ToString(CultureInfo.InvariantCulture)}` services — `{productServiceCount.ToString(CultureInfo.InvariantCulture)}` product services and `apps/kernel-api` — behind `apps/gateway` on port `{topology.GatewayPort.ToString(CultureInfo.InvariantCulture)}`;\n" +
            "epic #4833). Its API layer in `apps/web/src/api/` compiles against OpenAPI-generated TypeScript definitions\n" +
            "(`apps/web/scripts/generate-api-types.js` -> `apps/web/src/api/generated/<contract>.ts`, enforced by `cadence check api-clients`)\n" +
            "discovered automatically from every `docs/openapi/*_openapi.json` service and module contract (excluding the aggregated\n" +
            "`cdisc_openapi.json` snapshot; [docs/web/API_MIGRATION.md](../web/API_MIGRATION.md)). On the backend,\n" +
            $"`apps/kernel-api` composes `{kernelSchemas.Length.ToString(CultureInfo.InvariantCulture)}` EF Core module schemas ({kernelSchemasList}), and `{endpoints.Count.ToString(CultureInfo.InvariantCulture)}` mutating\n" +
            $"routes across `{stores.Count.ToString(CultureInfo.InvariantCulture)}` hosts ({signedHostList}) enforce 21 CFR Part 11 electronic\n" +
            "signatures via `.RequireElectronicSignature(...)` ([Signature Token Cryptographic Contract](../SDLC/Signature_Token_Cryptographic_Contract.md)).";
    }

    /// <summary>The <c>docs/OPERATIONAL_DATABASE.md</c> table synchronized with all 18 modules in <c>docs/schema/README.md</c> and <c>PersistenceModuleCatalog</c>.</summary>
    public static string RenderOperationalDatabaseModulesTable(string repoRoot)
    {
        var modules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);
        return RenderTable(
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
        return RenderTable(
            ["Host", "Endpoint", "`semantic_action`", "`action`"],
            endpoints.Select(FormatSignedEndpointRow));
    }

    /// <summary>The <c>docs/SDLC/Signature_Token_Cryptographic_Contract.md</c> §3 durable signature token stores table.</summary>
    public static string RenderSignatureContractStoresTable(string repoRoot)
    {
        var stores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);
        return RenderTable(
            ["Host", "DbContext", "Table", "Registration"],
            stores.Select(s => new[]
            {
                $"`{s.Host}`",
                $"`{s.DbContext}`",
                $"`{s.Table}`",
                $"`{s.RelativeFilePath}`",
            }));
    }

    /// <summary>Replaces every document's generated marker regions. Returns 1 when any target file or marker is missing.</summary>
    public static int Write(string repoRoot)
    {
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

            string text = File.ReadAllText(path).ReplaceLineEndings("\n");
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

            if (fileOk)
            {
                File.WriteAllText(path, text);
            }
        }

        return exitCode;
    }

    /// <summary>Returns <c>true</c> when every document's generated marker regions exist and match code metadata.</summary>
    public static bool Check(string repoRoot)
    {
        bool passed = true;
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
    /// Puts <paramref name="body"/> between <see cref="BeginMarker"/> and <see cref="EndMarker"/> in <paramref name="document"/>,
    /// or returns <c>null</c> when the markers are missing or out of order.
    /// </summary>
    public static string? ReplaceRegion(string document, string body) =>
        ReplaceRegion(document, BeginMarker, EndMarker, body);

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

    /// <summary>Renders a Markdown table with every column padded to its widest cell, as Prettier does.</summary>
    public static string RenderTable(IReadOnlyList<string> header, IEnumerable<string[]> rows)
    {
        var cells = rows.Prepend(header.ToArray()).Select(r => r.Select(Escape).ToArray()).ToList();
        int[] widths = Enumerable.Range(0, header.Count)
            .Select(i => Math.Max(3, cells.Max(r => r[i].Length)))
            .ToArray();

        var sb = new StringBuilder();
        AppendRow(sb, cells[0], widths);
        AppendRow(sb, widths.Select(w => new string('-', w)).ToArray(), widths);
        foreach (string[] row in cells.Skip(1))
        {
            AppendRow(sb, row, widths);
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

    private static void AppendRow(StringBuilder sb, string[] row, int[] widths)
    {
        sb.Append('|');
        for (int i = 0; i < row.Length; i++)
        {
            sb.Append(' ').Append(row[i].PadRight(widths[i])).Append(" |");
        }

        sb.Append('\n');
    }

    private static string JoinOxfordList(IReadOnlyList<string> items) =>
        items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };

    private static string Escape(string cell) => cell.Replace("|", "\\|", StringComparison.Ordinal);
}
