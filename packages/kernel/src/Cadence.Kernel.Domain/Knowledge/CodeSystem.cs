using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Domain.Knowledge;

/// <summary>
/// A code system registered in a tenant's Concept Registry (spec §6.1): LOINC, UCUM, CDISC CT, MedDRA, a sponsor
/// dictionary. Its content arrives as immutable <see cref="CodeSystemVersion"/>s. Every tenant loads its own
/// versions, under its own licence for licensed systems (K-SEM-2, ADR-2470).
/// </summary>
public class CodeSystem : AggregateRoot<CodeSystemId>
{
    public TenantId TenantId { get; private set; }
    public string Uri { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Publisher { get; private set; } = string.Empty;
    public LicenseClass License { get; private set; }

    protected CodeSystem() { }

    private CodeSystem(CodeSystemId id, TenantId tenantId, string uri, string name, string publisher, LicenseClass license)
        : base(id)
    {
        TenantId = tenantId;
        Uri = uri;
        Name = name;
        Publisher = publisher;
        License = license;
    }

    /// <summary>Registers a code system, or returns why it cannot be registered.</summary>
    public static CommandResult<CodeSystem> Register(CodeSystemId id, TenantId tenantId, string uri, string name, string publisher, LicenseClass license)
    {
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out _))
        {
            return KnowledgeErrors.CodeSystemUriInvalid;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher))
        {
            return IdentityErrors.NameRequired;
        }

        if (KnownCodeSystems.RequiredLicence(uri) is { } required && required != license)
        {
            return KnowledgeErrors.LicenseMismatch.WithMessage(
                $"{uri} is {required} content; it cannot be registered as {license} (K-SEM-2).");
        }

        return CommandResult<CodeSystem>.Success(new CodeSystem(id, tenantId, uri, name.Trim(), publisher.Trim(), license));
    }
}

/// <summary>
/// One release of a code system with all of its entries, bound to the SHA-256 of the package it was loaded from. It is
/// written once and never changes (its tables are insert-only), so a reference to it always means the same content.
/// </summary>
public class CodeSystemVersion : AggregateRoot<CodeSystemVersionId>
{
    private readonly List<CodeSystemEntry> _entries = [];

    public TenantId TenantId { get; private set; }
    public CodeSystemId CodeSystemId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public DateOnly ReleasedOn { get; private set; }
    public string PackageSha256 { get; private set; } = string.Empty;
    public string PackageFileName { get; private set; } = string.Empty;
    public string PackageSourceUri { get; private set; } = string.Empty;
    public long PackageByteLength { get; private set; }
    public string PackageFormat { get; private set; } = string.Empty;
    public int EntryCount { get; private set; }

    public IReadOnlyList<CodeSystemEntry> Entries => _entries;

    protected CodeSystemVersion() { }

    /// <summary>Builds a version from a parsed package, or returns why the package cannot be imported.</summary>
    public static CommandResult<CodeSystemVersion> Import(
        CodeSystemVersionId id,
        TenantId tenantId,
        CodeSystemId codeSystemId,
        string version,
        DateOnly releasedOn,
        TerminologyPackage package,
        IReadOnlyList<CodeSystemEntryData> entries)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(entries);

        if (string.IsNullOrWhiteSpace(version))
        {
            return KnowledgeErrors.VersionRequired;
        }

        if (!IsSha256(package.Sha256) || string.IsNullOrWhiteSpace(package.FileName)
            || string.IsNullOrWhiteSpace(package.SourceUri) || string.IsNullOrWhiteSpace(package.Format) || package.ByteLength <= 0)
        {
            return KnowledgeErrors.PackageInvalid;
        }

        if (entries.Count == 0 || entries.Any(e => string.IsNullOrWhiteSpace(e.Code) || string.IsNullOrWhiteSpace(e.Display)))
        {
            return KnowledgeErrors.EntriesInvalid;
        }

        var duplicate = entries.GroupBy(e => (e.GroupCode, e.Code)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return KnowledgeErrors.DuplicateEntry.WithMessage(
                $"Code {duplicate.Key.Code} appears {duplicate.Count()} times in group {duplicate.Key.GroupCode ?? "(none)"}.");
        }

        var result = new CodeSystemVersion
        {
            Id = id,
            TenantId = tenantId,
            CodeSystemId = codeSystemId,
            Version = version.Trim(),
            ReleasedOn = releasedOn,
            PackageSha256 = package.Sha256.ToLowerInvariant(),
            PackageFileName = package.FileName,
            PackageSourceUri = package.SourceUri,
            PackageByteLength = package.ByteLength,
            PackageFormat = package.Format,
            EntryCount = entries.Count,
        };

        for (var i = 0; i < entries.Count; i++)
        {
            result._entries.Add(new CodeSystemEntry(tenantId, id, i, entries[i]));
        }

        return CommandResult<CodeSystemVersion>.Success(result);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

/// <summary>One code in a <see cref="CodeSystemVersion"/>. Insert-only, with its version.</summary>
public class CodeSystemEntry
{
    public TenantId TenantId { get; private set; }
    public CodeSystemVersionId CodeSystemVersionId { get; private set; }

    /// <summary>Position in the package, which keeps the key deterministic for the same package.</summary>
    public int Ordinal { get; private set; }

    public string Code { get; private set; } = string.Empty;
    public string Display { get; private set; } = string.Empty;
    public string? GroupCode { get; private set; }
    public string? SubmissionValue { get; private set; }
    public string? Definition { get; private set; }
    public List<string> Synonyms { get; private set; } = [];

    protected CodeSystemEntry() { }

    internal CodeSystemEntry(TenantId tenantId, CodeSystemVersionId versionId, int ordinal, CodeSystemEntryData data)
    {
        TenantId = tenantId;
        CodeSystemVersionId = versionId;
        Ordinal = ordinal;
        Code = data.Code.Trim();
        Display = data.Display.Trim();
        GroupCode = string.IsNullOrWhiteSpace(data.GroupCode) ? null : data.GroupCode.Trim();
        SubmissionValue = string.IsNullOrWhiteSpace(data.SubmissionValue) ? null : data.SubmissionValue.Trim();
        Definition = string.IsNullOrWhiteSpace(data.Definition) ? null : data.Definition.Trim();
        Synonyms = data.Synonyms?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
    }
}
