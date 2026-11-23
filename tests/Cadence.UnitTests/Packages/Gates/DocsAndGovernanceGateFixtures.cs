using System;
using System.Collections.Generic;

using Cadence.Cli.Services;

namespace Cadence.UnitTests.Packages.Gates;

internal static class DocsAndGovernanceGateFixtures
{
    public static Dictionary<string, Func<GateContext>> Create(QualityGateFixtureWorkspace ws) => new(StringComparer.Ordinal)
    {
        ["actionlint"] = () =>
        {
            ws.Write(
                ".github/workflows/broken.yml",
                "name: Broken\non: invalid_trigger_event\njobs:\n  bad:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo \"${{ invalid.context.value }}\"\n");
            return ws.Context();
        },
        ["hadolint"] = () =>
        {
            ws.Write("Dockerfile", "FROM ubuntu:24.04\nWORKDIR relative/path\nEXPOSE 99999\n");
            return ws.Context();
        },
        ["yaml"] = () =>
        {
            ws.Write(".github/workflows/broken.yml", "name: \"unclosed\njobs:\n\tbad: true\n");
            return ws.Context();
        },
        ["cli-docs"] = () =>
        {
            ws.Write(
                "docs/cli.md",
                "# Cadence Unified Developer CLI Reference\n\n`cadence doctor` `cadence check` `cadence fix` `cadence gxp`\n\n" +
                QualityGateFixtureWorkspace.Region(GateDocs.RenderCliTable()));
            ws.Write(
                "docs/CI.md",
                "# CI\n\n" + QualityGateFixtureWorkspace.Region("| Gate | What it checks |\n| --- | --- |\n| `stale` | A gate that no longer exists |\n"));
            return ws.Context();
        },
        ["adrs"] = () =>
        {
            ws.Write("docs/adr/use-a-thing.md", "# ADR-0001: Use a thing\n\nNo sections.\n");
            return ws.Context();
        },
        ["sanitizer"] = () =>
        {
            ws.Write("docs/requirements.md", "# Requirements\n\nCovers @req:prd-sys-001.\n");
            return ws.Context();
        },
        ["markdown"] = () =>
        {
            ws.Write("docs/guide.md", "# Guide\n\nSee [the missing page](missing-page.md).\n");
            return ws.Context();
        },
        ["readmes"] = () =>
        {
            ws.Write("apps/alpha/src/Program.cs", "class Program { }\n");
            return ws.Context();
        },
        ["codeowners"] = () =>
        {
            ws.Write(".github/CODEOWNERS", "* @fderuiter\n/nonexistent/path/ @fderuiter\n");
            return ws.Context();
        },
        ["workflow-security"] = () =>
        {
            ws.Write(
                ".github/workflows/ci.yml",
                "name: CI\non: push\npermissions:\n  contents: read\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v7\n");
            return ws.Context();
        },
        ["rulesets"] = () =>
        {
            ws.Write(".github/rulesets/main.json", """
                {
                  "name": "main-branch-protection",
                  "target": "branch",
                  "enforcement": "active",
                  "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
                  "rules": [
                    { "type": "deletion" },
                    { "type": "non_fast_forward" },
                    { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                    { "type": "required_status_checks", "parameters": { "strict_required_status_checks_policy": false, "required_status_checks": [{ "context": "Removed Legacy Check" }] } }
                  ]
                }
                """);
            ws.Write(".github/rulesets/release_branches.json", """
                {
                  "name": "release-branches-protection",
                  "target": "branch",
                  "enforcement": "active",
                  "conditions": { "ref_name": { "include": ["refs/heads/release/*"], "exclude": [] } },
                  "rules": [
                    { "type": "deletion" },
                    { "type": "non_fast_forward" },
                    { "type": "pull_request", "parameters": { "required_approving_review_count": 1, "require_code_owner_review": true } },
                    { "type": "required_status_checks", "parameters": { "strict_required_status_checks_policy": true, "required_status_checks": [{ "context": "CI Gate", "integration_id": 15368 }, { "context": "Check for Merge Conflicts", "integration_id": 15368 }] } }
                  ]
                }
                """);
            ws.Write(".github/rulesets/version_tags.json", """
                {
                  "name": "version-tags-protection",
                  "target": "tag",
                  "enforcement": "active",
                  "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
                  "rules": [{ "type": "deletion" }, { "type": "non_fast_forward" }]
                }
                """);
            ws.Write(
                ".github/workflows/ci.yml",
                "name: CI\non:\n  pull_request:\njobs:\n  unit-tests:\n    name: Unit Tests\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ok\n  ci-gate:\n    name: CI Gate\n    needs: [unit-tests]\n    if: always()\n    runs-on: ubuntu-latest\n    steps:\n      - env:\n          UNIT_RESULT: ${{ needs.unit-tests.result }}\n        run: |\n          if [ \"$UNIT_RESULT\" != \"success\" ]; then exit 1; fi\n");
            ws.Write(
                ".github/workflows/conflict-check.yml",
                "name: Conflict Check\non:\n  pull_request:\njobs:\n  conflict-check:\n    name: Check for Merge Conflicts\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ok\n");
            return ws.Context();
        },
        ["doc-index"] = () =>
        {
            ws.Write("docs/DOCUMENTATION_INDEX.md", "# Documentation index\n");
            ws.Write("docs/unlisted.md", "# Unlisted\n");
            return ws.Context();
        },
    };
}
