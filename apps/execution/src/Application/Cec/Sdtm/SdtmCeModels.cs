using System;
using System.Collections.Generic;

namespace Cadence.Apps.Execution.Application.Cec.Sdtm;

/// <summary>
/// Represents a standard CDISC SDTM CE (Clinical Events) domain record compiled from a finalized CEC dossier.
/// @req:PRD-CEC-003
/// </summary>
public sealed record SdtmCeRecord(
    string STUDYID,
    string DOMAIN,
    string USUBJID,
    int CESEQ,
    string CETERM,
    string CEDECOD,
    string CECAT,
    string? CESCAT,
    string CEPRESP,
    string CEOCCUR,
    string CESTDTC,
    string CECADJ);

/// <summary>
/// Represents a CDISC SDTM SUPPCE (Supplemental Qualifiers for Clinical Events) domain record.
/// @req:PRD-CEC-003
/// </summary>
public sealed record SuppCeRecord(
    string STUDYID,
    string RDOMAIN,
    string USUBJID,
    string IDVAR,
    string IDVARVAL,
    string QNAM,
    string QLABEL,
    string QVAL);

/// <summary>
/// Result container holding compiled SDTM CE and SUPPCE domain records.
/// @req:PRD-CEC-003
/// </summary>
public sealed record SdtmCeDataset(
    List<SdtmCeRecord> CeRecords,
    List<SuppCeRecord> SuppCeRecords);
