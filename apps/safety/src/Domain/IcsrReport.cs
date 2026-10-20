using System.Collections.Generic;

namespace Cadence.Apps.Safety.Domain;

// The request body of POST /api/v1/safety/icsr/render: the ICH E2B(R3) data elements the route transmits. Each member
// names its E2B(R3) element; IcsrConformance checks the ICH conformance and business rules before anything is rendered.
// Dates and times are HL7 TS strings (CCYY[MM[DD[hh[mm[ss]]]]][+/-ZZzz]); codes are the ICH E2B code-list values.

/// <summary>The batch and message wrapper (N.1, N.2.r) of a single-message ICSR batch.</summary>
/// <param name="BatchNumber">N.1.2 batch number.</param>
/// <param name="BatchSenderIdentifier">N.1.3 batch sender identifier, as agreed between the trading partners.</param>
/// <param name="BatchReceiverIdentifier">N.1.4 batch receiver identifier.</param>
/// <param name="BatchTransmissionDate">N.1.5 date of batch transmission, to the second.</param>
/// <param name="MessageIdentifier">N.2.r.1 message identifier.</param>
/// <param name="MessageSenderIdentifier">N.2.r.2 message sender identifier.</param>
/// <param name="MessageReceiverIdentifier">N.2.r.3 message receiver identifier. N.2.r.4 is C.1.2.</param>
public record IcsrHeader(
    string BatchNumber,
    string BatchSenderIdentifier,
    string BatchReceiverIdentifier,
    string BatchTransmissionDate,
    string MessageIdentifier,
    string MessageSenderIdentifier,
    string MessageReceiverIdentifier);

/// <summary>Identification of the case safety report (C.1).</summary>
/// <param name="SenderSafetyReportId">C.1.1 sender's (case) safety report unique identifier (country-sender-number).</param>
/// <param name="CreationDate">C.1.2 date of creation, to the second; also sent as N.2.r.4.</param>
/// <param name="ReportType">C.1.3 type of report: 1 spontaneous, 2 report from study, 3 other, 4 not available to sender.</param>
/// <param name="FirstReceivedDate">C.1.4 date the report was first received from the source, at least to the day.</param>
/// <param name="MostRecentInformationDate">C.1.5 date of the most recent information, at least to the day.</param>
/// <param name="FulfilsLocalCriteriaForExpeditedReport">C.1.7, true or false.</param>
/// <param name="WorldwideUniqueCaseId">C.1.8.1 worldwide unique case identification.</param>
/// <param name="FirstSender">C.1.8.2 first sender of this case: 1 regulator, 2 other.</param>
/// <param name="DocumentsHeldBySender">C.1.6.1.r.1; C.1.6.1 (additional documents available) is true when any are listed.</param>
/// <param name="OtherCaseIdentifiers">C.1.9.1.r; C.1.9.1 is true when any are listed, otherwise nullFlavor NI.</param>
public record IcsrSafetyReport(
    string SenderSafetyReportId,
    string CreationDate,
    string ReportType,
    string FirstReceivedDate,
    string MostRecentInformationDate,
    bool? FulfilsLocalCriteriaForExpeditedReport,
    string WorldwideUniqueCaseId,
    string FirstSender,
    IReadOnlyList<string>? DocumentsHeldBySender = null,
    IReadOnlyList<IcsrOtherCaseIdentifier>? OtherCaseIdentifiers = null);

/// <summary>An identifier the case had in a previous transmission (C.1.9.1.r).</summary>
/// <param name="Source">C.1.9.1.r.1 source of the case identifier.</param>
/// <param name="CaseIdentifier">C.1.9.1.r.2 case identifier.</param>
public record IcsrOtherCaseIdentifier(string Source, string CaseIdentifier);

/// <summary>A primary source of information (C.2.r). Reporter names and addresses are not transmitted.</summary>
/// <param name="Qualification">C.2.r.4: 1 physician, 2 pharmacist, 3 other health professional, 4 lawyer, 5 consumer, or UNK.</param>
/// <param name="CountryCode">C.2.r.3 ISO 3166-1 alpha-2 country code (or EU).</param>
/// <param name="PrimaryForRegulatoryPurposes">C.2.r.5: exactly one primary source is flagged.</param>
public record IcsrPrimarySource(string Qualification, string? CountryCode = null, bool PrimaryForRegulatoryPurposes = false);

/// <summary>The sender of the case safety report (C.3).</summary>
/// <param name="SenderType">C.3.1 sender type, 1 to 7 (1 pharmaceutical company).</param>
/// <param name="Organisation">C.3.2 sender's organisation; required unless the sender type is 7 (patient / consumer).</param>
public record IcsrSender(string SenderType, string? Organisation = null);

/// <summary>Study identification (C.5); required when the report type (C.1.3) is 2, a report from a study.</summary>
/// <param name="StudyType">C.5.4: 1 clinical trials, 2 individual patient use, 3 other studies.</param>
/// <param name="SponsorStudyNumber">C.5.3 sponsor study number.</param>
/// <param name="StudyName">C.5.2 study name.</param>
public record IcsrStudy(string StudyType, string? SponsorStudyNumber = null, string? StudyName = null);

/// <summary>
/// Patient characteristics (D). The patient's name or initials (D.1) are never transmitted: D.1 is sent masked
/// (nullFlavor MSK), as the ICH guide prescribes when data privacy keeps them from being sent.
/// </summary>
/// <param name="PatientId">D.1.1.4 investigation number (the trial subject identifier).</param>
/// <param name="Sex">D.5 sex: 1 male, 2 female; omitted when not given.</param>
/// <param name="Age">D.2.2a age at onset of the reaction.</param>
/// <param name="AgeUnit">D.2.2b UCUM unit of the age: a, mo, wk, d, h or {decade}; required with an age.</param>
/// <param name="BirthDate">D.2.1 date of birth, at least to the day.</param>
public record IcsrPatient(
    string? PatientId = null,
    string? Sex = null,
    int? Age = null,
    string? AgeUnit = null,
    string? BirthDate = null);

/// <summary>
/// The MedDRA coding of a reaction (E.i.2.1). Only the lowest level term is transmitted; the receiver derives the
/// hierarchy. Whether the code exists in the given MedDRA version needs the licensed MedDRA dictionary and is not
/// checked here.
/// </summary>
/// <param name="Version">E.i.2.1a MedDRA version, such as 27.1; one version per ICSR.</param>
/// <param name="LltCode">E.i.2.1b MedDRA lowest level term code, eight digits.</param>
public record IcsrMeddraCoding(string Version, string LltCode);

/// <summary>A reaction or event (E.i).</summary>
/// <param name="MeddraCoding">E.i.2.1a and E.i.2.1b, required.</param>
/// <param name="Outcome">E.i.7 outcome: 1 recovered, 2 recovering, 3 not recovered, 4 recovered with sequelae, 5 fatal, 0 unknown.</param>
/// <param name="ReactionTerm">E.i.1.1a reaction as reported by the primary source.</param>
/// <param name="ReactionTermLanguage">E.i.1.1b ISO 639-2 alpha-3 language of the reported term; required with a term.</param>
/// <param name="StartDate">E.i.4 date of start, at least to the year.</param>
/// <param name="EndDate">E.i.5 date of end, at least to the year.</param>
/// <param name="ResultsInDeath">E.i.3.2a; true, or sent as nullFlavor NI when false.</param>
/// <param name="LifeThreatening">E.i.3.2b; true, or NI.</param>
/// <param name="CausedOrProlongedHospitalisation">E.i.3.2c; true, or NI.</param>
/// <param name="DisablingIncapacitating">E.i.3.2d; true, or NI.</param>
/// <param name="CongenitalAnomalyBirthDefect">E.i.3.2e; true, or NI.</param>
/// <param name="OtherMedicallyImportantCondition">E.i.3.2f; true, or NI.</param>
public record IcsrReaction(
    IcsrMeddraCoding MeddraCoding,
    string Outcome,
    string? ReactionTerm = null,
    string? ReactionTermLanguage = null,
    string? StartDate = null,
    string? EndDate = null,
    bool ResultsInDeath = false,
    bool LifeThreatening = false,
    bool CausedOrProlongedHospitalisation = false,
    bool DisablingIncapacitating = false,
    bool CongenitalAnomalyBirthDefect = false,
    bool OtherMedicallyImportantCondition = false);

/// <summary>A drug (G.k).</summary>
/// <param name="DrugRole">G.k.1 characterisation of drug role: 1 suspect, 2 concomitant, 3 interacting, 4 drug not administered.</param>
/// <param name="DrugName">G.k.2.2 medicinal product name as reported by the primary source.</param>
/// <param name="ActiveSubstanceName">G.k.2.3.r.1 substance name.</param>
/// <param name="DosageText">G.k.4.r.8 dosage text.</param>
/// <param name="RouteOfAdministration">G.k.4.r.10.2b route of administration, a three-digit code of ICH E2B code list 14.</param>
/// <param name="ActionTakenWithDrug">G.k.8 action taken: 1 withdrawn, 2 reduced, 3 increased, 4 not changed, 0 unknown, 9 not applicable.</param>
public record IcsrDrug(
    string DrugRole,
    string DrugName,
    string? ActiveSubstanceName = null,
    string? DosageText = null,
    string? RouteOfAdministration = null,
    string? ActionTakenWithDrug = null);

/// <summary>An ICH E2B(R3) individual case safety report to render as an HL7 v3 ICSR message.</summary>
/// <param name="Header">N.1 and N.2.r.</param>
/// <param name="SafetyReport">C.1.</param>
/// <param name="PrimarySources">C.2.r, at least one.</param>
/// <param name="Sender">C.3.</param>
/// <param name="Patient">D.</param>
/// <param name="Reactions">E.i, at least one.</param>
/// <param name="Drugs">G.k, at least one suspect, interacting or not-administered drug.</param>
/// <param name="Narrative">H.1 case narrative, required.</param>
/// <param name="Study">C.5, required when C.1.3 is 2.</param>
public record IcsrReport(
    IcsrHeader Header,
    IcsrSafetyReport SafetyReport,
    IReadOnlyList<IcsrPrimarySource> PrimarySources,
    IcsrSender Sender,
    IcsrPatient Patient,
    IReadOnlyList<IcsrReaction> Reactions,
    IReadOnlyList<IcsrDrug> Drugs,
    string Narrative,
    IcsrStudy? Study = null);
