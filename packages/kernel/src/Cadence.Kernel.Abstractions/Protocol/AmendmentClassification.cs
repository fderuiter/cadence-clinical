namespace Cadence.Kernel.Protocol;

/// <summary>
/// Regulatory classification of a protocol amendment (spec §6.2; EU CTR Art. 2(13), ICH E6(R3)). Stored by name.
/// </summary>
public enum AmendmentClassification
{
    Substantial,
    NonSubstantial,
    UrgentSafetyMeasure,
}
