using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services.Gates;

/// <summary>Service boundary, security/PHI scan, OpenAPI, schema doc, query, dependency, and demo manifest gates.</summary>
internal static class ContractAndCodeGates
{
    public static IReadOnlyList<QualityGate> Entries { get; } =
    [
        new("imports", ["validate-imports"], "No `using` of another service's namespace across `apps/<service>`",
            ctx => ImportValidatorService.ValidateImports(ctx.RepoRoot) == 0),
        new("security", ["audit-security"], "Hard-coded secret patterns across source, Vue templates and build scripts (private keys, cloud keys, API tokens, compliance secrets)",
            ctx => SecurityAuditorService.AuditSecurity(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("deid", ["deid-compliance-scan"],
            "PHI/PII in changed or tracked files including authored `docs/`, root portal docs, `.vue` templates and `stores/` (vendored `docs/CDISC/`, `docs/NCI/` and `docs/SDLC/runs/` excluded), ratcheted by `.deid-baseline.txt`; a line that is not PHI takes a `deid-ignore` comment",
            ctx => DeidComplianceService.ScanDeidCompliance(ctx.Files, ctx.RepoRoot, preCommit: ctx.PreCommit, indexReader: ctx.IndexReader, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        { TakesFiles = true },
        new("openapi", ["auto-stage-openapi"], "`docs/openapi` matches what the .NET services serve, and `docs/api` matches `docs/openapi`",
            ctx => PreCommitOpenApiService.ExecutePreCommitOpenApi(ctx.RepoRoot, quick: ctx.PreCommit, targetFiles: ctx.TargetFiles, indexReader: ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "exports specs only for the services the staged C# files touch, otherwise checks staged `docs/api` against staged `docs/openapi`",
        },
        new("api-clients", ["validate-api-clients"],
            "The web API types are generated from the current `docs/openapi`, and the web API clients typecheck against them",
            ctx => ApiClientValidatorService.ValidateApiClients(ctx.RepoRoot) == 0),
        new("schema-doc", ["auto-stage-schema-doc"],
            "`docs/schema` matches the EF Core models, and all 18 persistence modules pass snapshot, `.Designer.cs`, attribute, and DDL uniqueness checks",
            ctx => CheckSchemaDoc(ctx))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "runs the drift check when staged files include Entity Framework entities, migrations, configurations, or `docs/schema`",
        },
        new("path-patterns", ["validate-path-patterns"],
            "No `tmp`, `temp`, `scratch` or `old_src` directories, `.vue` files only under `apps/`, `packages/` and `docs/.vitepress/`, `.sh` files only under `scripts/`",
            ctx => PathPatternValidatorService.ValidatePathPatterns(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("query-linter", ["query-style-linter"], "No interpolated SQL string passed to `FromSqlRaw` or `ExecuteSqlRaw`, and no comparison with a boolean literal",
            ctx => QueryLinterService.LintQueries(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("vulnerabilities", ["validate-vulnerabilities"],
            "NuGet and pnpm dependency vulnerabilities not exempted by a valid `docs/SDLC/vulnerability_exclusions_ledger.json` entry (fails closed on missing or broken scanners)",
            ctx => VulnerabilityAuditorService.AuditVulnerabilities(ctx.RepoRoot) == 0),
        new("dependencies", ["validate-dependencies"],
            "Valid JSON across workspace `package.json` and `.config/dotnet-tools.json` manifests, no client-side crypto package (`node-forge`, `elliptic`, `jose`), and no test harness package (`axe-core`, `@axe-core/playwright`, `vitest`, `@vue/test-utils`, `jsdom`, `fake-indexeddb`, `msw`) in runtime `dependencies`",
            ctx => DependencyValidatorService.ValidateDependencies(ctx.RepoRoot) == 0),
        new("doc-index", ["validate-doc-index"], "Every Markdown document under `docs/` is listed in `docs/DOCUMENTATION_INDEX.md`",
            ctx => DocIndexSyncService.SyncDocIndex(check: true, repoRoot: ctx.RepoRoot) == 0),
        new("demo-manifest", ["validate-demo-manifest"],
            "`packages/demo-fixtures` matches the canonical demo seed in `packages/demo-fixtures/demo-seed.json`, `ExecutionDbSeeder.cs` and `docker/cadence-realm.json`",
            ctx => CheckDemoManifest(ctx))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "runs when staged files touch `packages/demo-fixtures`, `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs` or `docker/cadence-realm.json`, otherwise skips it",
        },
    ];

    private static bool CheckSchemaDoc(GateContext ctx)
    {
        if (ctx.TargetFiles is { Length: 0 })
        {
            return true;
        }

        if (ctx.PreCommit)
        {
            var files = ctx.TargetFiles ?? (IEnumerable<string>)PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot);
            if (!files.Any(AffectsSchemaDocGate))
            {
                Console.WriteLine("Staged changes do not include Entity Framework entities, migrations, or configurations. Skipping schema documentation check...");
                return true;
            }
        }
        else if (ctx.Files is not null && !ctx.Files.Any(AffectsSchemaDocGate))
        {
            return true;
        }

        return SchemaDocGeneratorService.GenerateSchemaDocumentation(
            validate: true,
            repoRoot: ctx.RepoRoot,
            preCommit: ctx.PreCommit,
            indexReader: ctx.IndexReader) == 0;
    }

    private static bool AffectsSchemaDocGate(string file)
    {
        if (PreCommitOpenApiService.AffectsEfCoreSchema(file))
        {
            return true;
        }

        string normalized = file.Replace('\\', '/');
        return normalized.StartsWith("docs/schema/", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckDemoManifest(GateContext ctx)
    {
        if (ctx.TargetFiles is { Length: 0 })
        {
            return true;
        }

        if (ctx.PreCommit)
        {
            var files = ctx.TargetFiles is not null
                ? (IEnumerable<string>)ctx.TargetFiles
                : PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot);

            if (!files.Any(f => AffectsDemoManifest(ctx.RepoRoot, f)))
            {
                Console.WriteLine("Staged changes do not touch demo seed or fixture inputs. Skipping demo manifest check...");
                return true;
            }

            var reader = GitIndexReader.ResolvePreCommitReader(ctx.RepoRoot, preCommit: true, ctx.IndexReader, out var ownedReader);
            using (ownedReader)
            {
                if (reader is { HasAuthoritativeIndex: true })
                {
                    using var stagedWorkspace = reader.Materialize(
                    [
                        DemoManifestGeneratorService.DemoSeedRelativePath,
                        DemoManifestGeneratorService.ExecutionSeedRelativePath,
                        DemoManifestGeneratorService.RealmRelativePath,
                        DemoManifestGeneratorService.ManifestJsRelativePath,
                        DemoManifestGeneratorService.ManifestDtsRelativePath,
                    ]);
                    return DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: stagedWorkspace.RootPath) == 0;
                }
            }
        }
        else if (ctx.Files is not null && !ctx.Files.Any(f => AffectsDemoManifest(ctx.RepoRoot, f)))
        {
            return true;
        }

        return DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: ctx.RepoRoot) == 0;
    }

    private static bool AffectsDemoManifest(string repoRoot, string file)
    {
        string rel = Path.IsPathRooted(file)
            ? Path.GetRelativePath(repoRoot, file)
            : file;
        string normalized = rel.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.StartsWith("packages/demo-fixtures/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.DemoSeedRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.KernelSeedRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.ExecutionSeedRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, DemoManifestGeneratorService.RealmRelativePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "packages/tooling_core/src/Services/DemoManifestGeneratorService.cs", StringComparison.OrdinalIgnoreCase);
    }
}
