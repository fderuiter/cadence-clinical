using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using Cadence.Deid;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Verifies <see cref="PublicBundleValidatorService"/> (<c>check public-bundle</c>): fail-closed bundle presence,
/// zero comment or pragma exemptions, rejection of national identifiers, unapproved emails, secrets, external assets,
/// executable local backend URLs, and missing or permissive Content-Security-Policy meta directives.
/// </summary>
public sealed class PublicBundleValidatorServiceTests : IDisposable
{
    private const string ValidCsp =
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; media-src 'self'; worker-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'\">";

    private static readonly string PrivateKeyHeader = "-----BEGIN " + "PRIVATE KEY-----";
    private static readonly string GitHubPat = "ghp_" + "0123456789abcdefghijklmnopqrstuvwxyz";

    private readonly string _dir = Directory.CreateTempSubdirectory("cadence-public-bundle-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ValidatePublicBundles_FailsClosed_WhenAnyRequiredBundleIsMissing()
    {
        Write("docs/.vitepress/dist/index.html", CleanHtml());
        Write("apps/web/dist/index.html", CleanHtml());

        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1);
    }

    [Fact]
    public void ValidatePublicBundles_Passes_OnCleanDocsWebAndSubjectPortalBundles()
    {
        SeedCleanBundles();
        Write("docs/.vitepress/dist/assets/LOCAL_DEV_ENVIRONMENT.md.abc123.js", "export const doc = 'Dev gateway http://localhost:8000 in prose';\n");
        Write("docs/.vitepress/dist/assets/chunks/@localSearchIndexroot.abc123.js", "export const idx = 'http://127.0.0.1:5432';\n");
        Write("docs/.vitepress/dist/guide.html", $"<!doctype html><html><head>{ValidCsp}</head><body><pre><code>curl https://images.unsplash.com/example.png http://localhost:8000</code></pre></body></html>\n");
        string inlineScriptHash = Convert.ToBase64String(SHA256.HashData("window.__VP_HASH__=1;"u8));
        Write("docs/.vitepress/dist/blog/index.html", $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self' 'sha256-{inlineScriptHash}'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; media-src 'self'; worker-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'\"></head><body><script>window.__VP_HASH__=1;</script></body></html>\n");
        Write("docs/.vitepress/dist/demo/index.html", CleanHtml());
        Write("docs/.vitepress/dist/demo/assets/manifest.js", "export const persona = 'designer.lead@example.com';\n");
        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());
        Write("apps/web/dist/assets/consent-diagram.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>fderuiter@cadence.clinical</text></svg>\n");
        Write("apps/subject-portal/dist/assets/support.js", "export const supportEmail = 'coordinator@cadence-clinical.com';\n");

        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(0);
    }

    [Fact]
    public void ValidatePublicBundles_RejectsLocalhostUrlInStagedSubpathDemoChunk()
    {
        SeedCleanBundles();
        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());
        Write("docs/.vitepress/dist/subject-portal/assets/LOCAL_DEV_ENVIRONMENT.md.abc123.js", "export const api = 'http://localhost:8000';\n");

        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1);
    }

    [Fact]
    public void ScanBundleFile_IgnoresAllCommentAndPragmaExemptions()
    {
        SeedCleanBundles();
        Write(
            "apps/web/dist/assets/index.js",
            string.Join('\n',
                "const ssn = '000-12-3456'; // deid-ignore",
                "const mail = 'john.doe@example.com'; // deid: ignore",
                $"const key = '{PrivateKeyHeader}'; // pragma: allowlist secret",
                $"const pat = '{GitHubPat}'; // pragma: allowlist secret"));

        var findings = PublicBundleValidatorService.ScanBundleFile(
            "apps/web/dist/assets/index.js",
            "assets/index.js",
            File.ReadAllText(Path.Combine(_dir, "apps/web/dist/assets/index.js")),
            isDocsBundle: false);

        var categories = findings.Select(f => f.Category).ToArray();
        categories.Should().Contain(DetectorCategory.SsnNationalId);
        categories.Should().Contain("unapproved_email");
        categories.Should().Contain("secret");
        findings.Count(f => f.Category == "secret").Should().Be(2);
    }

    [Theory]
    [InlineData("<img src=\"https://images.unsplash.com/photo-1\">", "index.html", false)]
    [InlineData("<script src=\"https://cdn.jsdelivr.net/npm/vue@3\"></script>", "index.html", true)]
    [InlineData("<link rel=\"stylesheet\" href=\"//fonts.googleapis.com/css?family=Inter\">", "index.html", true)]
    public void ScanBundleFile_RejectsExternalHtmlAssets(string snippet, string fileName, bool isDocsBundle)
    {
        string html = $"<!doctype html><html><head>{ValidCsp}</head><body>{snippet}</body></html>";

        var findings = PublicBundleValidatorService.ScanBundleFile(fileName, fileName, html, isDocsBundle);

        findings.Select(f => f.Category).Should().Contain("external_asset");
    }

    [Fact]
    public void ScanBundleFile_RejectsExternalCssAndJsAssets()
    {
        var cssFindings = PublicBundleValidatorService.ScanBundleFile(
            "apps/web/dist/assets/app.css",
            "assets/app.css",
            ".hero { background-image: url('https://images.unsplash.com/photo-1'); }",
            isDocsBundle: false);

        var jsFindings = PublicBundleValidatorService.ScanBundleFile(
            "apps/web/dist/assets/app.js",
            "assets/app.js",
            "const ns='http://www.w3.org/2000/svg',diagram='https://images.unsplash.com/photo-1579684385127',remoteScript='https://evil-cdn.example/loader.js';",
            isDocsBundle: false);

        cssFindings.Select(f => f.Category).Should().Contain("external_asset");
        jsFindings.Count(f => f.Category == "external_asset").Should().Be(2);
    }

    [Fact]
    public void ScanBundleFile_RejectsExecutableLocalBackendUrlsInWebAndDocsRuntimeScripts()
    {
        var webFindings = PublicBundleValidatorService.ScanBundleFile(
            "apps/web/dist/assets/index.js",
            "assets/index.js",
            "const apiBase = 'http://localhost:8000/api';",
            isDocsBundle: false);

        var docsRuntimeFindings = PublicBundleValidatorService.ScanBundleFile(
            "docs/.vitepress/dist/assets/app.xyz.js",
            "assets/app.xyz.js",
            "fetch('http://127.0.0.1:8000/health');",
            isDocsBundle: true);

        var docsInlineScriptFindings = PublicBundleValidatorService.ScanBundleFile(
            "docs/.vitepress/dist/index.html",
            "index.html",
            $"<!doctype html><html><head>{ValidCsp}</head><body><script>fetch('http://0.0.0.0:8080');</script></body></html>",
            isDocsBundle: true);

        webFindings.Select(f => f.Category).Should().Contain("executable_local_url");
        docsRuntimeFindings.Select(f => f.Category).Should().Contain("executable_local_url");
        docsInlineScriptFindings.Select(f => f.Category).Should().Contain("executable_local_url");
    }

    [Theory]
    [InlineData("<!doctype html><html><head><title>No CSP</title></head><body></body></html>")]
    [InlineData("<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self' https:; media-src 'self'; worker-src 'self'; connect-src 'self'\"></head><body></body></html>")]
    [InlineData("<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self' 'unsafe-eval'; media-src 'self'; worker-src 'self'; connect-src 'self'\"></head><body></body></html>")]
    [InlineData("<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'; script-src 'self'\"></head><body></body></html>")]
    public void ScanBundleFile_RejectsMissingOrPermissiveCspHeaders(string html)
    {
        var findings = PublicBundleValidatorService.ScanBundleFile("apps/web/dist/index.html", "index.html", html, isDocsBundle: false);

        findings.Select(f => f.Category).Should().Contain("csp_policy");
    }

    [Fact]
    public void ScanBundleFile_HandlesLargePunctuatedPayloadsAndUrlEmbeddedEmailsLinearly()
    {
        string densePayload = string.Concat(Enumerable.Repeat("abc+def-ghi.jkl%20", 12_000));
        string html = $"<!doctype html><html><head>{ValidCsp}</head><body><pre><code>{densePayload}</code></pre><p>mailto:https://leak.user@unapproved-domain.org/profile</p></body></html>";

        var findings = PublicBundleValidatorService.ScanBundleFile("docs/.vitepress/dist/spec.html", "spec.html", html, isDocsBundle: true);

        findings.Select(f => f.Category).Should().Contain("unapproved_email");
    }

    [Fact]
    public void ValidatePublicBundles_RejectsAxeCoreContaminationAcrossAppAndStagedBundles()
    {
        SeedCleanBundles();
        Write("apps/web/dist/assets/axe-B12345.js", "export const audit = () => {};\n");
        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1);

        var inlineAxeFindings = PublicBundleValidatorService.ScanBundleFile(
            "apps/web/dist/assets/vendor.js",
            "assets/vendor.js",
            "window.axe={};axe._audit={};axe._tree=[];",
            isDocsBundle: false);
        inlineAxeFindings.Select(f => f.Category).Should().Contain("bundle_contamination");
    }

    [Fact]
    public void ValidatePublicBundles_RejectsKeycloakInStagedDemosWhileAllowingNonDemoApps()
    {
        SeedCleanBundles();
        Write("apps/web/dist/assets/vendor-auth-abc123.js", "export const kc = 'KeycloakAdapter';\n");
        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(0,
            "non-demo production builds legitimately include keycloak-js for PKCE S256 login");

        Write("docs/.vitepress/dist/demo/index.html", CleanHtml());
        Write("docs/.vitepress/dist/demo/assets/vendor-auth-abc123.js", "export const kc = 1;\n");
        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1);
    }

    [Fact]
    public void ValidatePublicBundles_RejectsKeycloakInSubjectPortalStagedDemoAndForbiddenPreloads()
    {
        SeedCleanBundles();
        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());
        Write("docs/.vitepress/dist/subject-portal/assets/keycloak-xyz987.js", "export default function Keycloak() {}\n");
        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1);

        var preloadFindings = PublicBundleValidatorService.ScanBundleFile(
            "docs/.vitepress/dist/demo/index.html",
            "demo/index.html",
            $"<!doctype html><html><head>{ValidCsp}<link rel=\"modulepreload\" href=\"/cadence-clinical/demo/assets/vendor-media-123.js\"></head><body></body></html>",
            isDocsBundle: false,
            isDemoBundle: true);
        preloadFindings.Select(f => f.Category).Should().Contain("bundle_preload");
    }

    [Fact]
    public void ValidatePublicBundles_EnforcesStagedDemoColdBootAndSubjectPortalByteBudgets()
    {
        SeedCleanBundles();
        Write(
            "docs/.vitepress/dist/demo/index.html",
            $"<!doctype html><html><head>{ValidCsp}<link rel=\"modulepreload\" href=\"/cadence-clinical/demo/assets/vendor-big.js\"></head><body><script type=\"module\" src=\"/cadence-clinical/demo/assets/index.js\"></script></body></html>\n");
        Write("docs/.vitepress/dist/demo/assets/index.js", "console.log('boot');\n");
        Write("docs/.vitepress/dist/demo/assets/vendor-big.js", new string('a', (int)PublicBundleValidatorService.MaxDemoColdBootRawBytes));

        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1,
            "staged /demo/ cold-boot raw bytes over 1.35 MiB must fail closed");

        File.Delete(Path.Combine(_dir, "docs/.vitepress/dist/demo/assets/vendor-big.js"));
        Write("docs/.vitepress/dist/demo/index.html", CleanHtml());
        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());
        Write("docs/.vitepress/dist/subject-portal/assets/index.js", new string('b', (int)PublicBundleValidatorService.MaxSubjectPortalAssetsRawBytes));

        PublicBundleValidatorService.ValidatePublicBundles(_dir).Should().Be(1,
            "staged /subject-portal/assets/ volume over 920 KiB must fail closed");
    }

    [Fact]
    public void ValidatePublicBundles_FailsClosed_WhenDocsBundleIsMissingStagedDemoOrSubjectPortalEntrypoints()
    {
        Write("docs/.vitepress/dist/index.html", CleanHtml());
        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());

        PublicBundleValidatorService.ValidatePublicBundles(_dir, ["docs/.vitepress/dist"]).Should().Be(1,
            "docs/.vitepress/dist must fail closed when staged demo/index.html is missing");

        Write("docs/.vitepress/dist/demo/index.html", CleanHtml());
        File.Delete(Path.Combine(_dir, "docs/.vitepress/dist/subject-portal/index.html"));

        PublicBundleValidatorService.ValidatePublicBundles(_dir, ["docs/.vitepress/dist"]).Should().Be(1,
            "docs/.vitepress/dist must fail closed when staged subject-portal/index.html is missing");

        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());
        PublicBundleValidatorService.ValidatePublicBundles(_dir, ["docs/.vitepress/dist"]).Should().Be(0);
    }

    [Fact]
    public void ValidatePublicBundles_ScopesToExplicitTargetBundles_AndTreatsDistDemoAsDemoBundle()
    {
        Write("apps/web/dist/index.html", CleanHtml());
        Write("apps/subject-portal/dist/index.html", CleanHtml());

        PublicBundleValidatorService.ValidatePublicBundles(_dir, ["apps/web/dist", "apps/subject-portal/dist"]).Should().Be(0,
            "explicit app bundle targets should pass without requiring docs/.vitepress/dist");

        PublicBundleValidatorService.ValidatePublicBundles(_dir, ["apps/web/dist", "docs/.vitepress/dist"]).Should().Be(1,
            "missing explicit target bundle directory must fail closed");

        Write("apps/web/dist-demo/index.html", CleanHtml());
        Write("apps/web/dist-demo/assets/vendor-auth-abc123.js", "export const kc = 1;\n");
        PublicBundleValidatorService.ValidatePublicBundles(_dir, ["apps/web/dist-demo"]).Should().Be(1,
            "isolated dist-demo directories must enforce standalone demo bundle contamination checks");
    }

    private void SeedCleanBundles()
    {
        Write("docs/.vitepress/dist/index.html", CleanHtml());
        Write("docs/.vitepress/dist/demo/index.html", CleanHtml());
        Write("docs/.vitepress/dist/subject-portal/index.html", CleanHtml());
        Write("apps/web/dist/index.html", CleanHtml());
        Write("apps/subject-portal/dist/index.html", CleanHtml());
    }

    private static string CleanHtml() =>
        $"<!doctype html><html><head>{ValidCsp}</head><body><script src=\"/assets/app.js\"></script></body></html>\n";

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
