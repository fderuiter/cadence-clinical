using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Supply;

public record IpKitScanValidateRequest(
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("scanned_lot_number")] string? ScannedLotNumber = null
);

public record IpKitScanValidateResponse(
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("expected_lot_number")] string ExpectedLotNumber,
    [property: JsonPropertyName("scanned_lot_number")] string? ScannedLotNumber,
    [property: JsonPropertyName("is_lot_match")] bool IsLotMatch,
    [property: JsonPropertyName("is_valid")] bool IsValid,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message
);

public record IpKitItemReconcileRequest(
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("expected_lot_number")] string ExpectedLotNumber,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl,
    [property: JsonPropertyName("residual_volume_percentage")] double? ResidualVolumePercentage,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("disposition_reason")] string DispositionReason,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public record IpKitBatchReconciliationRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("items")] List<IpKitItemReconcileRequest> Items,
    [property: JsonPropertyName("processed_by")] string? ProcessedBy = null,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Batch IP kit reconciliation"
);

public record IpKitItemResponse(
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("expected_lot_number")] string ExpectedLotNumber,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl,
    [property: JsonPropertyName("residual_volume_percentage")] double? ResidualVolumePercentage,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("disposition_reason")] string DispositionReason,
    [property: JsonPropertyName("is_quarantined")] bool IsQuarantined
);

public record IpKitCertificateResponse(
    [property: JsonPropertyName("certificate_id")] string CertificateId,
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("batch_number")] string BatchNumber,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("issued_by")] string IssuedBy,
    [property: JsonPropertyName("issued_at")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("kit_numbers")] List<string> KitNumbers,
    [property: JsonPropertyName("digital_signature")] string DigitalSignature
);

public record IpKitReconciliationBatchResponse(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("batch_number")] string BatchNumber,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("processed_by")] string ProcessedBy,
    [property: JsonPropertyName("processed_at")] DateTimeOffset ProcessedAt,
    [property: JsonPropertyName("total_kits")] int TotalKits,
    [property: JsonPropertyName("quarantined_kits")] int QuarantinedKits,
    [property: JsonPropertyName("certificate_id")] string CertificateId,
    [property: JsonPropertyName("items")] List<IpKitItemResponse> Items,
    [property: JsonPropertyName("certificate")] IpKitCertificateResponse Certificate
);

public record IpKitInventoryResponse(
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("expiration_date")] DateTimeOffset ExpirationDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl = null,
    [property: JsonPropertyName("residual_volume_percentage")] double? ResidualVolumePercentage = null
);
