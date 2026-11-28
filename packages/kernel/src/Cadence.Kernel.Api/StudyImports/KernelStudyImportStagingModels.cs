using System;
using System.Collections.Generic;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Kernel.Api.StudyImports;

internal sealed record ParsedImportInput(
    byte[]? RawBytes,
    string? FileName,
    ProtocolImportFormat Format,
    IReadOnlyList<string>? TemplateIds,
    bool ReplaceForms,
    IReadOnlyList<TemplateActivityAssignment>? TemplateAssignments,
    AmendmentClassification? AmendmentClassification,
    TransitionPolicy? TransitionPolicy,
    ProblemHttpResult? Problem)
{
    public static ParsedImportInput Fail(ProblemHttpResult problem) =>
        new(null, null, ProtocolImportFormat.Auto, null, false, null, null, null, problem);
}

internal sealed record StagedCodeSystem(
    CodeSystemId CodeSystemId,
    string SystemUri,
    LicenseClass License);

internal sealed record StagedCodeSystemVersion(
    CodeSystemVersionId VersionId,
    CodeSystemId CodeSystemId,
    string SystemUri,
    string VersionLabel,
    DateOnly ReleasedOn,
    TerminologyPackage Package,
    IReadOnlyList<CodeSystemEntryData> Entries);

internal sealed record DraftMutationPlan(
    ProtocolDraftImportMode Mode,
    ProtocolVersionId TargetVersionId,
    ProtocolVersionId? ParentVersionId,
    ProtocolDesign? BaseDesign,
    string? ParentContent,
    ProtocolAmendment? ExistingAmendment,
    bool CreatedNewVersion,
    string? AmendmentIntent,
    ProblemHttpResult? Problem)
{
    public static DraftMutationPlan Fail(ProblemHttpResult problem) =>
        new(ProtocolDraftImportMode.CreateDraft, default, null, null, null, null, false, null, problem);
}
