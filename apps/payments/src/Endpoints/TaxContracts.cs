using System.Text.Json.Serialization;

namespace Cadence.Apps.Payments.Endpoints;

public record SubmitW9RequestDto(
    string FullLegalName,
    string TaxIdLastFour,
    string? Address = null,
    bool CertifyAccurate = true
);

public record VerifyW9RequestDto(
    string? Notes = null
);

public record Tax1099StatusResponseDto(
    [property: JsonPropertyName("wallet_id")] string WalletId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("taxable_ytd")] decimal TaxableYtd,
    [property: JsonPropertyName("threshold_amount")] decimal ThresholdAmount,
    [property: JsonPropertyName("threshold_percentage")] decimal ThresholdPercentage,
    [property: JsonPropertyName("is_1099_threshold_exceeded")] bool Is1099ThresholdExceeded,
    [property: JsonPropertyName("w9_status")] string W9Status,
    [property: JsonPropertyName("warning_level")] string WarningLevel
);
