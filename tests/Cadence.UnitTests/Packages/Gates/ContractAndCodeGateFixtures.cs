using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;

namespace Cadence.UnitTests.Packages.Gates;

internal static class ContractAndCodeGateFixtures
{
    public static Dictionary<string, Func<GateContext>> Create(QualityGateFixtureWorkspace ws) => new(StringComparer.Ordinal)
    {
        ["eslint"] = () => ws.Context(),
        ["duplication"] = () =>
        {
            string body = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"        int value{i} = Compute({i}, {i * 7});"));
            ws.Write("apps/alpha/src/One.cs", $"class One\n{{\n    void Run()\n    {{\n{body}\n    }}\n}}\n");
            ws.Write("apps/beta/src/Two.cs", $"class Two\n{{\n    void Go()\n    {{\n{body}\n    }}\n}}\n");
            return ws.Context();
        },
        ["arch-drift"] = () =>
        {
            ws.Write("docker/docker-compose.yml", "name: cadence\n");
            return ws.Context();
        },
        ["env-vars"] = () =>
        {
            ws.Write("apps/alpha/src/Settings.cs", "class Settings { string? V = System.Environment.GetEnvironmentVariable(\"FIXTURE_UNDOCUMENTED\"); }\n");
            ws.Write(".env.example", "# no variables\n");
            ws.Write("docs/ENVIRONMENT_VARIABLES.md", "# Environment variables\n");
            return ws.Context();
        },
        ["dev-env"] = () =>
        {
            // The port comparisons themselves are covered by DevEnvironmentValidatorServiceTests.
            ws.Write("apps/gateway/src/Program.cs", "class Program { }\n");
            return ws.Context();
        },
        ["imports"] = () =>
        {
            ws.Write("apps/alpha/src/Uses.cs", "using Cadence.Apps.Beta.Domain;\n\nclass Uses { }\n");
            return ws.Context();
        },
        ["openapi"] = () =>
        {
            // The pre-commit variant with no staged C#: docs/api is compared with docs/openapi, which serves nothing.
            Directory.CreateDirectory(Path.Combine(ws.Root, "docs", "openapi"));
            ws.Write("docs/api/retired.md", "# Retired service\n");
            return ws.Context(files: ["docs/api/retired.md"], preCommit: true);
        },
        ["api-clients"] = () => ws.Context(),
        ["schema-doc"] = () => ws.Context(),
        ["path-patterns"] = () =>
        {
            ws.Write("scratch/notes.txt", "left behind\n");
            return ws.Context();
        },
        ["query-linter"] = () =>
        {
            ws.Write("apps/alpha/src/Filter.cs", $"class Filter {{ bool Keep(bool flag) => flag {QualityGateFixtureWorkspace.BooleanComparison}; }}\n");
            return ws.Context();
        },
        ["dependencies"] = () =>
        {
            ws.Write("apps/web/package.json", """{ "dependencies": { "node-forge": "1.3.1" } }""");
            return ws.Context();
        },
        ["demo-manifest"] = () =>
        {
            ws.Write("packages/demo-fixtures/src/manifest.js", "export const DEMO_MANIFEST = {};\n");
            return ws.Context();
        },
        ["rtm"] = () =>
        {
            ws.Write(
                "tests/Fixture/FixtureTests.cs",
                $"public class FixtureTests\n{{\n    [Fact]\n    {QualityGateFixtureWorkspace.RequirementAttribute}\n    public void Proves() {{ }}\n}}\n");
            return ws.Context();
        },
        ["gxp-snapshot"] = () =>
        {
            ws.Write("docs/SDLC/Requirements_Traceability_Matrix.md", "# RTM\n");
            ws.CommitOnMainThenBranch();
            ws.Write("docs/SDLC/Requirements_Traceability_Matrix.md", "# RTM regenerated on a branch\n");
            ws.Git("commit", "-q", "-am", "regenerate the RTM");
            return ws.Context();
        },
    };
}
