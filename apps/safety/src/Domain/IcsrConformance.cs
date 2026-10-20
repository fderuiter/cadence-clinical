using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Apps.Safety.Domain;

/// <summary>
/// The ICH E2B(R3) conformance and business rules an <see cref="IcsrReport"/> must meet before it is rendered: every
/// mandatory element present (ICH ICSR Implementation Guide and its core data elements and business rules), each
/// value within its element's length, code list and date/time precision, no date in the future, and the rules that
/// span elements (one primary source for regulatory purposes, one MedDRA version per ICSR, at least one suspect,
/// interacting or not-administered drug, study identification for a report from a study). Errors are keyed by the
/// JSON path of the offending member, and each message names the E2B(R3) element. Whether a MedDRA code exists needs
/// the licensed MedDRA dictionary, so only its form (eight digits) is checked.
/// </summary>
public static partial class IcsrConformance
{
    private static readonly string[] ReportTypes = ["1", "2", "3", "4"];
    private static readonly string[] FirstSenders = ["1", "2"];
    private static readonly string[] Qualifications = ["1", "2", "3", "4", "5", "UNK"];
    private static readonly string[] SenderTypes = ["1", "2", "3", "4", "5", "6", "7"];
    private static readonly string[] StudyTypes = ["1", "2", "3"];
    private static readonly string[] Sexes = ["1", "2"];
    private static readonly string[] AgeUnits = ["a", "mo", "wk", "d", "h", "{decade}"];
    private static readonly string[] Outcomes = ["0", "1", "2", "3", "4", "5"];
    private static readonly string[] DrugRoles = ["1", "2", "3", "4"];
    private static readonly string[] ReportableDrugRoles = ["1", "3", "4"];
    private static readonly string[] ActionsTaken = ["0", "1", "2", "3", "4", "9"];

    // C.1.1 and C.1.8.1: "country code-company or regulator name-report number", with a two-letter country code.
    [GeneratedRegex(@"^[A-Z]{2}-[^-]+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex CaseIdentifierPattern();

    [GeneratedRegex(@"^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryCodePattern();

    [GeneratedRegex(@"^\d{1,2}\.\d$", RegexOptions.CultureInvariant)]
    private static partial Regex MeddraVersionPattern();

    [GeneratedRegex(@"^\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex MeddraCodePattern();

    [GeneratedRegex(@"^[a-z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    [GeneratedRegex(@"^\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex RouteCodePattern();

    /// <summary>
    /// The problems of <paramref name="report"/> as of <paramref name="now"/>, by JSON path; empty when it can be
    /// rendered.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Check(IcsrReport? report, DateTimeOffset now)
    {
        var errors = new Errors();
        if (report is null)
        {
            errors.Add("$", "An ICSR report body is required.");
            return errors.ToDictionary();
        }

        // A JSON body can omit any member, so every reference is checked even where the record type says non-null.
        CheckHeader(errors, report.Header, now);
        CheckSafetyReport(errors, report.SafetyReport, now);
        CheckPrimarySources(errors, report.PrimarySources);
        CheckSender(errors, report.Sender);
        CheckStudy(errors, report.Study, report.SafetyReport?.ReportType);
        CheckPatient(errors, report.Patient, now);
        CheckReactions(errors, report.Reactions, now);
        CheckDrugs(errors, report.Drugs);
        errors.Text(report.Narrative, "narrative", "H.1 case narrative", 100_000, required: true);
        return errors.ToDictionary();
    }

    private static void CheckHeader(Errors errors, IcsrHeader? header, DateTimeOffset now)
    {
        if (header is null)
        {
            errors.Add("header", "The batch and message header (N.1, N.2) is required.");
            return;
        }

        errors.Text(header.BatchNumber, "header.batchNumber", "N.1.2 batch number", 100, required: true);
        errors.Text(header.BatchSenderIdentifier, "header.batchSenderIdentifier", "N.1.3 batch sender identifier", 60, required: true);
        errors.Text(header.BatchReceiverIdentifier, "header.batchReceiverIdentifier", "N.1.4 batch receiver identifier", 60, required: true);
        errors.Timestamp(header.BatchTransmissionDate, "header.batchTransmissionDate", "N.1.5 date of batch transmission", Hl7TimestampPrecision.Second, now, required: true);
        errors.Text(header.MessageIdentifier, "header.messageIdentifier", "N.2.r.1 message identifier", 100, required: true);
        errors.Text(header.MessageSenderIdentifier, "header.messageSenderIdentifier", "N.2.r.2 message sender identifier", 60, required: true);
        errors.Text(header.MessageReceiverIdentifier, "header.messageReceiverIdentifier", "N.2.r.3 message receiver identifier", 60, required: true);
    }

    private static void CheckSafetyReport(Errors errors, IcsrSafetyReport? report, DateTimeOffset now)
    {
        if (report is null)
        {
            errors.Add("safetyReport", "The case safety report identification (C.1) is required.");
            return;
        }

        if (errors.Text(report.SenderSafetyReportId, "safetyReport.senderSafetyReportId", "C.1.1 sender's safety report unique identifier", 100, required: true))
        {
            errors.Pattern(report.SenderSafetyReportId, "safetyReport.senderSafetyReportId", "C.1.1 sender's safety report unique identifier", CaseIdentifierPattern(), "country code-sender name-report number, with a two-letter country code");
        }

        errors.Timestamp(report.CreationDate, "safetyReport.creationDate", "C.1.2 date of creation", Hl7TimestampPrecision.Second, now, required: true);
        errors.Code(report.ReportType, "safetyReport.reportType", "C.1.3 type of report", ReportTypes, required: true);
        errors.Timestamp(report.FirstReceivedDate, "safetyReport.firstReceivedDate", "C.1.4 date report was first received from source", Hl7TimestampPrecision.Day, now, required: true);
        errors.Timestamp(report.MostRecentInformationDate, "safetyReport.mostRecentInformationDate", "C.1.5 date of most recent information", Hl7TimestampPrecision.Day, now, required: true);
        if (report.FulfilsLocalCriteriaForExpeditedReport is null)
        {
            errors.Add("safetyReport.fulfilsLocalCriteriaForExpeditedReport", "C.1.7 (does this case fulfil the local criteria for an expedited report?) is mandatory: true or false.");
        }

        if (errors.Text(report.WorldwideUniqueCaseId, "safetyReport.worldwideUniqueCaseId", "C.1.8.1 worldwide unique case identification", 100, required: true))
        {
            errors.Pattern(report.WorldwideUniqueCaseId, "safetyReport.worldwideUniqueCaseId", "C.1.8.1 worldwide unique case identification", CaseIdentifierPattern(), "country code-sender name-report number, with a two-letter country code");
        }

        errors.Code(report.FirstSender, "safetyReport.firstSender", "C.1.8.2 first sender of this case", FirstSenders, required: true);

        for (int i = 0; i < (report.DocumentsHeldBySender?.Count ?? 0); i++)
        {
            errors.Text(report.DocumentsHeldBySender![i], $"safetyReport.documentsHeldBySender[{i}]", "C.1.6.1.r.1 documents held by sender", 2000, required: true);
        }

        for (int i = 0; i < (report.OtherCaseIdentifiers?.Count ?? 0); i++)
        {
            var other = report.OtherCaseIdentifiers![i];
            string path = $"safetyReport.otherCaseIdentifiers[{i}]";
            errors.Text(other?.Source, $"{path}.source", "C.1.9.1.r.1 source of the case identifier", 100, required: true);
            errors.Text(other?.CaseIdentifier, $"{path}.caseIdentifier", "C.1.9.1.r.2 case identifier", 100, required: true);
        }
    }

    private static void CheckPrimarySources(Errors errors, IReadOnlyList<IcsrPrimarySource>? sources)
    {
        if (sources is not { Count: > 0 })
        {
            errors.Add("primarySources", "At least one primary source of information (C.2.r) is required.");
            return;
        }

        for (int i = 0; i < sources.Count; i++)
        {
            string path = $"primarySources[{i}]";
            if (sources[i] is not { } source)
            {
                errors.Add(path, "A primary source (C.2.r) cannot be null.");
                continue;
            }

            errors.Code(source.Qualification, $"{path}.qualification", "C.2.r.4 qualification", Qualifications, required: true);
            errors.Pattern(source.CountryCode, $"{path}.countryCode", "C.2.r.3 reporter's country code", CountryCodePattern(), "an ISO 3166-1 alpha-2 code");
        }

        if (sources.Count(s => s is { PrimaryForRegulatoryPurposes: true }) != 1)
        {
            errors.Add("primarySources", "Exactly one primary source must be flagged as primary for regulatory purposes (C.2.r.5).");
        }
    }

    private static void CheckSender(Errors errors, IcsrSender? sender)
    {
        if (sender is null)
        {
            errors.Add("sender", "The sender of the case safety report (C.3) is required.");
            return;
        }

        errors.Code(sender.SenderType, "sender.senderType", "C.3.1 sender type", SenderTypes, required: true);
        errors.Text(sender.Organisation, "sender.organisation", "C.3.2 sender's organisation", 100, required: sender.SenderType != "7");
    }

    private static void CheckStudy(Errors errors, IcsrStudy? study, string? reportType)
    {
        if (study is null)
        {
            if (reportType == "2")
            {
                errors.Add("study", "Study identification (C.5) with a study type (C.5.4) is required for a report from a study (C.1.3 = 2).");
            }

            return;
        }

        errors.Code(study.StudyType, "study.studyType", "C.5.4 study type", StudyTypes, required: true);
        errors.Text(study.SponsorStudyNumber, "study.sponsorStudyNumber", "C.5.3 sponsor study number", 50, required: false);
        errors.Text(study.StudyName, "study.studyName", "C.5.2 study name", 2000, required: false);
    }

    private static void CheckPatient(Errors errors, IcsrPatient? patient, DateTimeOffset now)
    {
        if (patient is null)
        {
            errors.Add("patient", "The patient characteristics (D) are required.");
            return;
        }

        errors.Text(patient.PatientId, "patient.patientId", "D.1.1.4 investigation number", 20, required: false);
        errors.Code(patient.Sex, "patient.sex", "D.5 sex", Sexes, required: false);
        errors.Timestamp(patient.BirthDate, "patient.birthDate", "D.2.1 date of birth", Hl7TimestampPrecision.Day, now, required: false);
        if (patient.Age is { } age)
        {
            if (age is < 0 or > 99_999)
            {
                errors.Add("patient.age", "D.2.2a age at onset is a number of up to five digits.");
            }

            errors.Code(patient.AgeUnit, "patient.ageUnit", "D.2.2b age unit", AgeUnits, required: true);
        }
        else if (patient.AgeUnit is not null)
        {
            errors.Add("patient.ageUnit", "D.2.2b age unit is given without D.2.2a age.");
        }
    }

    private static void CheckReactions(Errors errors, IReadOnlyList<IcsrReaction>? reactions, DateTimeOffset now)
    {
        if (reactions is not { Count: > 0 })
        {
            errors.Add("reactions", "At least one reaction or event (E.i) is required.");
            return;
        }

        for (int i = 0; i < reactions.Count; i++)
        {
            string path = $"reactions[{i}]";
            if (reactions[i] is not { } reaction)
            {
                errors.Add(path, "A reaction (E.i) cannot be null.");
                continue;
            }

            if (reaction.MeddraCoding is not { } coding)
            {
                errors.Add($"{path}.meddraCoding", "E.i.2.1 MedDRA coding of the reaction (version and lowest level term code) is mandatory.");
            }
            else
            {
                if (errors.Text(coding.Version, $"{path}.meddraCoding.version", "E.i.2.1a MedDRA version", 4, required: true))
                {
                    errors.Pattern(coding.Version, $"{path}.meddraCoding.version", "E.i.2.1a MedDRA version", MeddraVersionPattern(), "a version such as 27.1");
                }

                if (errors.Text(coding.LltCode, $"{path}.meddraCoding.lltCode", "E.i.2.1b MedDRA code", 8, required: true))
                {
                    errors.Pattern(coding.LltCode, $"{path}.meddraCoding.lltCode", "E.i.2.1b MedDRA code", MeddraCodePattern(), "an eight-digit MedDRA lowest level term code");
                }
            }

            errors.Text(reaction.ReactionTerm, $"{path}.reactionTerm", "E.i.1.1a reaction as reported", 250, required: false);
            if (reaction.ReactionTerm is not null)
            {
                // E.i.1.1b: optional, but required if E.i.1.1a is populated.
                errors.Code(reaction.ReactionTermLanguage, $"{path}.reactionTermLanguage", "E.i.1.1b language of the reported reaction", null, required: true);
                errors.Pattern(reaction.ReactionTermLanguage, $"{path}.reactionTermLanguage", "E.i.1.1b language of the reported reaction", LanguagePattern(), "an ISO 639-2 alpha-3 code such as eng");
            }
            else if (reaction.ReactionTermLanguage is not null)
            {
                errors.Add($"{path}.reactionTermLanguage", "E.i.1.1b language is given without E.i.1.1a reaction as reported.");
            }

            errors.Code(reaction.Outcome, $"{path}.outcome", "E.i.7 outcome of the reaction", Outcomes, required: true);
            errors.Timestamp(reaction.StartDate, $"{path}.startDate", "E.i.4 date of start", Hl7TimestampPrecision.Year, now, required: false);
            errors.Timestamp(reaction.EndDate, $"{path}.endDate", "E.i.5 date of end", Hl7TimestampPrecision.Year, now, required: false);
        }

        var versions = reactions.Select(r => r?.MeddraCoding?.Version).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
        if (versions.Count > 1)
        {
            errors.Add("reactions", $"Only one MedDRA version is allowed per ICSR (E.i.2.1a); the reactions use {string.Join(", ", versions)}.");
        }
    }

    private static void CheckDrugs(Errors errors, IReadOnlyList<IcsrDrug>? drugs)
    {
        if (drugs is not { Count: > 0 })
        {
            errors.Add("drugs", "At least one drug (G.k) is required.");
            return;
        }

        for (int i = 0; i < drugs.Count; i++)
        {
            string path = $"drugs[{i}]";
            if (drugs[i] is not { } drug)
            {
                errors.Add(path, "A drug (G.k) cannot be null.");
                continue;
            }

            errors.Code(drug.DrugRole, $"{path}.drugRole", "G.k.1 characterisation of drug role", DrugRoles, required: true);
            errors.Text(drug.DrugName, $"{path}.drugName", "G.k.2.2 medicinal product name as reported", 250, required: true);
            errors.Text(drug.ActiveSubstanceName, $"{path}.activeSubstanceName", "G.k.2.3.r.1 substance name", 250, required: false);
            errors.Text(drug.DosageText, $"{path}.dosageText", "G.k.4.r.8 dosage text", 2000, required: false);
            errors.Pattern(drug.RouteOfAdministration, $"{path}.routeOfAdministration", "G.k.4.r.10.2b route of administration", RouteCodePattern(), "a three-digit code of ICH E2B code list 14");
            errors.Code(drug.ActionTakenWithDrug, $"{path}.actionTakenWithDrug", "G.k.8 action taken with drug", ActionsTaken, required: false);
        }

        if (!drugs.Any(d => d?.DrugRole is { } role && ReportableDrugRoles.Contains(role, StringComparer.Ordinal)))
        {
            errors.Add("drugs", "At least one drug must be suspect (1), interacting (3) or not administered (4) (G.k.1).");
        }
    }

    private sealed class Errors
    {
        private readonly SortedDictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

        public void Add(string path, string message)
        {
            if (!_errors.TryGetValue(path, out var messages))
            {
                messages = [];
                _errors[path] = messages;
            }

            messages.Add(message);
        }

        /// <summary>Checks a free-text element; true when it is present and within its length.</summary>
        public bool Text(string? value, string path, string element, int maxLength, bool required)
        {
            if (value is null)
            {
                if (required)
                {
                    Add(path, $"{element} is mandatory and is missing.");
                }

                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                Add(path, required ? $"{element} is mandatory and is blank." : $"{element} is blank; leave it out instead.");
                return false;
            }

            if (value.Length > maxLength)
            {
                Add(path, $"{element} is longer than its {maxLength} characters.");
                return false;
            }

            return true;
        }

        public void Code(string? value, string path, string element, string[]? allowed, bool required)
        {
            if (value is null)
            {
                if (required)
                {
                    Add(path, $"{element} is mandatory and is missing.");
                }

                return;
            }

            if (allowed is not null && !allowed.Contains(value, StringComparer.Ordinal))
            {
                Add(path, $"{element} must be one of {string.Join(", ", allowed)}.");
            }
        }

        public void Pattern(string? value, string path, string element, Regex pattern, string expected)
        {
            if (value is not null && !pattern.IsMatch(value))
            {
                Add(path, $"{element} must be {expected}.");
            }
        }

        public void Timestamp(string? value, string path, string element, Hl7TimestampPrecision minimum, DateTimeOffset now, bool required)
        {
            if (value is null)
            {
                if (required)
                {
                    Add(path, $"{element} is mandatory and is missing.");
                }

                return;
            }

            if (Hl7Timestamp.Problem(value, minimum, now) is { } problem)
            {
                Add(path, $"{element} {problem}.");
            }
        }

        public Dictionary<string, string[]> ToDictionary() =>
            _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
    }
}
