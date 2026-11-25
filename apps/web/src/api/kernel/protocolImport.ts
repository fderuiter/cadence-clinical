import { demoNowMs, isDemoBuild, nextDemoStep } from "../../utils/demoMode.js";
import {
  applyDemoIdentityHeaders,
  fetchWithSingleFlightRetry,
  getBearerToken,
  notifySubscriptionExpiredIfNeeded,
} from "../authorization.js";
import { ApiError, getBaseUrl, unwrapResponse } from "../client";
import type {
  KernelEntitlements,
  KernelTrialOnboardingRequest,
  KernelTrialOnboardingResponse,
} from "./entitlements";
import type {
  KernelActivityPage,
  KernelActivityQuery,
  KernelChild,
  KernelCommand,
  KernelDependents,
  KernelLineage,
  KernelTraceQuery,
  KernelZoomStep,
} from "./navigation";
import type {
  KernelAssertionRetraction,
  KernelAssertionState,
  KernelObservation,
  KernelObservationCorrection,
  KernelObservationState,
  KernelObservationVerification,
} from "./observations";
import type {
  KernelProposal,
  KernelProposalRejection,
  KernelProposalState,
  KernelProposalStatus,
} from "./proposals";
import type {
  KernelDefineWorkQueueInput,
  KernelWorkQueueDefinition,
  KernelWorkQueuesPage,
  KernelWorkQueuesQuery,
} from "./workQueues";
import {
  describeKernelError,
  kernelClient,
  mutationHeaders,
  signedMutationHeaders,
  streamKernelDownload,
  unversioned,
  windowQuery,
  type KernelLineageWindow,
  type KernelStreamedArtifact,
} from "./shared";
import type {
  CasebookExportFormat,
  KernelApplyLock,
  KernelCasebookExportQuery,
  KernelLock,
  KernelLockReadiness,
  KernelReleaseLock,
  KernelStudyExportQuery,
  KernelStudyLocks,
  KernelStudyLocksQuery,
  StudyExportFormat,
} from "./locks";
import type {
  KernelAcceptValidationFindingInput,
  KernelApproveLibraryItemRequest,
  KernelClassifyValidationFindingInput,
  KernelDeclareStudyRulePacksInput,
  KernelExplainValidationFindingInput,
  KernelRulePack,
  KernelRulePackPreview,
  KernelStudyAttention,
  KernelStudyRulePacks,
  KernelStudyValidationFindingsQuery,
  KernelValidationFinding,
  KernelValidationFindingDetail,
  KernelValidationFindingPage,
  KernelValidationFindingReasonInput,
} from "./validation";
import type {
  KernelAmendmentImpact,
  KernelCreateOrganizationInput,
  KernelCreateSiteInput,
  KernelCreateStudyRequest,
  KernelLinkSiteToStudyInput,
  KernelMatrixGapFinding,
  KernelOrganizationSummary,
  KernelProtocolDraftSummary,
  KernelProtocolImportRequest,
  KernelProtocolTemplate,
  KernelProtocolVersion,
  KernelScheduleOfActivities,
  KernelSimulationResult,
  KernelSiteSummary,
  KernelStudySiteSummary,
  KernelStudySummary,
} from "./studies";

export function executeCreateOrganization({
  name,
  code,
  kind = "SiteInstitution",
  reason,
  idempotencyKey,
}: KernelCreateOrganizationInput): Promise<KernelOrganizationSummary> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/organizations", {
      body: {
        name,
        code: code ?? null,
        kind: kind ?? "SiteInstitution",
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeCreateSite({
  siteCode,
  code,
  name,
  country,
  institutionRef,
  institutionCode,
  institutionName,
  reason,
  idempotencyKey,
}: KernelCreateSiteInput): Promise<KernelSiteSummary> {
  const resolvedSiteCode = (siteCode ?? code ?? "").trim();
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/sites", {
      body: {
        site_code: resolvedSiteCode,
        name,
        country,
        institution_ref: institutionRef ?? null,
        institution_code: institutionCode ?? null,
        institution_name: institutionName ?? null,
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeLinkSiteToStudy({
  studyRef,
  siteRef,
  siteCode,
  institutionRef,
  institutionCode,
  reason,
  idempotencyKey,
}: KernelLinkSiteToStudyInput): Promise<KernelStudySiteSummary> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/studies/{studyRef}/sites", {
      params: { path: { studyRef } },
      body: {
        site_ref: siteRef ?? null,
        site_code: siteCode ?? null,
        ...(institutionRef !== undefined
          ? { institution_ref: institutionRef }
          : {}),
        ...(institutionCode !== undefined
          ? { institution_code: institutionCode }
          : {}),
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeCreateStudy({
  code,
  title,
  phase,
  sponsorRef,
  sponsorCode,
  sponsorName,
  linkSandboxSite,
  initialSiteRef,
  initialSiteCode,
  reason,
  idempotencyKey,
}: KernelCreateStudyRequest): Promise<KernelStudySummary> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/studies", {
      body: {
        study_code: code,
        title,
        phase,
        sponsor_ref: sponsorRef ?? null,
        sponsor_code: sponsorCode ?? null,
        sponsor_name: sponsorName ?? null,
        link_sandbox_site: Boolean(linkSandboxSite),
        initial_site_ref: initialSiteRef ?? null,
        initial_site_code: initialSiteCode ?? null,
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeListProtocolTemplates(): Promise<
  KernelProtocolTemplate[]
> {
  return unwrapResponse(kernelClient.GET("/api/v1/kernel/protocol-templates"));
}

export function executeGetProtocolDesignSummary(
  versionRef: string
): Promise<KernelProtocolDraftSummary> {
  return unwrapResponse(
    kernelClient.GET(
      "/api/v1/kernel/protocol-versions/{versionRef}/design-summary",
      {
        params: { path: { versionRef } },
      }
    )
  );
}

export function executeAmendmentImpact(
  versionRef: string,
  window: Omit<KernelLineageWindow, "edgeKinds"> = {}
): Promise<KernelAmendmentImpact> {
  const { depth, valid_at, known_at } = windowQuery(window);
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/protocol-versions/{versionRef}/impact", {
      params: { path: { versionRef }, query: { depth, valid_at, known_at } },
    })
  );
}

export function executeSimulate(request: {
  baseVersionRef?: string;
  storedVersionRef?: string;
  candidateContent?: string;
  validAt?: string;
  knownAt?: string;
  kind?: "ProtocolVersion" | "MappingSpecification" | "RulePack";
  studyRef?: string;
  libraryItemRef?: string;
  baseLibraryItemRef?: string;
  standardVersionRef?: string;
}): Promise<KernelSimulationResult> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/simulation/simulate", {
      body: {
        base_version_ref: request.baseVersionRef ?? null,
        stored_version_ref: request.storedVersionRef ?? null,
        candidate_content: request.candidateContent ?? null,
        valid_at: request.validAt ?? null,
        known_at: request.knownAt ?? null,
        ...(request.kind ? { kind: request.kind } : {}),
        ...(request.studyRef ? { study_ref: request.studyRef } : {}),
        ...(request.libraryItemRef
          ? { library_item_ref: request.libraryItemRef }
          : {}),
        ...(request.baseLibraryItemRef
          ? { base_library_item_ref: request.baseLibraryItemRef }
          : {}),
        ...(request.standardVersionRef
          ? { standard_version_ref: request.standardVersionRef }
          : {}),
      },
    })
  );
}

export function executeCheckMatrixGaps(
  protocolContent: string
): Promise<KernelMatrixGapFinding[]> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/protocols/matrix-gap-check", {
      body: { protocol_content: protocolContent },
    })
  );
}

export async function executeProtocolDraftImport({
  studyRef,
  file,
  content,
  fileName,
  format,
  templateIds,
  templateAssignments,
  replaceForms = false,
  amendmentClassification,
  transitionPolicy,
  reason,
  idempotencyKey,
}: KernelProtocolImportRequest): Promise<KernelProtocolDraftSummary> {
  const serializedAssignments =
    templateAssignments && templateAssignments.length > 0
      ? templateAssignments.map((assignment) => ({
          template_id: assignment.templateId,
          activity_ids: assignment.activityIds,
        }))
      : null;

  if (file) {
    const baseUrl = getBaseUrl().replace(/\/+$/, "");
    const url = `${baseUrl}/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/protocol-imports`;
    const formData = new FormData();
    formData.append("file", file);
    if (templateIds && templateIds.length > 0) {
      for (const tplId of templateIds) {
        formData.append("template_ids", tplId);
      }
    }
    if (serializedAssignments) {
      formData.append(
        "template_assignments",
        JSON.stringify(serializedAssignments)
      );
    }
    if (replaceForms) {
      formData.append("replace_forms", "true");
    }
    if (amendmentClassification) {
      formData.append("amendment_classification", amendmentClassification);
    }
    if (transitionPolicy) {
      formData.append("transition_policy", transitionPolicy);
    }

    const headers: Record<string, string> = mutationHeaders(
      reason,
      idempotencyKey
    );
    const token = await getBearerToken();
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }
    if (isDemoBuild()) {
      applyDemoIdentityHeaders(headers);
    }

    const response = await fetchWithSingleFlightRetry(url, {
      method: "POST",
      headers,
      body: formData,
    });
    if (!response.ok) {
      const errData = await response.json().catch(() => null);
      const expiredGuidance = notifySubscriptionExpiredIfNeeded(
        response.status,
        errData
      );
      const tempError = new ApiError(
        `Import failed with status ${response.status}`,
        response.status,
        response.statusText,
        errData
      );
      throw new ApiError(
        expiredGuidance || describeKernelError(tempError),
        response.status,
        response.statusText,
        errData
      );
    }
    return (await response.json()) as KernelProtocolDraftSummary;
  }

  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/studies/{studyRef}/protocol-imports", {
      params: { path: { studyRef } },
      body: {
        content: content ?? null,
        file_name: fileName ?? null,
        format: format ?? null,
        template_ids:
          templateIds && templateIds.length > 0 ? templateIds : null,
        replace_forms: replaceForms,
        template_assignments: serializedAssignments,
        amendment_classification: amendmentClassification ?? null,
        transition_policy: transitionPolicy ?? null,
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

function buildExportParams(
  init: Record<string, string>,
  q: { asOf?: string; validAt?: string; knownAt?: string }
): string {
  const params = new URLSearchParams(init);
  if (q.asOf) params.set("as_of", q.asOf);
  if (q.validAt) params.set("valid_at", q.validAt);
  if (q.knownAt) params.set("known_at", q.knownAt);
  return params.toString();
}

const STUDY_EXPORT_EXT: Record<StudyExportFormat, string> = {
  csv: "csv",
  odm_xml: "odm.xml",
  dataset_json: "dataset.json",
  sas_bundle: "sas_bundle.zip",
  r_bundle: "r_bundle.zip",
  study_archive: "study_archive.zip",
};

const CASEBOOK_EXPORT_EXT: Record<CasebookExportFormat, string> = {
  json: "casebook.json",
  odm_xml: "casebook.odm.xml",
  html: "casebook.html",
  pdf: "casebook.pdf",
};

export function executeApplyLock({
  studyRef,
  scopeRef,
  kind,
  meaning = "Approved",
  lockId,
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelApplyLock): Promise<KernelLock> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/locks", {
      body: {
        study_ref: unversioned(studyRef),
        scope_ref: unversioned(scopeRef),
        kind,
        meaning,
        ...(lockId ? { lock_id: lockId } : {}),
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function executeReleaseLock({
  lockRef,
  studyRef,
  meaning = "Responsibility",
  assertionId,
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelReleaseLock): Promise<KernelLock> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/locks/{lockRef}/release", {
      params: { path: { lockRef: unversioned(lockRef) } },
      body: {
        study_ref: unversioned(studyRef),
        meaning,
        ...(assertionId ? { assertion_id: assertionId } : {}),
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function executeStreamStudyExport({
  studyRef,
  format = "csv",
  lockFilter = "locked_only",
  asOf,
  validAt,
  knownAt,
}: KernelStudyExportQuery): Promise<KernelStreamedArtifact> {
  const qs = buildExportParams(
    { format, lock_filter: lockFilter },
    { asOf, validAt, knownAt }
  );
  return streamKernelDownload(
    `/api/v1/kernel/studies/${encodeURIComponent(unversioned(studyRef))}/exports?${qs}`,
    `study-export.${STUDY_EXPORT_EXT[format] || "csv"}`
  );
}

export function executeStreamEnrollmentCasebook({
  enrollmentRef,
  format = "json",
  asOf,
  validAt,
  knownAt,
}: KernelCasebookExportQuery): Promise<KernelStreamedArtifact> {
  const qs = buildExportParams({ format }, { asOf, validAt, knownAt });
  return streamKernelDownload(
    `/api/v1/kernel/enrollments/${encodeURIComponent(unversioned(enrollmentRef))}/casebook?${qs}`,
    `participant.${CASEBOOK_EXPORT_EXT[format] || "casebook.json"}`
  );
}

function findingPath(findingRef: string) {
  return { path: { findingRef: unversioned(findingRef) } };
}

export function executeListStudyRulePacks(
  studyRef: string
): Promise<KernelStudyRulePacks> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/rule-packs", {
      params: { path: { studyRef } },
    })
  );
}

export function executePreviewStudyRulePack(
  studyRef: string,
  itemRef: string
): Promise<KernelRulePackPreview> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview",
      {
        params: { path: { studyRef, itemRef: unversioned(itemRef) } },
      }
    )
  );
}

export function executeApproveLibraryItem({
  itemRef,
  meaning = "Approved",
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelApproveLibraryItemRequest): Promise<KernelRulePack> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/library-items/{itemRef}/approve", {
      params: { path: { itemRef: unversioned(itemRef) } },
      body: {
        meaning,
        ...(attestationId ? { attestation_id: attestationId } : {}),
      },
      headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
    })
  );
}

export function executeDeclareStudyRulePacks({
  studyRef,
  rulePackRefs,
  reason,
  idempotencyKey,
}: KernelDeclareStudyRulePacksInput): Promise<KernelStudyRulePacks> {
  const normalizedRefs = rulePackRefs.map((ref) => {
    const clean = unversioned(ref.trim());
    return clean.includes(":") ? clean : `library_item:${clean}`;
  });
  return unwrapResponse(
    kernelClient.PUT("/api/v1/kernel/studies/{studyRef}/standards", {
      params: { path: { studyRef } },
      body: { rule_pack_refs: normalizedRefs },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeListStudyValidationFindings(
  studyRef: string,
  query: KernelStudyValidationFindingsQuery = {}
): Promise<KernelValidationFindingPage> {
  const statuses = Array.isArray(query.status)
    ? query.status.filter(Boolean)
    : query.status
      ? [query.status]
      : undefined;
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/validation/findings", {
      params: {
        path: { studyRef },
        query: {
          stage: query.stage || undefined,
          status: statuses && statuses.length > 0 ? statuses : undefined,
          severity: query.severity || undefined,
          dataset: query.dataset || undefined,
          pack_key: query.packKey || undefined,
          limit: query.limit,
        },
      },
    })
  );
}

export function executeGetStudyAttention(
  studyRef: string,
  query: { limit?: number } = {}
): Promise<KernelStudyAttention> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/attention", {
      params: {
        path: { studyRef },
        query: { limit: query.limit },
      },
    })
  );
}

export function executeGetValidationFinding(
  findingRef: string
): Promise<KernelValidationFindingDetail> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/validation/findings/{findingRef}", {
      params: findingPath(findingRef),
    })
  );
}

export function executeInvestigateValidationFinding({
  findingRef,
  reason,
  idempotencyKey,
}: KernelValidationFindingReasonInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/investigate",
      {
        params: findingPath(findingRef),
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeClassifyValidationFinding({
  findingRef,
  classification,
  reason,
  idempotencyKey,
}: KernelClassifyValidationFindingInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/classify",
      {
        params: findingPath(findingRef),
        body: { classification },
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeAcceptValidationFinding({
  findingRef,
  rationale,
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelAcceptValidationFindingInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/accept",
      {
        params: findingPath(findingRef),
        body: {
          rationale,
          ...(attestationId ? { attestation_id: attestationId } : {}),
        },
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
      }
    )
  );
}

export function executeExplainValidationFinding({
  findingRef,
  explanation,
  attestationId,
  sigToken,
  reason,
  idempotencyKey,
}: KernelExplainValidationFindingInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/explain",
      {
        params: findingPath(findingRef),
        body: {
          explanation,
          ...(attestationId ? { attestation_id: attestationId } : {}),
        },
        headers: signedMutationHeaders(reason, idempotencyKey, sigToken),
      }
    )
  );
}

export function executeReopenValidationFinding({
  findingRef,
  reason,
  idempotencyKey,
}: KernelValidationFindingReasonInput): Promise<KernelValidationFinding> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/validation/findings/{findingRef}/reopen",
      {
        params: findingPath(findingRef),
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeGetObservation(
  observationRef: string
): Promise<KernelObservation> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/observations/{observationRef}", {
      params: { path: { observationRef: unversioned(observationRef) } },
    })
  );
}

export function executeCorrectObservation({
  observationRef,
  enrollmentRef,
  value,
  expectedVersion,
  reason,
  idempotencyKey,
}: KernelObservationCorrection): Promise<KernelObservationState> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/observations/{observationRef}/corrections",
      {
        params: { path: { observationRef: unversioned(observationRef) } },
        body: {
          enrollment_ref: enrollmentRef,
          value,
          expected_version: expectedVersion,
        },
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeVerifyObservation({
  observationRef,
  enrollmentRef,
  revision,
  assertionId,
  reason,
  idempotencyKey,
}: KernelObservationVerification): Promise<KernelAssertionState> {
  return unwrapResponse(
    kernelClient.POST(
      "/api/v1/kernel/observations/{observationRef}/verifications",
      {
        params: { path: { observationRef: unversioned(observationRef) } },
        body: {
          enrollment_ref: enrollmentRef,
          revision,
          ...(assertionId ? { assertion_id: assertionId } : {}),
        },
        headers: mutationHeaders(reason, idempotencyKey),
      }
    )
  );
}

export function executeRetractAssertion({
  assertionRef,
  enrollmentRef,
  assertionId,
  reason,
  idempotencyKey,
}: KernelAssertionRetraction): Promise<KernelAssertionState> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/assertions/{assertionRef}/retraction", {
      params: { path: { assertionRef: unversioned(assertionRef) } },
      body: {
        enrollment_ref: enrollmentRef,
        ...(assertionId ? { assertion_id: assertionId } : {}),
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeListProposals(
  studyRef: string,
  query: { status?: KernelProposalStatus; limit?: number } = {}
): Promise<KernelProposal[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/proposals", {
      params: {
        path: { studyRef },
        query: { status: query.status, limit: query.limit },
      },
    })
  );
}

export function executeGetProposal(
  proposalRef: string
): Promise<KernelProposal> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/proposals/{proposalRef}", {
      params: { path: { proposalRef: unversioned(proposalRef) } },
    })
  );
}

export function executeRejectProposal({
  proposalRef,
  reason,
  idempotencyKey,
}: KernelProposalRejection): Promise<KernelProposalState> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/proposals/{proposalRef}/rejection", {
      params: { path: { proposalRef: unversioned(proposalRef) } },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeListWorkQueues(
  query: KernelWorkQueuesQuery = {}
): Promise<KernelWorkQueuesPage> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/work-queues", {
      params: {
        query: {
          within: query.within ? unversioned(query.within) : undefined,
          code: query.codes && query.codes.length > 0 ? query.codes : undefined,
          limit: query.limit,
        },
      },
    })
  );
}

export function executeDefineWorkQueue({
  code,
  title,
  capability,
  verbClass,
  source,
  workQueueDefinitionId,
  reason,
  idempotencyKey,
}: KernelDefineWorkQueueInput): Promise<KernelWorkQueueDefinition> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/work-queues", {
      body: {
        code,
        title,
        capability,
        verb_class: verbClass,
        source,
        ...(workQueueDefinitionId
          ? { work_queue_definition_id: workQueueDefinitionId }
          : {}),
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}

export function executeBootstrapTrial(
  request: KernelTrialOnboardingRequest,
  options?: { idempotencyKey?: string; reason?: string }
): Promise<KernelTrialOnboardingResponse> {
  const idempotencyKey =
    options?.idempotencyKey ||
    `web-trial-bootstrap-${demoNowMs(nextDemoStep("trial-bootstrap"))}`;
  const reason =
    options?.reason ||
    `Bootstrap self-service ${request.tier || "Individual"} trial workspace`;
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/onboarding/trial", {
      headers: mutationHeaders(reason, idempotencyKey),
      body: request,
    })
  );
}

export function executeUpgradeSubscriptionTier({
  tier,
  idempotencyKey,
  reason = "Update commercial subscription tier in workspace",
}: {
  tier: string;
  idempotencyKey: string;
  reason?: string;
}): Promise<KernelEntitlements> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/entitlements/tier", {
      headers: mutationHeaders(reason, idempotencyKey),
      body: { tier },
    })
  );
}

export function executeActivateSubscription({
  tier,
  studyPhase,
  studyDurationMonths,
  countries,
  siteCount,
  participantCount,
  activeStudyCount,
  billingConfirmation,
  idempotencyKey,
  reason = "Activate scoped commercial subscription in workspace",
}: {
  tier: string;
  studyPhase?: string | null;
  studyDurationMonths?: number | null;
  countries?: string[] | null;
  siteCount?: number | null;
  participantCount?: number | null;
  activeStudyCount?: number | null;
  billingConfirmation?: string | null;
  idempotencyKey: string;
  reason?: string;
}): Promise<KernelEntitlements> {
  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/entitlements/activate", {
      headers: mutationHeaders(reason, idempotencyKey),
      body: {
        tier,
        study_phase: studyPhase ?? null,
        study_duration_months: studyDurationMonths ?? null,
        countries: countries ?? null,
        site_count: siteCount ?? null,
        participant_count: participantCount ?? null,
        active_study_count: activeStudyCount ?? null,
        billing_confirmation: billingConfirmation ?? null,
      },
    })
  );
}

export function executeListStudyLocks(
  studyRef: string,
  query: KernelStudyLocksQuery = {}
): Promise<KernelStudyLocks> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/locks", {
      params: {
        path: { studyRef: unversioned(studyRef) },
        query: {
          active_only: query.activeOnly,
          as_of: query.asOf,
          valid_at: query.validAt,
          known_at: query.knownAt,
        },
      },
    })
  );
}

export function executeGetStudyLockReadiness(
  studyRef: string,
  scopeRef?: string
): Promise<KernelLockReadiness> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/locks/readiness", {
      params: {
        path: { studyRef: unversioned(studyRef) },
        query: scopeRef ? { scope_ref: unversioned(scopeRef) } : undefined,
      },
    })
  );
}

export function executeListOrganizations(
  kind?: string
): Promise<KernelOrganizationSummary[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/organizations", {
      params: { query: { kind: kind || undefined } },
    })
  );
}

export function executeListSites(): Promise<KernelSiteSummary[]> {
  return unwrapResponse(kernelClient.GET("/api/v1/kernel/sites"));
}

export function executeListStudySites(
  studyRef: string
): Promise<KernelStudySiteSummary[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/sites", {
      params: { path: { studyRef } },
    })
  );
}

export function executeListProtocolVersions(
  studyRef: string
): Promise<KernelProtocolVersion[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/studies/{studyRef}/protocol-versions", {
      params: { path: { studyRef } },
    })
  );
}

export function executeGetProtocolVersion(
  versionRef: string
): Promise<KernelProtocolVersion> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/protocol-versions/{versionRef}", {
      params: { path: { versionRef } },
    })
  );
}

export function executeGetScheduleOfActivities(
  versionRef: string
): Promise<KernelScheduleOfActivities> {
  return unwrapResponse(
    kernelClient.GET(
      "/api/v1/kernel/protocol-versions/{versionRef}/schedule-of-activities",
      {
        params: { path: { versionRef } },
      }
    )
  );
}

export function executeGetZoomPath(nodeRef: string): Promise<KernelZoomStep[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/path", {
      params: { path: { nodeRef } },
    })
  );
}

export function executeGetChildren(nodeRef: string): Promise<KernelChild[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/children", {
      params: { path: { nodeRef } },
    })
  );
}

export function executeGetCommands(nodeRef: string): Promise<KernelCommand[]> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/commands", {
      params: { path: { nodeRef } },
    })
  );
}

export function executeTrace(
  nodeRef: string,
  query: KernelTraceQuery = {}
): Promise<KernelLineage> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/lineage", {
      params: {
        path: { nodeRef },
        query: { direction: query.direction, ...windowQuery(query) },
      },
    })
  );
}

export function executeDependents(
  nodeRef: string,
  window: KernelLineageWindow = {}
): Promise<KernelDependents> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/nodes/{nodeRef}/dependents", {
      params: { path: { nodeRef }, query: windowQuery(window) },
    })
  );
}

export function executeActivity({
  scope,
  families,
  actor,
  from,
  before,
  limit,
  includeNodes,
}: KernelActivityQuery = {}): Promise<KernelActivityPage> {
  return unwrapResponse(
    kernelClient.GET("/api/v1/kernel/activity", {
      params: {
        query: {
          scope,
          family: families && families.length > 0 ? families : undefined,
          actor,
          from,
          before,
          limit,
          include_nodes: includeNodes ? true : undefined,
        },
      },
    })
  );
}
