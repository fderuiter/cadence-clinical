using System;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
    [GeneratedRegex(@"^\s*image:\s*(?<image>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ComposeImageLineRegex();

    [GeneratedRegex(@"^[^\s:@]+:[^\s@]+@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256PinnedImageRegex();

    [GeneratedRegex(@"public\s+const\s+int\s+Major\s*=\s*(?<major>\d+)\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresMajorConstRegex();

    [GeneratedRegex(@"public\s+const\s+string\s+Image\s*=\s*""(?<image>[^""]+)""\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresImageConstRegex();

    [GeneratedRegex(@"^postgres:(?<tag>(?<major>\d+)-[^\s@""']+)@sha256:(?<digest>[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresPinnedImageRegex();

    [GeneratedRegex(@"postgres:[0-9][^\s""']*", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresReferenceRegex();

    private static int ValidatePostgresDigestParity(RepoSnapshot snapshot, bool hasSolution)
    {
        const string sourcePath = "packages/testing/src/Persistence/PostgresTestImage.cs";
        const string composePath = "docker/docker-compose.yml";
        int errors = 0;

        if (snapshot.Exists(composePath))
        {
            errors += ValidateComposeImageDigests(snapshot, composePath);
        }

        if (!snapshot.Exists(sourcePath))
        {
            if (hasSolution || snapshot.Exists(composePath))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] Missing '{sourcePath}' required to verify PostgreSQL @sha256 image parity (ADR-2533).");
                return errors + 1;
            }

            return errors;
        }

        if (!TryReadCanonicalPostgresImage(snapshot, sourcePath, out string expectedImage, out string expectedTag, out string expectedDigest))
        {
            return errors + 1;
        }

        errors += ValidatePostgresImageInFile(snapshot, composePath, expectedImage, minMatches: 1, required: hasSolution);
        errors += ValidatePostgresImageInFile(snapshot, ".github/workflows/ci.yml", expectedImage, minMatches: 2, required: hasSolution);
        errors += ValidatePostgresImageInFile(snapshot, ".github/workflows/production-pipeline.yml", expectedImage, minMatches: 1, required: hasSolution);
        errors += ValidatePostgresImageInAppHost(snapshot, "apps/apphost/src/Program.cs", expectedTag, expectedDigest, required: hasSolution);
        return errors;
    }

    private static int ValidateComposeImageDigests(RepoSnapshot snapshot, string composePath)
    {
        if (!snapshot.TryReadBytes(composePath, out byte[] bytes, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {composePath}: {readError}");
            return 1;
        }

        var yamlErrors = YamlSyntaxValidatorService.ValidateBytes(bytes, composePath);
        if (yamlErrors.Count > 0)
        {
            foreach (string err in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {err}");
            }

            return yamlErrors.Count;
        }

        string compose = WorkspacePrimitives.StrictUtf8.GetString(WorkspacePrimitives.StripBom(bytes));
        int errors = 0;
        foreach (Match match in ComposeImageLineRegex().Matches(compose))
        {
            string image = YamlPrimitives.UnquoteScalar(match.Groups["image"].Value);
            if (!Sha256PinnedImageRegex().IsMatch(image))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {composePath} image '{image}' must pin both a tag and a 64-hex @sha256 digest.");
                errors++;
            }
        }

        return errors;
    }

    private static bool TryReadCanonicalPostgresImage(
        RepoSnapshot snapshot,
        string sourcePath,
        out string expectedImage,
        out string expectedTag,
        out string expectedDigest)
    {
        expectedImage = string.Empty;
        expectedTag = string.Empty;
        expectedDigest = string.Empty;

        if (!snapshot.TryReadText(sourcePath, out string source, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {sourcePath}: {readError}");
            return false;
        }

        var majorMatch = PostgresMajorConstRegex().Match(source);
        var imageMatch = PostgresImageConstRegex().Match(source);
        if (!majorMatch.Success || !imageMatch.Success)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {sourcePath} must declare 'Major' and digest-pinned 'Image' constants.");
            return false;
        }

        string major = majorMatch.Groups["major"].Value;
        expectedImage = imageMatch.Groups["image"].Value;
        var parsed = PostgresPinnedImageRegex().Match(expectedImage);
        if (!parsed.Success || !string.Equals(parsed.Groups["major"].Value, major, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {sourcePath} Image ('{expectedImage}') must match 'postgres:{major}-<variant>@sha256:<64hex>' (ADR-2533).");
            return false;
        }

        expectedTag = parsed.Groups["tag"].Value;
        expectedDigest = parsed.Groups["digest"].Value;
        return true;
    }

    private static int ValidatePostgresImageInFile(
        RepoSnapshot snapshot,
        string relativePath,
        string expectedImage,
        int minMatches,
        bool required)
    {
        if (!snapshot.Exists(relativePath))
        {
            if (!required)
            {
                return 0;
            }

            Console.WriteLine($"[DEPENDENCY VIOLATION] Missing '{relativePath}' required for PostgresTestImage.Image parity.");
            return 1;
        }

        if (!snapshot.TryReadText(relativePath, out string content, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        var matches = PostgresReferenceRegex().Matches(content).Select(m => m.Value).ToList();
        if (matches.Count < minMatches)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} must contain at least {minMatches} PostgreSQL image pin(s) matching '{expectedImage}' (found {matches.Count}).");
            return 1;
        }

        int errors = 0;
        foreach (string actual in matches.Where(m => !string.Equals(m, expectedImage, StringComparison.Ordinal)))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} PostgreSQL image '{actual}' drifts from PostgresTestImage.Image ('{expectedImage}'); synchronize PostgresTestImage.cs, docker-compose.yml, CI workflows, and AppHost together (ADR-2533).");
            errors++;
        }

        return errors;
    }

    private static int ValidatePostgresImageInAppHost(
        RepoSnapshot snapshot,
        string relativePath,
        string expectedTag,
        string expectedDigest,
        bool required)
    {
        if (!snapshot.Exists(relativePath))
        {
            if (!required)
            {
                return 0;
            }

            Console.WriteLine($"[DEPENDENCY VIOLATION] Missing '{relativePath}' required for PostgresTestImage.Image parity.");
            return 1;
        }

        if (!snapshot.TryReadText(relativePath, out string appHost, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        bool hasTag = appHost.Contains($".WithImageTag(\"{expectedTag}\")", StringComparison.Ordinal);
        bool hasDigest = appHost.Contains($".WithImageSHA256(\"{expectedDigest}\")", StringComparison.Ordinal);
        if (!hasTag || !hasDigest)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} must pin PostgreSQL with .WithImageTag(\"{expectedTag}\") and .WithImageSHA256(\"{expectedDigest}\") to match PostgresTestImage.Image (ADR-2533).");
            return 1;
        }

        return 0;
    }
}
