using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Api;

/// <summary>Visible study entry returned by <c>GET /api/v1/kernel/studies</c> and <c>POST /api/v1/kernel/studies</c>.</summary>
/// <param name="Ref">Study <c>NodeRef</c> pinned to its current version.</param>
/// <param name="Code">Unique study code in the tenant.</param>
/// <param name="Title">Study display title.</param>
/// <param name="Phase">Clinical phase label.</param>
/// <param name="SponsorRef">Sponsor organization <c>NodeRef</c>.</param>
/// <param name="SponsorCode">Sponsor organization code.</param>
/// <param name="SponsorName">Sponsor organization display name.</param>
/// <param name="SiteCount">Number of linked study sites.</param>
/// <param name="LinkedSite">Optional study-site link created in one step during <c>POST /api/v1/kernel/studies</c>.</param>
public sealed record KernelStudySummaryResponse(
    string Ref,
    string Code,
    string Title,
    string Phase,
    string SponsorRef,
    string SponsorCode,
    string SponsorName,
    int SiteCount,
    KernelStudySiteSummaryResponse? LinkedSite = null)
{
    internal static KernelStudySummaryResponse From(StudyDirectoryEntry entry) =>
        From(entry, linkedSite: null);

    internal static KernelStudySummaryResponse From(StudyDirectoryEntry entry, KernelStudySiteSummaryResponse? linkedSite) =>
        new(
            NodeRef.For(entry.StudyId, entry.VersionIndex).ToString(),
            entry.StudyCode,
            entry.Title,
            entry.Phase,
            NodeRef.For(entry.SponsorId, entry.SponsorVersionIndex).ToString(),
            entry.SponsorCode,
            entry.SponsorName,
            entry.SiteCount,
            linkedSite);
}

/// <summary>Visible organization entry returned by <c>GET /api/v1/kernel/organizations</c> and <c>POST /api/v1/kernel/organizations</c>.</summary>
/// <param name="Ref">Organization <c>NodeRef</c> pinned to its current version.</param>
/// <param name="Code">Organization code.</param>
/// <param name="Name">Organization display name.</param>
/// <param name="Kind">Organization kind (<c>Sponsor</c>, <c>Cro</c>, <c>SiteInstitution</c>, <c>Vendor</c>, …).</param>
public sealed record KernelOrganizationSummaryResponse(
    string Ref,
    string Code,
    string Name,
    string Kind)
{
    internal static KernelOrganizationSummaryResponse From(OrganizationDirectoryEntry entry) =>
        new(
            NodeRef.For(entry.OrganizationId, entry.VersionIndex).ToString(),
            entry.Code,
            entry.Name,
            entry.Kind.ToString());
}

/// <summary>Request body for <c>POST /api/v1/kernel/organizations</c>.</summary>
/// <param name="Name">Organization display name.</param>
/// <param name="Code">Optional unique organization code in the tenant; derived from <paramref name="Name"/> when omitted.</param>
/// <param name="Kind">Organization kind (defaults to <c>SiteInstitution</c>).</param>
public sealed record KernelCreateOrganizationRequest(
    string Name,
    string? Code = null,
    string? Kind = null);

/// <summary>Visible investigational site entry returned by <c>GET /api/v1/kernel/sites</c> and <c>POST /api/v1/kernel/sites</c>.</summary>
/// <param name="Ref">Site <c>NodeRef</c> pinned to its current version.</param>
/// <param name="Code">Unique site code.</param>
/// <param name="Name">Site display name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
/// <param name="InstitutionRef">Owning site institution <c>NodeRef</c>.</param>
/// <param name="InstitutionCode">Owning site institution code.</param>
/// <param name="InstitutionName">Owning site institution display name.</param>
/// <param name="IsActive">Whether the investigational site is active.</param>
/// <param name="LinkedStudyCount">Number of studies linked to this site.</param>
public sealed record KernelSiteSummaryResponse(
    string Ref,
    string Code,
    string Name,
    string Country,
    string InstitutionRef,
    string InstitutionCode,
    string InstitutionName,
    bool IsActive = true,
    int LinkedStudyCount = 0)
{
    internal static KernelSiteSummaryResponse From(SiteDirectoryEntry entry) =>
        new(
            NodeRef.For(entry.SiteId, entry.VersionIndex).ToString(),
            entry.SiteCode,
            entry.Name,
            entry.Country,
            NodeRef.For(entry.InstitutionId, entry.InstitutionVersionIndex).ToString(),
            entry.InstitutionCode,
            entry.InstitutionName,
            entry.IsActive,
            entry.LinkedStudyCount);
}

/// <summary>Request body for <c>POST /api/v1/kernel/sites</c>.</summary>
/// <param name="SiteCode">Unique site code within the institution/tenant.</param>
/// <param name="Name">Investigational site display name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code (two uppercase ASCII letters, e.g. <c>US</c>, <c>NL</c>).</param>
/// <param name="InstitutionRef">Existing site institution <c>NodeRef</c>, when selecting an existing institution.</param>
/// <param name="InstitutionCode">Site institution code, when matching or registering an institution.</param>
/// <param name="InstitutionName">Site institution display name, when registering or matching an institution.</param>
public sealed record KernelCreateSiteRequest(
    string SiteCode,
    string Name,
    string Country,
    string? InstitutionRef = null,
    string? InstitutionCode = null,
    string? InstitutionName = null);

/// <summary>Visible study-site association returned by <c>GET /api/v1/kernel/studies/{studyRef}/sites</c> and <c>POST /api/v1/kernel/studies/{studyRef}/sites</c>.</summary>
/// <param name="Ref">StudySite <c>NodeRef</c> pinned to its current version.</param>
/// <param name="StudyRef">Linked study <c>NodeRef</c> pinned to its current version.</param>
/// <param name="StudyCode">Linked study code.</param>
/// <param name="SiteRef">Linked site <c>NodeRef</c> pinned to its current version.</param>
/// <param name="SiteCode">Linked site code.</param>
/// <param name="SiteName">Linked site display name.</param>
/// <param name="Country">Linked site ISO 3166-1 alpha-2 country code.</param>
/// <param name="InstitutionRef">Owning site institution <c>NodeRef</c>.</param>
/// <param name="InstitutionCode">Owning site institution code.</param>
/// <param name="InstitutionName">Owning site institution display name.</param>
/// <param name="Status">Study-site lifecycle status (<c>Selected</c> or <c>Active</c>).</param>
/// <param name="IsActive">Whether the study-site link is active.</param>
public sealed record KernelStudySiteSummaryResponse(
    string Ref,
    string StudyRef,
    string StudyCode,
    string SiteRef,
    string SiteCode,
    string SiteName,
    string Country,
    string InstitutionRef,
    string InstitutionCode,
    string InstitutionName,
    string Status,
    bool IsActive = true)
{
    internal static KernelStudySiteSummaryResponse From(StudySiteDirectoryEntry entry) =>
        new(
            NodeRef.For(entry.StudySiteId, entry.VersionIndex).ToString(),
            NodeRef.For(entry.StudyId, entry.StudyVersionIndex).ToString(),
            entry.StudyCode,
            NodeRef.For(entry.SiteId, entry.SiteVersionIndex).ToString(),
            entry.SiteCode,
            entry.SiteName,
            entry.Country,
            NodeRef.For(entry.InstitutionId, entry.InstitutionVersionIndex).ToString(),
            entry.InstitutionCode,
            entry.InstitutionName,
            entry.Status,
            entry.IsActive);
}

/// <summary>Request body for <c>POST /api/v1/kernel/studies/{studyRef}/sites</c>.</summary>
/// <param name="SiteRef">Existing site <c>NodeRef</c> to link to the study.</param>
/// <param name="SiteCode">Existing site code to link to the study when <paramref name="SiteRef"/> is omitted.</param>
/// <param name="InstitutionRef">Optional institution <c>NodeRef</c> to disambiguate <paramref name="SiteCode"/>.</param>
/// <param name="InstitutionCode">Optional institution code to disambiguate <paramref name="SiteCode"/>.</param>
public sealed record KernelLinkSiteToStudyRequest(
    string? SiteRef = null,
    string? SiteCode = null,
    string? InstitutionRef = null,
    string? InstitutionCode = null);

/// <summary>Request body for <c>POST /api/v1/kernel/studies</c>.</summary>
/// <param name="StudyCode">Unique study code in the tenant.</param>
/// <param name="Title">Study title.</param>
/// <param name="Phase">Clinical phase (for example <c>Phase 1</c>, <c>Phase 2</c>, <c>Phase 3</c>).</param>
/// <param name="SponsorRef">Existing sponsor organization <c>NodeRef</c>, when selecting an existing sponsor.</param>
/// <param name="SponsorCode">Sponsor organization code, when matching or registering a sponsor.</param>
/// <param name="SponsorName">Sponsor organization display name, when registering or matching a sponsor.</param>
/// <param name="LinkSandboxSite">True to link the workspace's default sandbox site to the new study in one step.</param>
/// <param name="InitialSiteRef">Optional explicit site <c>NodeRef</c> to link to the new study on creation.</param>
/// <param name="InitialSiteCode">Optional explicit site code to link to the new study on creation.</param>
public sealed record KernelCreateStudyRequest(
    string StudyCode,
    string Title,
    string Phase,
    string? SponsorRef = null,
    string? SponsorCode = null,
    string? SponsorName = null,
    bool LinkSandboxSite = false,
    string? InitialSiteRef = null,
    string? InitialSiteCode = null);

/// <summary>Bundled standard eCRF template entry returned by <c>GET /api/v1/kernel/protocol-templates</c>.</summary>
/// <param name="Id">Template identifier.</param>
/// <param name="Category">Clinical category name.</param>
/// <param name="FormOid">Form OID in the template.</param>
/// <param name="FormName">Form display title.</param>
/// <param name="FileName">Template file name.</param>
/// <param name="ItemCount">Number of items on the form.</param>
public sealed record KernelProtocolTemplateResponse(
    string Id,
    string Category,
    string FormOid,
    string FormName,
    string FileName,
    int ItemCount)
{
    internal static KernelProtocolTemplateResponse From(BundledProtocolTemplate template) =>
        new(template.Id, template.Category, template.FormOid, template.FormName, template.FileName, template.ItemCount);
}

/// <summary>JSON request body option for <c>POST /api/v1/kernel/studies/{studyRef}/protocol-imports</c>.</summary>
/// <param name="Format">Optional format hint (<c>auto</c>, <c>odm_xml</c>, <c>usdm_json</c>).</param>
/// <param name="FileName">Optional uploaded file name.</param>
/// <param name="Content">Raw ODM v1.3.2 XML or USDM v4 JSON text, when sending JSON instead of multipart upload.</param>
/// <param name="TemplateIds">Selected bundled CDISC Library template identifiers.</param>
/// <param name="ReplaceForms">True to replace existing draft forms instead of merging into an existing draft.</param>
public sealed record KernelImportProtocolJsonRequest(
    string? Format = null,
    string? FileName = null,
    string? Content = null,
    IReadOnlyList<string>? TemplateIds = null,
    bool ReplaceForms = false);

/// <summary>Readable import note attached to a protocol draft summary.</summary>
/// <param name="Source">Source element, OID, or path in the imported file.</param>
/// <param name="Message">Readable explanation of how unmapped terminology or optional vendor attributes were handled.</param>
public sealed record KernelProtocolImportNoteResponse(string Source, string Message)
{
    internal static KernelProtocolImportNoteResponse From(ProtocolImportNote note) =>
        new(note.Source, note.Message);
}

/// <summary>Viewable item inside a protocol draft form summary.</summary>
/// <param name="Id">Item identifier in the protocol design.</param>
/// <param name="DisplayOrder">1-based display position within the form.</param>
/// <param name="Prompt">Preserved human-readable item prompt or question.</param>
/// <param name="Concept">Bound concept version reference (<c>conceptId@version</c>).</param>
/// <param name="Required">True when the item is mandatory.</param>
/// <param name="ValueKind">Expected value kind (<c>Text</c>, <c>Quantity</c>, <c>DateTime</c>, <c>Boolean</c>, <c>Coded</c>).</param>
/// <param name="Unit">UCUM unit when constrained.</param>
public sealed record KernelProtocolFormItemResponse(
    string Id,
    int DisplayOrder,
    string Prompt,
    string Concept,
    bool Required,
    string? ValueKind,
    string? Unit)
{
    internal static KernelProtocolFormItemResponse From(
        CollectionItem item,
        int displayOrder,
        IReadOnlyDictionary<ConceptRef, string> conceptLabels)
    {
        var prompt = !string.IsNullOrWhiteSpace(item.Prompt)
            ? item.Prompt
            : conceptLabels.GetValueOrDefault(item.Concept) ?? item.Id;
        return new(
            item.Id,
            displayOrder,
            prompt,
            item.Concept.ToString(),
            item.Required,
            item.ExpectedKind?.ToString(),
            item.Unit);
    }
}

/// <summary>Viewable form (<see cref="CollectionSpec"/>) inside a protocol draft summary.</summary>
/// <param name="Id">Form identifier in the protocol design.</param>
/// <param name="DisplayOrder">1-based display position among the draft's forms.</param>
/// <param name="Name">Preserved form title.</param>
/// <param name="ItemCount">Number of items on the form.</param>
/// <param name="RequiredItemCount">Number of mandatory items on the form.</param>
/// <param name="Items">Items in preserved display order.</param>
public sealed record KernelProtocolFormSummaryResponse(
    string Id,
    int DisplayOrder,
    string Name,
    int ItemCount,
    int RequiredItemCount,
    IReadOnlyList<KernelProtocolFormItemResponse> Items)
{
    internal static KernelProtocolFormSummaryResponse From(
        CollectionSpec spec,
        int displayOrder,
        IReadOnlyDictionary<ConceptRef, string> conceptLabels)
    {
        var items = spec.Items
            .Select((item, idx) => KernelProtocolFormItemResponse.From(item, idx + 1, conceptLabels))
            .ToList();
        return new(
            spec.Id,
            displayOrder,
            spec.Name,
            items.Count,
            items.Count(i => i.Required),
            items);
    }
}

/// <summary>Summary of a rule-pack library item drafted alongside a protocol import.</summary>
/// <param name="Ref">Drafted library item <c>NodeRef</c> (<c>library_item:&lt;uuid&gt;</c>).</param>
/// <param name="Key">Rule-pack key.</param>
/// <param name="Version">Assigned version number.</param>
/// <param name="Title">Rule-pack title.</param>
/// <param name="RuleCount">Number of compiled ODM rules.</param>
/// <param name="Status">Governance status (<c>Draft</c>).</param>
public sealed record KernelImportedRulePackSummaryResponse(
    string Ref,
    string Key,
    int Version,
    string Title,
    int RuleCount,
    string Status)
{
    internal static KernelImportedRulePackSummaryResponse From(LibraryItemVersion item, string title, int ruleCount) =>
        new(
            NodeRef.For(item.Id).ToString(),
            item.Key,
            item.Version,
            title,
            ruleCount,
            GovernanceStatus.Draft.ToString());
}

/// <summary>Viewable protocol draft summary with forms, ordered items, and readable import notes.</summary>
/// <param name="Version">Protocol version metadata.</param>
/// <param name="Title">Protocol design title.</param>
/// <param name="FormCount">Total number of forms in the protocol design.</param>
/// <param name="ItemCount">Total number of collection items across all forms.</param>
/// <param name="Forms">Forms and their ordered items.</param>
/// <param name="ImportNotes">Readable import notes recorded during ODM/USDM import.</param>
/// <param name="ImportedRulePack">Drafted rule-pack library item when the imported ODM file contained range checks or conditions.</param>
public sealed record KernelProtocolDraftSummaryResponse(
    KernelProtocolVersionResponse Version,
    string Title,
    int FormCount,
    int ItemCount,
    IReadOnlyList<KernelProtocolFormSummaryResponse> Forms,
    IReadOnlyList<KernelProtocolImportNoteResponse> ImportNotes,
    KernelImportedRulePackSummaryResponse? ImportedRulePack = null)
{
    internal static KernelProtocolDraftSummaryResponse From(
        ProtocolVersionSummary version,
        ProtocolDesign? design,
        IReadOnlyDictionary<ConceptRef, string> conceptLabels,
        KernelImportedRulePackSummaryResponse? importedRulePack = null)
    {
        var specs = design?.CollectionSpecs ?? [];
        var forms = specs
            .Select((spec, idx) => KernelProtocolFormSummaryResponse.From(spec, idx + 1, conceptLabels))
            .ToList();
        var notes = (design?.ImportNotes ?? [])
            .Select(KernelProtocolImportNoteResponse.From)
            .ToList();
        return new(
            KernelProtocolVersionResponse.From(version, mask: null),
            design?.Title ?? string.Empty,
            forms.Count,
            forms.Sum(f => f.ItemCount),
            forms,
            notes,
            importedRulePack);
    }
}
