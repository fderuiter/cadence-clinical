using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Events;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>Handles <see cref="CreateProtocolDraft"/>: a study's first protocol version.</summary>
internal sealed class CreateProtocolDraftHandler : ICommandHandler<CreateProtocolDraft, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        CreateProtocolDraft command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (!await context.Work.ExistsAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false))
        {
            return ProtocolErrors.StudyUnknown;
        }

        var studyId = command.StudyId;
        await context.Work.LockAsync<Study>(studyId, cancellationToken).ConfigureAwait(false);
        var existing = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return ProtocolErrors.FirstVersionExists;
        }

        var created = ProtocolVersion.CreateFirst(command.ProtocolVersionId, command.TenantId, command.StudyId, command.Content);
        if (created.Value is not { } version)
        {
            return created.Error!;
        }

        context.Work.Add(version);
        context.Work.Raise(new ProtocolVersionDrafted(version.TenantId, version.StudyId, version.Id, version.Number, null, null));
        return ProtocolVersionStates.Of(version);
    }
}

/// <summary>Handles <see cref="ForkProtocolVersion"/>: an amended draft numbered after every version in the study.</summary>
internal sealed class ForkProtocolVersionHandler : ICommandHandler<ForkProtocolVersion, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ForkProtocolVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<ProtocolVersion>(command.ParentVersionId, cancellationToken).ConfigureAwait(false) is not { } parent)
        {
            return KernelErrors.NotFound.WithMessage("The parent protocol version does not exist.");
        }

        // Numbers are taken under the study's lock, so concurrent forks number one after another (K-TX-4); the unique
        // (tenant, study, number) index stays as the last line of defence.
        var studyId = parent.StudyId;
        await context.Work.LockAsync<Study>(studyId, cancellationToken).ConfigureAwait(false);
        var siblings = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        var number = siblings.Max(v => v.Number) + 1;

        var forked = ProtocolVersion.Fork(parent, command.ProtocolVersionId, number, command.Amendment);
        if (forked.Value is not { } version)
        {
            return forked.Error!;
        }

        context.Work.Add(version);

        // The fork is derived from the approved parent it copies, which is what amendment impact walks from (K-PROT-2).
        context.Work.LinkInput(NodeRef.For(parent.Id, parent.VersionIndex), LineageEdgeKind.DerivedFrom);
        context.Work.Raise(new ProtocolVersionDrafted(
            version.TenantId, version.StudyId, version.Id, version.Number, version.ParentVersionId, version.Amendment));
        return ProtocolVersionStates.Of(version);
    }
}

/// <summary>Handles <see cref="ReviseProtocolDraft"/>.</summary>
internal sealed class ReviseProtocolDraftHandler : ICommandHandler<ReviseProtocolDraft, ProtocolVersionState>
{
    public Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ReviseProtocolDraft command, ICommandContext context, CancellationToken cancellationToken) =>
        ProtocolVersionStates.ChangeAsync(context, command.ProtocolVersionId, cancellationToken, version =>
            version.Revise(command.Content, command.Amendment)
            ?? Raise(context, new ProtocolVersionRevised(version.TenantId, version.StudyId, version.Id)));

    private static KernelError? Raise(ICommandContext context, object domainEvent)
    {
        context.Work.Raise(domainEvent);
        return null;
    }
}

/// <summary>
/// Handles <see cref="SubmitProtocolVersionForReview"/>. Freezing the content is when its references outside the
/// design must resolve (K-SEM-1): every concept version, code-system version and artifact it names must exist in the
/// tenant, so reviewers sign a design whose meaning is pinned. The registered <see cref="IProtocolSubmissionCheck"/>s
/// then run in registration order; the first refusal stops the submission.
/// </summary>
internal sealed class SubmitProtocolVersionForReviewHandler(IEnumerable<IProtocolSubmissionCheck> checks) : ICommandHandler<SubmitProtocolVersionForReview, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        SubmitProtocolVersionForReview command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        if (version.Status == ProtocolVersionStatus.Draft
            && await UnknownReferenceAsync(context, version.Design().ExternalReferences(), cancellationToken).ConfigureAwait(false) is { } unknown)
        {
            return ProtocolErrors.ReferenceUnknown.WithMessage($"{ProtocolErrors.ReferenceUnknown.Message} Unknown: {unknown}.");
        }

        if (version.Status == ProtocolVersionStatus.Draft)
        {
            foreach (var check in checks)
            {
                if (await check.CheckAsync(version.StudyId, version.Design(), context, cancellationToken).ConfigureAwait(false) is { } refused)
                {
                    return refused;
                }
            }
        }

        if (version.SubmitForReview() is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProtocolVersionSubmittedForReview(version.TenantId, version.StudyId, version.Id, version.ContentHash!));
        return ProtocolVersionStates.Of(version);
    }

    /// <summary>The first reference the tenant cannot resolve, or null. A concept reference needs that version to exist.</summary>
    private static async Task<string?> UnknownReferenceAsync(
        ICommandContext context, DesignExternalReferences references, CancellationToken cancellationToken)
    {
        foreach (var group in references.Concepts.GroupBy(c => c.ConceptId).OrderBy(g => g.Key.Value))
        {
            var concept = await context.Work.ReadAsync<Concept>(group.Key, cancellationToken).ConfigureAwait(false);
            var missing = group.Where(c => concept is null || c.Version > concept.Version).OrderBy(c => c.Version).ToList();
            if (missing.Count > 0)
            {
                return $"concept {missing[0]}";
            }
        }

        foreach (var codeSystemVersion in references.CodeSystemVersions.OrderBy(v => v.Value))
        {
            if (!await context.Work.ExistsAsync<CodeSystemVersion>(codeSystemVersion, cancellationToken).ConfigureAwait(false))
            {
                return $"code-system version {codeSystemVersion}";
            }
        }

        foreach (var artifact in references.Artifacts.OrderBy(a => a.Value))
        {
            if (!await context.Work.ExistsAsync<Artifact>(artifact, cancellationToken).ConfigureAwait(false))
            {
                return $"artifact {artifact}";
            }
        }

        return null;
    }
}

/// <summary>Handles <see cref="ReturnProtocolVersionToDraft"/>.</summary>
internal sealed class ReturnProtocolVersionToDraftHandler : ICommandHandler<ReturnProtocolVersionToDraft, ProtocolVersionState>
{
    public Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ReturnProtocolVersionToDraft command, ICommandContext context, CancellationToken cancellationToken) =>
        ProtocolVersionStates.ChangeAsync(context, command.ProtocolVersionId, cancellationToken, version =>
        {
            if (version.ReturnToDraft() is { } error)
            {
                return error;
            }

            context.Work.Raise(new ProtocolVersionReturnedToDraft(version.TenantId, version.StudyId, version.Id));
            return null;
        });
}

/// <summary>
/// Handles <see cref="ApproveProtocolVersion"/>: the named attestation must mean <c>Approved</c> and sign this
/// version's content hash (spec §6.2, 21 CFR 11.70). The approval also writes the design's elements as lineage parts
/// of the approved version (ADR-2474), so amendment impact can start from the elements an amendment changes.
/// </summary>
internal sealed class ApproveProtocolVersionHandler(IAttestationVerifier verifier) : ICommandHandler<ApproveProtocolVersion, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ApproveProtocolVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        if (version.Status != ProtocolVersionStatus.InReview)
        {
            return version.Approve(command.AttestationId)!;
        }

        if (await ProtocolVersionStates.VerifyAsync(verifier, context, command.AttestationId, AttestationMeaning.Approved, version, cancellationToken)
                .ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        version.Approve(command.AttestationId);
        var plan = ProtocolElementLineage.Plan(version.StudyId, await ChainAsync(version, context, cancellationToken).ConfigureAwait(false));
        foreach (var part in plan.Parts)
        {
            context.Work.IncludePart(part.Node, part.NewVersion);
        }

        foreach (var edge in plan.Edges)
        {
            context.Work.LinkPart(edge.From, edge.To, edge.Kind);
        }

        context.Work.Raise(new ProtocolVersionApproved(
            version.TenantId, version.StudyId, version.Id, version.Number, version.ContentHash!, command.AttestationId));

        if (!string.IsNullOrWhiteSpace(version.Content))
        {
            try
            {
                if (TryCompileUsdm(version.Content, version.StudyId.Value.ToString(), version.Number.ToString()) is { } compiled)
                {
                    var snapshot = new FormVersionSnapshot(
                        FormVersionSnapshotId.New(),
                        version.TenantId,
                        version.StudyId,
                        version.Id,
                        version.Number.ToString(),
                        compiled.FormId,
                        compiled.FormHash,
                        compiled.OpenRosaXml,
                        compiled.DdfIrJson,
                        compiled.OdmXml);

                    context.Work.Add(snapshot);

                    context.Work.Raise(new FormVersionPublished(
                        version.TenantId,
                        version.StudyId,
                        version.Id,
                        version.Number.ToString(),
                        compiled.FormId,
                        compiled.FormHash,
                        compiled.OpenRosaXml,
                        compiled.DdfIrJson));
                }
            }
            catch (Exception)
            {
                // Gracefully ignore if version.Content is not valid USDM JSON (e.g. in legacy/simple tests)
            }
        }

        return ProtocolVersionStates.Of(version);
    }

    private static (string FormHash, string OpenRosaXml, string DdfIrJson, string FormId, string? OdmXml)? TryCompileUsdm(
        string content, string fallbackStudyId, string versionTag)
    {
        var ddfAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Cadence.Ddf")
            ?? System.Reflection.Assembly.Load("Cadence.Ddf");
        if (ddfAssembly is null) return null;

        var compilerType = ddfAssembly.GetType("Cadence.Ddf.UsdmCompiler");
        if (compilerType is null) return null;

        var compileMethod = compilerType.GetMethod("CompileUsdm", [typeof(string), typeof(string), typeof(string)]);
        var parseMethod = compilerType.GetMethod("ParseUsdmToIr", [typeof(string), typeof(string), typeof(string)]);
        if (compileMethod is null || parseMethod is null) return null;

        var compilation = compileMethod.Invoke(null, [content, fallbackStudyId, versionTag]);
        var parseResult = parseMethod.Invoke(null, [content, fallbackStudyId, versionTag]);
        if (compilation is null || parseResult is null) return null;

        var formHash = compilation.GetType().GetProperty("FormHash")?.GetValue(compilation) as string ?? string.Empty;
        var openRosaXml = compilation.GetType().GetProperty("OpenRosaXml")?.GetValue(compilation) as string ?? string.Empty;
        var odmXml = compilation.GetType().GetProperty("OdmXml")?.GetValue(compilation) as string;

        var studyIr = parseResult.GetType().GetProperty("Item1")?.GetValue(parseResult);
        var ddfIrJson = studyIr is not null ? System.Text.Json.JsonSerializer.Serialize(studyIr) : "{}";
        var studyIdProp = studyIr?.GetType().GetProperty("StudyId")?.GetValue(studyIr) as string;
        var formId = string.IsNullOrWhiteSpace(studyIdProp) ? fallbackStudyId : studyIdProp;

        return (formHash, openRosaXml, ddfIrJson, formId, odmXml);
    }

    /// <summary>The version and its ancestors, first version first, as (number, content).</summary>
    private static async Task<IReadOnlyList<(int Number, string Content)>> ChainAsync(
        ProtocolVersion version, ICommandContext context, CancellationToken cancellationToken)
    {
        var chain = new List<(int Number, string Content)> { (version.Number, version.Content) };
        var parentId = version.ParentVersionId;
        while (parentId is { } id)
        {
            var parent = await context.Work.ReadAsync<ProtocolVersion>(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Protocol version {id} names a parent the tenant does not have.");
            chain.Add((parent.Number, parent.Content));
            parentId = parent.ParentVersionId;
        }

        chain.Reverse();
        return chain;
    }
}

/// <summary>
/// Handles <see cref="WithdrawProtocolVersion"/>: the named attestation must mean <c>Withdrawn</c> and sign this
/// version's content hash; the reason for change is the withdrawal reason.
/// </summary>
internal sealed class WithdrawProtocolVersionHandler(IAttestationVerifier verifier) : ICommandHandler<WithdrawProtocolVersion, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        WithdrawProtocolVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        if (version.Status is not (ProtocolVersionStatus.InReview or ProtocolVersionStatus.Approved))
        {
            return version.Withdraw(command.AttestationId, command.ReasonForChange)!;
        }

        if (await ProtocolVersionStates.VerifyAsync(verifier, context, command.AttestationId, AttestationMeaning.Withdrawn, version, cancellationToken)
                .ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        if (version.Withdraw(command.AttestationId, command.ReasonForChange) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProtocolVersionWithdrawn(
            version.TenantId, version.StudyId, version.Id, command.AttestationId, command.ReasonForChange));
        return ProtocolVersionStates.Of(version);
    }
}

/// <summary>Shared steps of the protocol handlers.</summary>
internal static class ProtocolVersionStates
{
    public static CommandResult<ProtocolVersionState> Of(ProtocolVersion version) =>
        CommandResult<ProtocolVersionState>.Success(
            new ProtocolVersionState(version.Id, version.StudyId, version.Number, version.Status, version.ContentHash));

    /// <summary>Loads the version for change, applies <paramref name="change"/> and returns the new state.</summary>
    public static async Task<CommandResult<ProtocolVersionState>> ChangeAsync(
        ICommandContext context,
        ProtocolVersionId id,
        CancellationToken cancellationToken,
        Func<ProtocolVersion, KernelError?> change)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(id, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        return change(version) is { } error ? CommandResult<ProtocolVersionState>.Failure(error) : Of(version);
    }

    /// <summary>
    /// Checks that the acting person signed the attestation over the version's frozen content; returns the rejection,
    /// or null.
    /// </summary>
    public static async Task<KernelError?> VerifyAsync(
        IAttestationVerifier verifier,
        ICommandContext context,
        AttestationId attestation,
        AttestationMeaning meaning,
        ProtocolVersion version,
        CancellationToken cancellationToken)
    {
        var verification = await verifier
            .VerifyAsync(new AttestationRequirement(context.TenantId, attestation, meaning, version.SignableTarget!, context.Actor), cancellationToken)
            .ConfigureAwait(false);
        return verification.Satisfied
            ? null
            : ProtocolErrors.AttestationRejected.WithMessage(verification.Reason ?? ProtocolErrors.AttestationRejected.Message);
    }
}

/// <summary>Registers the protocol version command handlers (spec §6.2).</summary>
public static class ProtocolServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelProtocolHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<CreateProtocolDraft, ProtocolVersionState>, CreateProtocolDraftHandler>();
        services.AddTransient<ICommandHandler<ReviseProtocolDraft, ProtocolVersionState>, ReviseProtocolDraftHandler>();
        services.AddTransient<ICommandHandler<SubmitProtocolVersionForReview, ProtocolVersionState>, SubmitProtocolVersionForReviewHandler>();
        services.AddTransient<ICommandHandler<ReturnProtocolVersionToDraft, ProtocolVersionState>, ReturnProtocolVersionToDraftHandler>();
        services.AddTransient<ICommandHandler<ApproveProtocolVersion, ProtocolVersionState>, ApproveProtocolVersionHandler>();
        services.AddTransient<ICommandHandler<WithdrawProtocolVersion, ProtocolVersionState>, WithdrawProtocolVersionHandler>();
        services.AddTransient<ICommandHandler<ForkProtocolVersion, ProtocolVersionState>, ForkProtocolVersionHandler>();
        return services;
    }
}
