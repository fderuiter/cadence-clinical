using System;
using System.Collections.Generic;

using Cadence.Security;

namespace Cadence.Deid;

public static class RedactionManifestBuilder
{
    public static RedactionManifest BuildRedactionManifest(
        List<RedactionRecordItem> redactionRecord,
        string operatorIdentity,
        string reason,
        string sourceVersion,
        string targetVersion,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(redactionRecord);

        var categoriesCounts = new Dictionary<string, int>();
        var strategies = new Dictionary<string, string>();

        foreach (var item in redactionRecord)
        {
            categoriesCounts[item.Category] = categoriesCounts.GetValueOrDefault(item.Category, 0) + 1;
            strategies[item.Category] = item.Strategy;
        }

        return new RedactionManifest
        {
            CategoriesCounts = categoriesCounts,
            Strategies = strategies,
            OperatorIdentity = operatorIdentity,
            Reason = reason,
            Timestamp = recordedAt.UtcDateTime.ToString("o"),
            SourceVersion = sourceVersion,
            TargetVersion = targetVersion
        };
    }

    private static object CreatePayloadWithoutSignature(RedactionManifest manifest)
    {
        return new Dictionary<string, object?>
        {
            ["categories_counts"] = manifest.CategoriesCounts,
            ["strategies"] = manifest.Strategies,
            ["operator_identity"] = manifest.OperatorIdentity,
            ["reason"] = manifest.Reason,
            ["timestamp"] = manifest.Timestamp,
            ["source_version"] = manifest.SourceVersion,
            ["target_version"] = manifest.TargetVersion
        };
    }

    public static RedactionManifest SignManifestSymmetric(RedactionManifest manifest, byte[] secretKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(secretKey);

        var payload = CreatePayloadWithoutSignature(manifest);
        manifest.Signature = CanonicalSigner.GenerateCanonicalSignature(payload, secretKey);
        return manifest;
    }

    public static bool VerifyManifestSymmetric(RedactionManifest manifest, byte[] secretKey)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Signature) || secretKey == null || secretKey.Length == 0)
        {
            return false;
        }

        var payload = CreatePayloadWithoutSignature(manifest);
        return CanonicalSigner.VerifyCanonicalSignature(payload, manifest.Signature, secretKey);
    }

    public static RedactionManifest SignManifestAsymmetric(RedactionManifest manifest, string privateKeyPem, byte[]? password = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        var payload = CreatePayloadWithoutSignature(manifest);
        byte[] data = CanonicalSigner.CanonicalSerialize(payload);
        manifest.Signature = AsymmetricSigner.AsymmetricSign(data, privateKeyPem, password);
        return manifest;
    }

    public static bool VerifyManifestAsymmetric(RedactionManifest manifest, string publicKeyOrCertPem)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Signature) || string.IsNullOrWhiteSpace(publicKeyOrCertPem))
        {
            return false;
        }

        var payload = CreatePayloadWithoutSignature(manifest);
        byte[] data = CanonicalSigner.CanonicalSerialize(payload);
        return AsymmetricSigner.AsymmetricVerify(data, manifest.Signature, publicKeyOrCertPem);
    }
}
