using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Cadence.UnitTests.Safety;

/// <summary>
/// A complete ICH E2B(R3) ICSR request body for <c>POST /api/v1/safety/icsr/render</c> (#5573): every mandatory element
/// and a few optional ones, all dated in 2025 so none is in the future. Synthetic data only; the MedDRA codes are only
/// checked for their form, not looked up (the MedDRA dictionary is licensed and not in the repository).
/// </summary>
public static class IcsrFixture
{
    /// <summary>A fresh copy of the complete report, safe to modify.</summary>
    public static JsonNode CompleteReport() => JsonNode.Parse(
        """
        {
          "header": {
            "batchNumber": "BATCH-FIX-0001",
            "batchSenderIdentifier": "CADENCE-FIX-SENDER",
            "batchReceiverIdentifier": "FIX-RECEIVER",
            "batchTransmissionDate": "20251020091500+0000",
            "messageIdentifier": "MSG-FIX-0001",
            "messageSenderIdentifier": "CADENCE-FIX-SENDER",
            "messageReceiverIdentifier": "FIX-RECEIVER"
          },
          "safetyReport": {
            "senderSafetyReportId": "XX-CADENCE-FIX-0001",
            "creationDate": "20251020090000+0000",
            "reportType": "2",
            "firstReceivedDate": "20251015",
            "mostRecentInformationDate": "20251018",
            "fulfilsLocalCriteriaForExpeditedReport": true,
            "worldwideUniqueCaseId": "XX-CADENCE-FIX-0001",
            "firstSender": "2",
            "documentsHeldBySender": ["Hospital discharge summary"],
            "otherCaseIdentifiers": [{ "source": "Fixture registry", "caseIdentifier": "XX-FIXREG-0042" }]
          },
          "primarySources": [{ "qualification": "1", "countryCode": "NL", "primaryForRegulatoryPurposes": true }],
          "sender": { "senderType": "1", "organisation": "Cadence Fixture Sponsor" },
          "study": { "studyType": "1", "sponsorStudyNumber": "STUDY-FIX", "studyName": "Fixture study" },
          "patient": { "patientId": "PT-FIX-0001", "sex": "2", "age": 54, "ageUnit": "a" },
          "reactions": [
            {
              "meddraCoding": { "version": "27.1", "lltCode": "10019663" },
              "reactionTerm": "Hepatic failure",
              "reactionTermLanguage": "eng",
              "startDate": "20250619",
              "outcome": "5",
              "resultsInDeath": true,
              "causedOrProlongedHospitalisation": true
            },
            {
              "meddraCoding": { "version": "27.1", "lltCode": "10028813" },
              "reactionTerm": "Nausea",
              "reactionTermLanguage": "eng",
              "startDate": "20250414",
              "endDate": "20250420",
              "outcome": "1"
            }
          ],
          "drugs": [
            {
              "drugRole": "1",
              "drugName": "Fixture-Compound 10 mg tablet",
              "activeSubstanceName": "Fixture-Compound",
              "dosageText": "10 mg once daily",
              "routeOfAdministration": "048",
              "actionTakenWithDrug": "1"
            }
          ],
          "narrative": "Synthetic fixture case: a trial subject developed nausea, then hepatic failure, and died."
        }
        """)!;

    /// <summary>
    /// The smallest report the route accepts: only the mandatory elements, a spontaneous report (so no study), no
    /// optional patient data.
    /// </summary>
    public static JsonNode MinimalReport()
    {
        var report = CompleteReport();
        var safetyReport = report["safetyReport"]!.AsObject();
        safetyReport["reportType"] = "1";
        safetyReport.Remove("documentsHeldBySender");
        safetyReport.Remove("otherCaseIdentifiers");
        report["primarySources"]![0]!.AsObject().Remove("countryCode");
        report.AsObject().Remove("study");
        report["patient"] = new JsonObject();
        foreach (var reaction in report["reactions"]!.AsArray())
        {
            foreach (string optional in new[] { "reactionTerm", "reactionTermLanguage", "startDate", "endDate" })
            {
                reaction!.AsObject().Remove(optional);
            }
        }

        report["drugs"] = new JsonArray(new JsonObject { ["drugRole"] = "1", ["drugName"] = "Fixture-Compound" });
        return report;
    }

    /// <summary>
    /// <paramref name="report"/> with <paramref name="path"/> (dotted, with <c>[i]</c> indexes) removed, or with
    /// <paramref name="blank"/> set blank: whitespace for a string, null for an object or a boolean, empty for an array.
    /// </summary>
    public static JsonNode Without(JsonNode report, string path, bool blank)
    {
        var (parent, last) = Locate(report, path);
        var target = parent.AsObject();
        if (!blank)
        {
            target.Remove(last);
            return report;
        }

        target[last] = target[last] switch
        {
            JsonArray => new JsonArray(),
            JsonObject => null,
            JsonValue value when value.TryGetValue(out bool _) => null,
            _ => "   ",
        };
        return report;
    }

    /// <summary><paramref name="report"/> with <paramref name="path"/> set to <paramref name="value"/>.</summary>
    public static JsonNode With(JsonNode report, string path, JsonNode? value)
    {
        var (parent, last) = Locate(report, path);
        parent.AsObject()[last] = value;
        return report;
    }

    private static (JsonNode Parent, string Last) Locate(JsonNode report, string path)
    {
        string[] segments = path.Split('.');
        JsonNode parent = report;
        foreach (string segment in segments[..^1])
        {
            parent = Step(parent, segment);
        }

        return (parent, segments[^1]);
    }

    private static JsonNode Step(JsonNode node, string segment)
    {
        int bracket = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracket < 0)
        {
            return node[segment]!;
        }

        int index = int.Parse(segment[(bracket + 1)..^1], CultureInfo.InvariantCulture);
        return node[segment[..bracket]]![index]!;
    }
}
