using System;
using System.Collections.Generic;

using Cadence.Cli.Services;

namespace Cadence.UnitTests.Packages.Gates;

internal static class ReleaseAndTestSuiteGateFixtures
{
    public static Dictionary<string, Func<GateContext>> Create(QualityGateFixtureWorkspace ws) => new(StringComparer.Ordinal)
    {
        ["vulnerabilities"] = () =>
        {
            // An exemption with an out-of-range score fails before any package manager is asked.
            ws.Write(
                "docs/SDLC/vulnerability_exclusions_ledger.json",
                """[{ "vulnerability_id": "GHSA-0000", "package_name": "pkg", "severity": 9, "occurrence": 1, "detectability": 1, "rpn": 9, "justification": "j", "status": "Approved" }]""");
            return ws.Context();
        },
        ["merge-readiness"] = () =>
        {
            ws.Write("apps/shared.cs", "class Shared { int Value = 1; }\n");
            ws.CommitOnMainThenBranch();
            ws.Write("apps/shared.cs", "class Shared { int Value = 2; }\n");
            ws.Git("commit", "-q", "-am", "feature change");
            ws.Git("checkout", "-q", "main");
            ws.Write("apps/shared.cs", "class Shared { int Value = 3; }\n");
            ws.Git("commit", "-q", "-am", "upstream change");
            ws.Git("update-ref", "refs/remotes/origin/main", "HEAD");
            ws.Git("checkout", "-q", "feature");
            return ws.Context();
        },
        ["public-bundle"] = () =>
        {
            const string validCsp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self'; media-src 'self'; worker-src 'self'; connect-src 'self'\">";
            ws.Write("docs/.vitepress/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><p>Portal</p></body></html>\n");
            ws.Write("apps/subject-portal/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><p>Participant Portal</p></body></html>\n");
            ws.Write("apps/web/dist/index.html", $"<!doctype html><html><head>{validCsp}</head><body><img src=\"https://images.unsplash.com/photo-1\"></body></html>\n");
            return ws.Context();
        },
        ["branch-freshness"] = () =>
        {
            ws.Write("README.md", "# Cadence\n");
            ws.CommitOnMainThenBranch();
            ws.Git("checkout", "-q", "main");
            ws.Write("README.md", "# Cadence advanced on main\n");
            ws.Git("commit", "-q", "-am", "advance main");
            ws.Git("update-ref", "refs/remotes/origin/main", "HEAD");
            ws.Git("checkout", "-q", "feature");
            return ws.Context();
        },
        ["architecture-tests"] = () =>
        {
            ws.WriteBrokenTestProject("tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj");
            return ws.Context();
        },
        ["unit-tests"] = () =>
        {
            ws.WriteBrokenTestProject("tests/Cadence.UnitTests/Cadence.UnitTests.csproj");
            return ws.Context();
        },
    };
}
