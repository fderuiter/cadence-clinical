using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Cec;

namespace Cadence.Apps.Execution.Application.Cec.Sdtm;

/// <summary>
/// Compiler service that transforms finalized CEC dossier determinations into standard CDISC SDTM CE and SUPPCE domain records.
/// @req:PRD-CEC-003
/// </summary>
public static class CdiscSdtmCeCompiler
{
    public static SdtmCeDataset Compile(IEnumerable<CecDossier> finalizedDossiers)
    {
        ArgumentNullException.ThrowIfNull(finalizedDossiers);

        var ceRecords = new List<SdtmCeRecord>();
        var suppCeRecords = new List<SuppCeRecord>();

        var subjectSequenceCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var dossier in finalizedDossiers)
        {
            if (dossier.WorkflowStatus is not ("FINALIZED_CONCORDANT" or "FINALIZED_BY_CHAIR" or "CONSENSUS_REACHED"))
            {
                continue;
            }

            string usubjid = $"{dossier.StudyId}-{dossier.SubjectId}";
            if (!subjectSequenceCounters.TryGetValue(usubjid, out int currentSeq))
            {
                currentSeq = 0;
            }
            currentSeq++;
            subjectSequenceCounters[usubjid] = currentSeq;

            string ceDecod = dossier.FinalConsensusClassification ?? dossier.CandidateEventType;
            bool isRefuted = string.Equals(ceDecod, "NOT_AN_ENDPOINT", StringComparison.OrdinalIgnoreCase);
            string ceOccur = isRefuted ? "N" : "Y";
            string ceStdDtc = dossier.FinalAdjudicatedOnsetDate ?? dossier.ReportedOnsetDate;

            var ceRecord = new SdtmCeRecord(
                STUDYID: dossier.StudyId,
                DOMAIN: "CE",
                USUBJID: usubjid,
                CESEQ: currentSeq,
                CETERM: dossier.ReportedTerm,
                CEDECOD: ceDecod,
                CECAT: dossier.CandidateEventType,
                CESCAT: dossier.FinalConsensusSubtype,
                CEPRESP: "Y",
                CEOCCUR: ceOccur,
                CESTDTC: ceStdDtc,
                CECADJ: "FINALIZED");

            ceRecords.Add(ceRecord);

            string seqStr = currentSeq.ToString();

            if (!string.IsNullOrWhiteSpace(dossier.AdjudicationRationale))
            {
                suppCeRecords.Add(new SuppCeRecord(
                    STUDYID: dossier.StudyId,
                    RDOMAIN: "CE",
                    USUBJID: usubjid,
                    IDVAR: "CESEQ",
                    IDVARVAL: seqStr,
                    QNAM: "CERATNL",
                    QLABEL: "Adjudication Rationale",
                    QVAL: dossier.AdjudicationRationale));
            }

            if (dossier.SignatureManifest != null)
            {
                suppCeRecords.Add(new SuppCeRecord(
                    STUDYID: dossier.StudyId,
                    RDOMAIN: "CE",
                    USUBJID: usubjid,
                    IDVAR: "CESEQ",
                    IDVARVAL: seqStr,
                    QNAM: "CESIGNER",
                    QLABEL: "Chair Signer Name",
                    QVAL: dossier.SignatureManifest.SignerName));

                suppCeRecords.Add(new SuppCeRecord(
                    STUDYID: dossier.StudyId,
                    RDOMAIN: "CE",
                    USUBJID: usubjid,
                    IDVAR: "CESEQ",
                    IDVARVAL: seqStr,
                    QNAM: "CEHASH",
                    QLABEL: "Signature Payload SHA256 Hash",
                    QVAL: dossier.SignatureManifest.PayloadHash));
            }

            if (!string.IsNullOrWhiteSpace(dossier.MaskedSiteId))
            {
                suppCeRecords.Add(new SuppCeRecord(
                    STUDYID: dossier.StudyId,
                    RDOMAIN: "CE",
                    USUBJID: usubjid,
                    IDVAR: "CESEQ",
                    IDVARVAL: seqStr,
                    QNAM: "BLINDSITE",
                    QLABEL: "Blinded Site Identifier Token",
                    QVAL: dossier.MaskedSiteId));
            }
        }

        return new SdtmCeDataset(ceRecords, suppCeRecords);
    }
}
