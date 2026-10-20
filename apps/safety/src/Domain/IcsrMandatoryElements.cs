using System;
using System.Collections.Generic;

namespace Cadence.Apps.Safety.Domain;

/// <summary>
/// The elements an ICSR cannot be transmitted without (ICH E2B(R3): N.1.2 batch number, N.1.3 sender, N.1.4 receiver,
/// N.1.5 transmission date, C.1.8.1 worldwide unique case identifier, D.1 patient, E.i.1 at least one reaction as
/// reported, G.k.2 at least one suspect drug). The render route reports each one that is missing instead of writing an
/// empty element (#5464).
/// </summary>
public static class IcsrMandatoryElements
{
    /// <summary>
    /// The rendered element path of each mandatory element <paramref name="report"/> leaves out or blank, with why it is
    /// needed; empty when the report has them all.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Missing(IcsrReport? report)
    {
        var missing = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (report is null)
        {
            missing["ichicsr"] = ["An ICSR report body is required."];
            return missing;
        }

        // A JSON body can omit any member, so every reference is checked even where the record type says non-null.
        Require(missing, report.Header?.MessageId, "header/message_id", "header.messageId (N.1.2 batch number)");
        Require(missing, report.Header?.SenderOrganization, "header/sender_organization", "header.senderOrganization (N.1.3 sender)");
        Require(missing, report.Header?.ReceiverOrganization, "header/receiver_organization", "header.receiverOrganization (N.1.4 receiver)");
        Require(missing, report.Header?.TransmissionDate, "header/transmission_date", "header.transmissionDate (N.1.5 transmission date)");
        Require(
            missing,
            report.ReportIdentifiers?.WorldwideUniqueCaseId,
            "safety_report/worldwide_unique_case_id",
            "reportIdentifiers.worldwideUniqueCaseId (C.1.8.1 worldwide unique case identifier)");
        Require(missing, report.Patient?.PatientId, "patient/patient_id", "patient.patientId (D.1 patient)");

        if (report.Reactions is not { Count: > 0 })
        {
            missing["reactions/reaction"] = ["At least one reaction (E.i) is required."];
        }
        else
        {
            for (int i = 0; i < report.Reactions.Count; i++)
            {
                Require(missing, report.Reactions[i]?.ReactionTerm, $"reactions/reaction[{i + 1}]/reaction_term", $"reactions[{i}].reactionTerm (E.i.1 reaction as reported)");
            }
        }

        if (report.SuspectDrugs is not { Count: > 0 })
        {
            missing["suspect_drugs/suspect_drug"] = ["At least one suspect drug (G.k) is required."];
        }
        else
        {
            for (int i = 0; i < report.SuspectDrugs.Count; i++)
            {
                Require(missing, report.SuspectDrugs[i]?.DrugName, $"suspect_drugs/suspect_drug[{i + 1}]/drug_name", $"suspectDrugs[{i}].drugName (G.k.2 drug identification)");
            }
        }

        return missing;
    }

    private static void Require(SortedDictionary<string, string[]> missing, string? value, string element, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing[element] = [$"{field} is mandatory and is missing."];
        }
    }
}
