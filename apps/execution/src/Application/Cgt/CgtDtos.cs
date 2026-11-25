using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Execution.Application.Cgt;

public record CgtApheresisCreateRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("collection_site_id")] string CollectionSiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("din")] string? Din = null,
    [property: JsonPropertyName("collection_timestamp")] DateTimeOffset? CollectionTimestamp = null,
    [property: JsonPropertyName("apheresis_bag_identifier")] string? ApheresisBagIdentifier = null,
    [property: JsonPropertyName("volume_ml")] double? VolumeMl = null,
    [property: JsonPropertyName("cell_count_billions")] double? CellCountBillions = null,
    [property: JsonPropertyName("custodian_id")] string? CustodianId = null,
    [property: JsonPropertyName("custodian_name")] string? CustodianName = null,
    [property: JsonPropertyName("custodian_role")] string? CustodianRole = null,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public record CgtChainOfCustodyResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("din")] string Din,
    [property: JsonPropertyName("batch_id")] string? BatchId,
    [property: JsonPropertyName("collection_site_id")] string? CollectionSiteId,
    [property: JsonPropertyName("manufacturing_facility_id")] string? ManufacturingFacilityId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("current_stage")] string CurrentStage,
    [property: JsonPropertyName("cryo_temp_excursion_count")] int CryoTempExcursionCount,
    [property: JsonPropertyName("lowest_temp_celsius")] double? LowestTempCelsius,
    [property: JsonPropertyName("unlock_token")] string? UnlockToken,
    [property: JsonPropertyName("harvested_at")] DateTimeOffset? HarvestedAt,
    [property: JsonPropertyName("infused_at")] DateTimeOffset? InfusedAt,
    [property: JsonPropertyName("reconciled_at")] DateTimeOffset? ReconciledAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy
);

public record CgtMilestoneCreateRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("chain_of_custody_id")] Guid ChainOfCustodyId,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("milestone_type")] string MilestoneType,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp = null,
    [property: JsonPropertyName("location_name")] string? LocationName = null,
    [property: JsonPropertyName("facility_id")] string? FacilityId = null,
    [property: JsonPropertyName("latitude")] double? Latitude = null,
    [property: JsonPropertyName("longitude")] double? Longitude = null,
    [property: JsonPropertyName("temperature_celsius")] double? TemperatureCelsius = null,
    [property: JsonPropertyName("custodian_id")] string? CustodianId = null,
    [property: JsonPropertyName("custodian_name")] string? CustodianName = null,
    [property: JsonPropertyName("custodian_role")] string? CustodianRole = null,
    [property: JsonPropertyName("carrier")] string? Carrier = null,
    [property: JsonPropertyName("tracking_number")] string? TrackingNumber = null,
    [property: JsonPropertyName("seal_intact")] bool SealIntact = true,
    [property: JsonPropertyName("seal_id")] string? SealId = null,
    [property: JsonPropertyName("dry_shipper_id")] string? DryShipperId = null,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public record CgtMilestoneResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("chain_of_custody_id")] Guid ChainOfCustodyId,
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("milestone_type")] string MilestoneType,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("location_name")] string? LocationName,
    [property: JsonPropertyName("facility_id")] string? FacilityId,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("temperature_celsius")] double? TemperatureCelsius,
    [property: JsonPropertyName("temperature_status")] string? TemperatureStatus,
    [property: JsonPropertyName("custodian_id")] string? CustodianId,
    [property: JsonPropertyName("custodian_name")] string? CustodianName,
    [property: JsonPropertyName("custodian_role")] string? CustodianRole,
    [property: JsonPropertyName("carrier")] string? Carrier,
    [property: JsonPropertyName("tracking_number")] string? TrackingNumber,
    [property: JsonPropertyName("seal_intact")] bool SealIntact,
    [property: JsonPropertyName("seal_id")] string? SealId,
    [property: JsonPropertyName("dry_shipper_id")] string? DryShipperId,
    [property: JsonPropertyName("notes")] string? Notes
);

public record CgtManufacturingBindRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("chain_of_custody_id")] Guid ChainOfCustodyId,
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("manufacturing_facility_id")] string ManufacturingFacilityId,
    [property: JsonPropertyName("synthesis_start")] DateTimeOffset? SynthesisStart = null,
    [property: JsonPropertyName("synthesis_end")] DateTimeOffset? SynthesisEnd = null,
    [property: JsonPropertyName("release_qc_status")] string? ReleaseQcStatus = null,
    [property: JsonPropertyName("qualified_person_signoff_id")] string? QualifiedPersonSignoffId = null,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public record CgtBedsideVerificationRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("din")] string Din,
    [property: JsonPropertyName("batch_id")] string? BatchId,
    [property: JsonPropertyName("bag_barcode_payload")] string BagBarcodePayload,
    [property: JsonPropertyName("patient_wristband_id")] string PatientWristbandId,
    [property: JsonPropertyName("primary_verifier_id")] string PrimaryVerifierId,
    [property: JsonPropertyName("primary_verifier_name")] string? PrimaryVerifierName = null,
    [property: JsonPropertyName("primary_verifier_role")] string? PrimaryVerifierRole = null,
    [property: JsonPropertyName("primary_signature_token")] string PrimarySignatureToken = "",
    [property: JsonPropertyName("secondary_verifier_id")] string SecondaryVerifierId = "",
    [property: JsonPropertyName("secondary_verifier_name")] string? SecondaryVerifierName = null,
    [property: JsonPropertyName("secondary_verifier_role")] string? SecondaryVerifierRole = null,
    [property: JsonPropertyName("secondary_signature_token")] string SecondarySignatureToken = "",
    [property: JsonPropertyName("notes")] string? Notes = null
)
{
    /// <summary>
    /// Canonical target-record binding identifier (<c>batch_id</c> claim) carried by both bedside step-up signature tokens.
    /// </summary>
    public string TargetRecordBindingId() =>
        $"{(StudyId ?? string.Empty).Trim()}:{(SubjectId ?? string.Empty).Trim()}:{(Din ?? string.Empty).Trim()}:{(BatchId ?? string.Empty).Trim()}";
}

public record CgtBedsideVerificationResponse(
    [property: JsonPropertyName("verification_id")] Guid VerificationId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("din")] string Din,
    [property: JsonPropertyName("batch_id")] string? BatchId,
    [property: JsonPropertyName("patient_wristband_id")] string PatientWristbandId,
    [property: JsonPropertyName("primary_verifier_id")] string PrimaryVerifierId,
    [property: JsonPropertyName("secondary_verifier_id")] string SecondaryVerifierId,
    [property: JsonPropertyName("unlock_token")] string UnlockToken,
    [property: JsonPropertyName("verified_at")] DateTimeOffset VerifiedAt,
    [property: JsonPropertyName("is_match")] bool IsMatch,
    [property: JsonPropertyName("status")] string Status
);

public record CgtInfusionExecuteRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("chain_of_custody_id")] Guid ChainOfCustodyId,
    [property: JsonPropertyName("unlock_token")] string UnlockToken,
    [property: JsonPropertyName("primary_verifier_id")] string PrimaryVerifierId,
    [property: JsonPropertyName("infusion_start")] DateTimeOffset? InfusionStart = null,
    [property: JsonPropertyName("infusion_end")] DateTimeOffset? InfusionEnd = null,
    [property: JsonPropertyName("volume_infused_ml")] double? VolumeInfusedMl = null,
    [property: JsonPropertyName("adverse_event_observed")] bool AdverseEventObserved = false,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public record CgtVialReturnReconciliationRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("vial_identifier")] string VialIdentifier,
    [property: JsonPropertyName("returned_quantity")] int ReturnedQuantity = 1,
    [property: JsonPropertyName("intact_vials")] int IntactVials = 0,
    [property: JsonPropertyName("used_vials")] int UsedVials = 1,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl = null,
    [property: JsonPropertyName("logged_by")] string? LoggedBy = null,
    [property: JsonPropertyName("notes")] string? Notes = null
);

public record CgtVialReturnReconciliationResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("kit_number")] string KitNumber,
    [property: JsonPropertyName("vial_identifier")] string VialIdentifier,
    [property: JsonPropertyName("returned_quantity")] int ReturnedQuantity,
    [property: JsonPropertyName("intact_vials")] int IntactVials,
    [property: JsonPropertyName("used_vials")] int UsedVials,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl,
    [property: JsonPropertyName("adherence_percentage")] double AdherencePercentage,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("logged_by")] string LoggedBy,
    [property: JsonPropertyName("logged_at")] DateTimeOffset LoggedAt,
    [property: JsonPropertyName("certificate_id")] string? CertificateId,
    [property: JsonPropertyName("notes")] string? Notes
);

public record CgtDestructionCertificateCreateRequest(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("kit_numbers")] List<string> KitNumbers,
    [property: JsonPropertyName("vial_identifiers")] List<string> VialIdentifiers,
    [property: JsonPropertyName("destruction_reason")] string DestructionReason,
    [property: JsonPropertyName("destruction_method")] string DestructionMethod,
    [property: JsonPropertyName("destruction_facility")] string DestructionFacility,
    [property: JsonPropertyName("witness_1_id")] string Witness1Id,
    [property: JsonPropertyName("witness_1_name")] string? Witness1Name = null,
    [property: JsonPropertyName("witness_1_role")] string? Witness1Role = null,
    [property: JsonPropertyName("witness_1_signature_token")] string Witness1SignatureToken = "",
    [property: JsonPropertyName("witness_2_id")] string Witness2Id = "",
    [property: JsonPropertyName("witness_2_name")] string? Witness2Name = null,
    [property: JsonPropertyName("witness_2_role")] string? Witness2Role = null,
    [property: JsonPropertyName("witness_2_signature_token")] string Witness2SignatureToken = "",
    [property: JsonPropertyName("quantity_destroyed")] int QuantityDestroyed = 1,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl = null,
    [property: JsonPropertyName("subject_id")] string? SubjectId = null,
    [property: JsonPropertyName("din")] string? Din = null,
    [property: JsonPropertyName("coc_id")] Guid? CocId = null,
    [property: JsonPropertyName("notes")] string? Notes = null
)
{
    /// <summary>
    /// Canonical target-record binding identifier (<c>batch_id</c> claim) carried by both destruction witness step-up tokens.
    /// </summary>
    public string TargetRecordBindingId() =>
        $"{(StudyId ?? string.Empty).Trim()}:{(SiteId ?? string.Empty).Trim()}:{(LotNumber ?? string.Empty).Trim()}:{(Din ?? string.Empty).Trim()}";
}

public record CgtDestructionCertificateResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("certificate_number")] string CertificateNumber,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("chain_of_custody_id")] Guid? ChainOfCustodyId,
    [property: JsonPropertyName("subject_id")] string? SubjectId,
    [property: JsonPropertyName("din")] string? Din,
    [property: JsonPropertyName("lot_number")] string LotNumber,
    [property: JsonPropertyName("kit_numbers")] List<string> KitNumbers,
    [property: JsonPropertyName("vial_identifiers")] List<string> VialIdentifiers,
    [property: JsonPropertyName("quantity_destroyed")] int QuantityDestroyed,
    [property: JsonPropertyName("unit_of_measure")] string UnitOfMeasure,
    [property: JsonPropertyName("residual_volume_ml")] double? ResidualVolumeMl,
    [property: JsonPropertyName("destruction_reason")] string DestructionReason,
    [property: JsonPropertyName("destruction_method")] string DestructionMethod,
    [property: JsonPropertyName("destruction_facility")] string DestructionFacility,
    [property: JsonPropertyName("destruction_timestamp")] DateTimeOffset DestructionTimestamp,
    [property: JsonPropertyName("witness_1_id")] string Witness1Id,
    [property: JsonPropertyName("witness_2_id")] string Witness2Id,
    [property: JsonPropertyName("cryptographic_seal")] string CryptographicSeal,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("notes")] string? Notes
);

public record CgtBarcodeVerifyRequest(
    [property: JsonPropertyName("barcode_payload")] string BarcodePayload,
    [property: JsonPropertyName("expected_subject_id")] string? ExpectedSubjectId = null,
    [property: JsonPropertyName("expected_din")] string? ExpectedDin = null,
    [property: JsonPropertyName("expected_batch_id")] string? ExpectedBatchId = null
);

public record CgtBarcodeVerifyResponse(
    [property: JsonPropertyName("is_valid")] bool IsValid,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("barcode_id")] string? BarcodeId,
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("subject_id")] string? SubjectId,
    [property: JsonPropertyName("din")] string? Din,
    [property: JsonPropertyName("batch_id")] string? BatchId
);

public record CgtFullTraceabilityResponse(
    [property: JsonPropertyName("chain_of_custody")] CgtChainOfCustodyResponse? ChainOfCustody,
    [property: JsonPropertyName("milestones")] List<CgtMilestoneResponse> Milestones,
    [property: JsonPropertyName("bedside_verification")] CgtBedsideVerificationResponse? BedsideVerification,
    [property: JsonPropertyName("destruction_certificate")] CgtDestructionCertificateResponse? DestructionCertificate
);
