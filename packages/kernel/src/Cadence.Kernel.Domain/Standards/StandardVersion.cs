using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Domain.Standards;

/// <summary>
/// One version of a data standard in a tenant's Standards Registry (spec §6.3): governed reference data whose
/// datasets, variables and value-level definitions are fixed at registration and identified by
/// <see cref="ContentHash"/>. Approval and deprecation change only its <see cref="Status"/>.
/// </summary>
public sealed class StandardVersion : AggregateRoot<StandardVersionId>
{
    private readonly List<StandardDataset> _datasets = [];
    private readonly List<StandardValueLevelDefinition> _valueLevelDefinitions = [];
    private readonly List<StandardTerminologyBinding> _terminology = [];

    private StandardVersion() { }

    private StandardVersion(StandardVersionId id, TenantId tenantId, StandardPackage package, string contentHash)
        : base(id)
    {
        TenantId = tenantId;
        StandardCode = package.StandardCode;
        Version = package.Version;
        Name = package.Name;
        Publisher = package.Publisher;
        Source = package.Source;
        EffectiveDate = package.EffectiveDate;
        ContentHash = contentHash;
        Status = GovernanceStatus.Draft;

        _datasets.AddRange(package.Datasets.Select(d => new StandardDataset(tenantId, id, d)));
        _valueLevelDefinitions.AddRange(package.ValueLevelDefinitions.Select((v, i) => new StandardValueLevelDefinition(tenantId, id, i + 1, v)));
        _terminology.AddRange(package.ControlledTerminology.Select(t => new StandardTerminologyBinding(tenantId, id, t)));
    }

    public TenantId TenantId { get; private set; }

    public string StandardCode { get; private set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Publisher { get; private set; } = string.Empty;

    public string? Source { get; private set; }

    public DateOnly? EffectiveDate { get; private set; }

    /// <summary>The content hash of the normalized package (<see cref="StandardPackageContent.Hash"/>).</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public GovernanceStatus Status { get; private set; }

    public AttestationId? ApprovalAttestationId { get; private set; }

    public AttestationId? DeprecationAttestationId { get; private set; }

    public IReadOnlyList<StandardDataset> Datasets => _datasets;

    public IReadOnlyList<StandardValueLevelDefinition> ValueLevelDefinitions => _valueLevelDefinitions;

    public IReadOnlyList<StandardTerminologyBinding> Terminology => _terminology;

    /// <summary>
    /// Registers a package as a draft. The package is validated and normalized (<see cref="StandardPackageContent"/>);
    /// when <paramref name="expectedContentHash"/> is given it must equal the computed hash.
    /// </summary>
    public static (StandardVersion? Version, KernelError? Error) Register(
        StandardVersionId id, TenantId tenantId, StandardPackage package, string? expectedContentHash)
    {
        ArgumentNullException.ThrowIfNull(package);

        var problems = StandardPackageContent.Validate(package);
        if (problems.Count > 0)
        {
            return (null, StandardsErrors.PackageInvalid.WithMessage(string.Join(" ", problems.Take(20))));
        }

        var normalized = StandardPackageContent.Normalize(package);
        var hash = StandardPackageContent.Hash(normalized);
        if (!string.IsNullOrWhiteSpace(expectedContentHash) && !string.Equals(expectedContentHash, hash, StringComparison.Ordinal))
        {
            return (null, StandardsErrors.ContentHashMismatch.WithMessage($"Expected {expectedContentHash}, computed {hash}."));
        }

        return (new StandardVersion(id, tenantId, normalized, hash), null);
    }

    /// <summary>Approves a draft. The caller has verified the Attestation.</summary>
    public KernelError? Approve(AttestationId attestation)
    {
        if (Status != GovernanceStatus.Draft)
        {
            return StandardsErrors.InvalidTransition.WithMessage($"{StandardCode} {Version} is {Status}; only a draft can be approved.");
        }

        Status = GovernanceStatus.Approved;
        ApprovalAttestationId = attestation;
        return null;
    }

    /// <summary>Deprecates a draft or approved version. The caller has verified the Attestation.</summary>
    public KernelError? Deprecate(AttestationId attestation)
    {
        if (Status == GovernanceStatus.Deprecated)
        {
            return StandardsErrors.InvalidTransition.WithMessage($"{StandardCode} {Version} is already deprecated.");
        }

        Status = GovernanceStatus.Deprecated;
        DeprecationAttestationId = attestation;
        return null;
    }

    /// <summary>The registered definitions, as a package, in normalized order.</summary>
    public StandardPackage ToPackage() => new(
        StandardCode,
        Version,
        Name,
        Publisher,
        Source,
        EffectiveDate,
        [.. _datasets.OrderBy(d => d.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal).Select(d => d.ToData())],
        [.. _valueLevelDefinitions.OrderBy(v => v.Ordinal).Select(v => v.ToData())],
        [.. _terminology.Select(t => new CodeSystemVersionRef(t.CodeSystem, t.Version))
            .OrderBy(t => t.CodeSystem, StringComparer.Ordinal).ThenBy(t => t.Version, StringComparer.Ordinal)]);
}

/// <summary>A dataset of a <see cref="StandardVersion"/>.</summary>
public sealed class StandardDataset
{
    private readonly List<StandardVariable> _variables = [];

    private StandardDataset() { }

    internal StandardDataset(TenantId tenantId, StandardVersionId standardVersionId, DatasetDefinitionData data)
    {
        TenantId = tenantId;
        StandardVersionId = standardVersionId;
        Name = data.Name;
        Label = data.Label;
        Class = data.Class;
        Structure = data.Structure;
        Ordinal = data.Ordinal;
        _variables.AddRange(data.Variables.Select(v => new StandardVariable(tenantId, standardVersionId, data.Name, v)));
    }

    public TenantId TenantId { get; private set; }

    public StandardVersionId StandardVersionId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public string? Class { get; private set; }

    public string? Structure { get; private set; }

    public int Ordinal { get; private set; }

    public IReadOnlyList<StandardVariable> Variables => _variables;

    internal DatasetDefinitionData ToData() => new(
        Name, Label, Class, Structure, Ordinal,
        [.. _variables.OrderBy(v => v.Ordinal).ThenBy(v => v.Name, StringComparer.Ordinal).Select(v => v.ToData())]);
}

/// <summary>A variable of a <see cref="StandardDataset"/>.</summary>
public sealed class StandardVariable
{
    private StandardVariable() { }

    internal StandardVariable(TenantId tenantId, StandardVersionId standardVersionId, string datasetName, VariableDefinitionData data)
    {
        TenantId = tenantId;
        StandardVersionId = standardVersionId;
        DatasetName = datasetName;
        Name = data.Name;
        Label = data.Label;
        Ordinal = data.Ordinal;
        DataType = data.DataType;
        Role = data.Role;
        Core = data.Core;
        Codelists = [.. data.Codelists];
        ValueList = [.. data.ValueList];
        DescribedValueDomain = data.DescribedValueDomain;
        Description = data.Description;
    }

    public TenantId TenantId { get; private set; }

    public StandardVersionId StandardVersionId { get; private set; }

    public string DatasetName { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public int Ordinal { get; private set; }

    public StandardDataType DataType { get; private set; }

    public string? Role { get; private set; }

    public VariableCore Core { get; private set; }

    public string[] Codelists { get; private set; } = [];

    public string[] ValueList { get; private set; } = [];

    public string? DescribedValueDomain { get; private set; }

    public string? Description { get; private set; }

    internal VariableDefinitionData ToData() =>
        new(Name, Label, Ordinal, DataType, Role, Core, Codelists, ValueList, DescribedValueDomain, Description);
}

/// <summary>A value-level definition of a <see cref="StandardVersion"/>.</summary>
public sealed class StandardValueLevelDefinition
{
    private StandardValueLevelDefinition() { }

    internal StandardValueLevelDefinition(TenantId tenantId, StandardVersionId standardVersionId, int ordinal, ValueLevelDefinitionData data)
    {
        TenantId = tenantId;
        StandardVersionId = standardVersionId;
        Ordinal = ordinal;
        Dataset = data.Dataset;
        Variable = data.Variable;
        Where = [.. data.Where];
        DataType = data.DataType;
        Length = data.Length;
        Codelists = [.. data.Codelists];
        ValueList = [.. data.ValueList];
        Description = data.Description;
    }

    public TenantId TenantId { get; private set; }

    public StandardVersionId StandardVersionId { get; private set; }

    /// <summary>Position within the standard version, from 1.</summary>
    public int Ordinal { get; private set; }

    public string Dataset { get; private set; } = string.Empty;

    public string Variable { get; private set; } = string.Empty;

    public List<WhereCondition> Where { get; private set; } = [];

    public StandardDataType DataType { get; private set; }

    public int? Length { get; private set; }

    public string[] Codelists { get; private set; } = [];

    public string[] ValueList { get; private set; } = [];

    public string? Description { get; private set; }

    internal ValueLevelDefinitionData ToData() =>
        new(Dataset, Variable, [.. Where], DataType, Length, Codelists, ValueList, Description);
}

/// <summary>A controlled-terminology version a <see cref="StandardVersion"/> references.</summary>
public sealed class StandardTerminologyBinding
{
    private StandardTerminologyBinding() { }

    internal StandardTerminologyBinding(TenantId tenantId, StandardVersionId standardVersionId, CodeSystemVersionRef reference)
    {
        TenantId = tenantId;
        StandardVersionId = standardVersionId;
        CodeSystem = reference.CodeSystem;
        Version = reference.Version;
    }

    public TenantId TenantId { get; private set; }

    public StandardVersionId StandardVersionId { get; private set; }

    public string CodeSystem { get; private set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;
}
