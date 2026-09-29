using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Core.Domain.Rules;

namespace Cadence.Core.Domain.DerivedMetrics;

/// <summary>
/// Supported clinical formula types.
/// @req:PRD-SYS-053
/// </summary>
public enum DerivedFormulaType
{
    BMI,
    BSA_MOSTELLER,
    BSA_DUBOIS,
    EGFR_CKD_EPI_2021,
    EGFR_MDRD,
    QTC_FRIDERICIA,
    QTC_BAZETT,
    MAP,
    COCKCROFT_GAULT,
    CUSTOM_AST
}

/// <summary>
/// Physiological validation boundary for a medical input variable.
/// @req:PRD-SYS-053
/// </summary>
public record PlausibilityRange(
    double? MinValue = null,
    double? MaxValue = null,
    string? Unit = null,
    bool WarningOnly = false
);

/// <summary>
/// Specification of an input variable required for a derived calculation.
/// @req:PRD-SYS-053
/// </summary>
public record FormulaInputRequirement(
    string Name,
    string? Unit = null,
    bool Required = true,
    double? DefaultValue = null,
    PlausibilityRange? Plausibility = null
);

/// <summary>
/// Result of a derived variable calculation with validation metadata.
/// @req:PRD-SYS-053
/// </summary>
public record DerivedCalculationResult(
    string FormulaId,
    string OutputVariable,
    double? CalculatedValue = null,
    string? OutputUnit = null,
    bool IsValid = true,
    string? ErrorMessage = null,
    IReadOnlyList<string>? PlausibilityViolations = null,
    IReadOnlyDictionary<string, object?>? InputValuesUsed = null,
    DateTime? EvalTimestamp = null
)
{
    public IReadOnlyList<string> PlausibilityViolations { get; init; } = PlausibilityViolations ?? Array.Empty<string>();
    public IReadOnlyDictionary<string, object?> InputValuesUsed { get; init; } = InputValuesUsed ?? new Dictionary<string, object?>();
    public DateTime? EvalTimestamp { get; init; } = EvalTimestamp ?? DateTime.UtcNow;
}

/// <summary>
/// Declarative definition of a standardized or custom derived variable formula.
/// @req:PRD-SYS-053
/// </summary>
public record DerivedFormulaDefinition
{
    public string FormulaId { get; init; }
    public string Name { get; init; }
    public DerivedFormulaType FormulaType { get; init; }
    public ExpressionNode? Expression { get; init; }
    public IReadOnlyList<FormulaInputRequirement> Inputs { get; init; }
    public string OutputVariable { get; init; }
    public string? OutputUnit { get; init; }
    public int VersionIndex { get; init; }
    public bool IsActive { get; init; }
    public string? ReasonForChange { get; init; }
    public string? Sha256Checksum { get; init; }

    public DerivedFormulaDefinition(
        string formulaId,
        string name,
        string outputVariable,
        DerivedFormulaType formulaType = DerivedFormulaType.CUSTOM_AST,
        ExpressionNode? expression = null,
        IReadOnlyList<FormulaInputRequirement>? inputs = null,
        string? outputUnit = null,
        int versionIndex = 1,
        bool isActive = true,
        string? reasonForChange = null,
        string? sha256Checksum = null)
    {
        FormulaId = formulaId;
        Name = name;
        OutputVariable = outputVariable;
        FormulaType = formulaType;
        Expression = expression;
        Inputs = inputs ?? Array.Empty<FormulaInputRequirement>();
        OutputUnit = outputUnit;
        VersionIndex = versionIndex;
        IsActive = isActive;
        ReasonForChange = reasonForChange;

        if (!string.IsNullOrEmpty(sha256Checksum))
        {
            Sha256Checksum = sha256Checksum;
        }
        else
        {
            var payload = new
            {
                expression = Expression,
                formula_id = FormulaId,
                formula_type = FormulaType.ToString(),
                inputs = Inputs,
                output_variable = OutputVariable,
                version_index = VersionIndex
            };
            string canonical = JsonSerializer.Serialize(payload);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            Sha256Checksum = Convert.ToHexStringLower(hash);
        }
    }
}
