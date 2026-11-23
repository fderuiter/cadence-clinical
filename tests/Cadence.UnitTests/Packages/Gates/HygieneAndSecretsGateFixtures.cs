using System;
using System.Collections.Generic;

using Cadence.Cli.Services;

namespace Cadence.UnitTests.Packages.Gates;

internal static class HygieneAndSecretsGateFixtures
{
    public static Dictionary<string, Func<GateContext>> Create(QualityGateFixtureWorkspace ws) => new(StringComparer.Ordinal)
    {
        ["trailing-whitespace"] = () =>
        {
            ws.Write("docs/note.md", "# Note   \n");
            return ws.Context();
        },
        ["end-of-file"] = () =>
        {
            ws.Write("docs/note.md", "# Note without newline");
            return ws.Context();
        },
        ["clean-secrets"] = () =>
        {
            ws.Write(".secrets.baseline", "{ \"results\": ");
            return ws.Context();
        },
        ["detect-secrets"] = () =>
        {
            ws.Write(
                "baselines/secrets/config.json",
                "{\n  \"version\": \"1.5.0\",\n  \"plugins_used\": [{ \"name\": \"PrivateKeyDetector\" }],\n  \"filters_used\": []\n}\n");
            ws.CommitOnMainThenBranch();
            ws.Write("config/leak.txt", $"{QualityGateFixtureWorkspace.PrivateKeyHeader}\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC\n");
            ws.Git("add", "config/leak.txt");
            ws.Git("commit", "-q", "-m", "leak secret on branch");
            return ws.Context();
        },
        ["security"] = () =>
        {
            ws.Write("config/service.pem", $"{QualityGateFixtureWorkspace.PrivateKeyHeader}\nMIIB\n");
            return ws.Context();
        },
        ["deid"] = () =>
        {
            ws.Write("apps/alpha/src/Seed.cs", "var a = 1;\nvar ssn = \"000-12-3456\";\n");
            return ws.Context(files: ["apps/alpha/src/Seed.cs"]);
        },
        ["baseline-ratchet"] = () =>
        {
            ws.Write("tests/Arch/rules.baseline.txt", "# may only shrink\nK-XS-1|App|A\n");
            ws.CommitOnMainThenBranch();
            ws.Write("tests/Arch/rules.baseline.txt", "# may only shrink\nK-XS-1|App|A\nK-XS-1|App|B\n");
            ws.Git("commit", "-q", "-am", "grow the baseline");
            return ws.Context();
        },
    };
}
