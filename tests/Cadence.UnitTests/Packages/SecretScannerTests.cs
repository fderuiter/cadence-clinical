using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Verifies <see cref="SecretScanner"/> inspects frontend <c>.vue</c> templates and <c>scripts/</c> (including
/// <c>scripts/build-docs.js</c>) for private keys, cloud credentials, API tokens, and hardcoded compliance secrets.
/// </summary>
public sealed class SecretScannerTests : IDisposable
{
    private static readonly string OpenSshKeyHeader = "-----BEGIN " + "OPENSSH PRIVATE KEY-----";
    private static readonly string AwsAccessKey = "AKIA" + "IOSFODNN7EXAMPLE";
    private static readonly string GcpApiKey = "AIza" + "SyDaGmWKa4JsXZ-HjGw7ISLn_3namBGewQe";
    private static readonly string GitHubPat = "ghp_" + "0123456789abcdefghijklmnopqrstuvwxyz";
    private static readonly string SlackBotToken = "xoxb-" + "123456789012-abcdefghijkl";
    private static readonly string StripeLiveKey = "sk_live_" + "0123456789abcdefghijklmnop";
    private static readonly string AuditKeyAssignment = "process.env.AUDIT_LOG_SECRET_KEY = " + "\"hardcoded_super_secret_audit_key_val\";";

    private readonly string _dir = Directory.CreateTempSubdirectory("cadence-secret-scan-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ScanFiles_InspectsVueTemplatesAndBuildDocsScript()
    {
        string vuePath = Write("apps/web/src/components/Leak.vue", $"<template><div>{GcpApiKey}</div></template>\n");
        string scriptPath = Write("scripts/build-docs.js", $"{AuditKeyAssignment}\n");

        var findings = SecretScanner.ScanFiles(_dir, [vuePath, scriptPath]);

        findings.Should().Contain(f => f.FilePath == "apps/web/src/components/Leak.vue" && f.PatternName == "GCP API Key");
        findings.Should().Contain(f => f.FilePath == "scripts/build-docs.js" && f.PatternName == "Hardcoded Compliance Secret");
    }

    [Fact]
    public void ScanFiles_DetectsKeyHeadersCloudKeysAndServiceTokens_EvenWithExampleInLine()
    {
        string config = Write(
            "apps/gateway/src/Secrets.ts",
            string.Join('\n',
                $"const ssh = \"{OpenSshKeyHeader}\"; // example",
                $"const aws = \"{AwsAccessKey}\";",
                $"const gh = \"{GitHubPat}\";",
                $"const slack = \"{SlackBotToken}\";",
                $"const stripe = \"{StripeLiveKey}\";"));

        var findings = SecretScanner.ScanFiles(_dir, [config]);

        findings.Should().HaveCount(5);
        findings.Select(f => f.PatternName).Should().BeEquivalentTo(
            "Private Key Header",
            "AWS Access Key ID",
            "GitHub Token",
            "Slack Token",
            "Stripe Live Secret Key");
    }

    [Fact]
    public void ScanFiles_HonorsExplicitPragmaAllowlistInSourceOnly()
    {
        string allowed = Write("config/dev.env", $"const token = \"{GitHubPat}\"; // pragma: allowlist secret\n");

        SecretScanner.ScanFiles(_dir, [allowed]).Should().BeEmpty();
    }

    [Fact]
    public void BuildDocs_IsolatesComplianceSecretsToRtmEnv_AndPassesCleanAllowlistToVitePress()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string script = $$"""
            const mod = require('./scripts/build-docs.js');
            const dirtyEnv = {
              PATH: '/usr/bin',
              HOME: '/tmp',
              VITEPRESS_BASE: '/cadence-clinical/',
              ['AUDIT_LOG_' + 'SECRET_KEY']: 'ephemeral_test_value_1',
              ['INBOUND_EMAIL_' + 'HMAC_SECRET']: 'ephemeral_test_value_2',
              ['GATEWAY_' + 'SECRET_KEY']: 'ephemeral_test_value_3',
              ['AWS_SECRET_' + 'ACCESS_KEY']: 'ephemeral_test_value_4',
              ['GITHUB_' + 'TOKEN']: '{{GitHubPat}}'
            };
            const rtmEnv = mod.buildRtmEnv({ PATH: '/usr/bin' });
            const vpEnv = mod.buildVitePressEnv(dirtyEnv);
            console.log(JSON.stringify({
              hasProcessAudit: Boolean(process.env['AUDIT_LOG_' + 'SECRET_KEY']),
              rtmHasEphemeralKeys: rtmEnv.AUDIT_LOG_SECRET_KEY?.length === 64 && rtmEnv.INBOUND_EMAIL_HMAC_SECRET?.length === 64 && rtmEnv.GATEWAY_SECRET_KEY?.length === 64,
              vpKeys: Object.keys(vpEnv).sort()
            }));
            """;

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd().Trim();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, stderr);
        stdout.Should().Contain("\"hasProcessAudit\":false");
        stdout.Should().Contain("\"rtmHasEphemeralKeys\":true");
        stdout.Should().NotContain("AUDIT_LOG_SECRET_KEY");
        stdout.Should().NotContain("INBOUND_EMAIL_HMAC_SECRET");
        stdout.Should().NotContain("GATEWAY_SECRET_KEY");
        stdout.Should().NotContain("AWS_SECRET_ACCESS_KEY");
        stdout.Should().NotContain("GITHUB_TOKEN");
        stdout.Should().Contain("\"VITEPRESS_BASE\"");
    }

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
