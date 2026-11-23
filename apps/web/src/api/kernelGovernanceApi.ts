import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/kernel";
import type { KernelLockMeaning, KernelProtocolVersion } from "./kernelApi";

type Schemas = components["schemas"];

export type KernelArtifact = Schemas["Kernel_KernelArtifactResponse"];
export type KernelEffectivity = Schemas["Kernel_KernelEffectivityResponse"];
export type KernelEffectiveVersion =
  Schemas["Kernel_KernelEffectiveVersionResponse"];
export type KernelStudySite = Schemas["Kernel_KernelStudySiteResponse"];

/** Gateway step-up semantic action for `POST /api/v1/kernel/protocol-versions/{versionRef}/approve`. */
export const PROTOCOL_APPROVE_SEMANTIC_ACTION =
  "kernel.protocol_version.approve";

/** Gateway step-up target URL for `POST /api/v1/kernel/protocol-versions/{versionRef}/approve`. */
export function protocolVersionApproveAction(versionRef: string): string {
  const at = versionRef.indexOf("@");
  const clean = at < 0 ? versionRef : versionRef.slice(0, at);
  return `/api/v1/kernel/protocol-versions/${clean}/approve`;
}

export interface KernelProtocolLifecycleRequest {
  versionRef: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelApproveProtocolVersionInput {
  versionRef: string;
  meaning?: KernelLockMeaning;
  expectedContentHash?: string | null;
  attestationId?: string | null;
  reviewAttestationRef?: string | null;
  sigToken: string;
  reason: string;
  idempotencyKey: string;
}

export interface KernelSaveSimulationReportInput {
  studyRef: string;
  title: string;
  result: Schemas["Kernel_SimulationResult"];
  artifactId?: string | null;
  artifactVersionId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelRegisterArtifactInput {
  studyRef: string;
  kind: string;
  title: string;
  contentHash: string;
  mediaType?: string | null;
  sizeBytes?: number;
  byteLength?: number;
  storageRef?: string | null;
  storageKey?: string | null;
  artifactId?: string | null;
  artifactVersionId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelDeclareEffectivityInput {
  versionRef: string;
  scopeLevel?: "Study" | "Country" | "StudySite" | string;
  country?: string | null;
  studySiteRef?: string | null;
  declaredAt?: string | null;
  effectivityId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelChangeEffectivityStatusInput {
  effectivityRef: string;
  status: string;
  effectiveAt?: string | null;
  evidenceRefs?: string[];
  reason: string;
  idempotencyKey: string;
}

export interface KernelLinkStudySiteInput {
  studyRef: string;
  siteCode: string;
  siteName?: string | null;
  country?: string | null;
  institutionRef?: string | null;
  siteId?: string | null;
  studySiteId?: string | null;
  reason: string;
  idempotencyKey: string;
}

export interface KernelActivateStudySiteInput {
  studySiteRef: string;
  reason: string;
  idempotencyKey: string;
}

const client = createOpenApiClient<paths>();

function unversioned(nodeRef: string): string {
  const at = nodeRef.indexOf("@");
  return at < 0 ? nodeRef : nodeRef.slice(0, at);
}

/**
 * Route-scoped kernel client for protocol governance, evidence artifacts, bitemporal effectivities, and study-site
 * activation (ADR-2606). Separated from `kernelApi` so lazy Design/Startup views do not add governance endpoints to
 * the `index.html` cold-boot bundle.
 */
export const kernelGovernanceApi = {
  /** Submits a Draft protocol version for review, freezing its design and computing its content hash. */
  submitProtocolVersionForReview({
    versionRef,
    reason,
    idempotencyKey,
  }: KernelProtocolLifecycleRequest): Promise<KernelProtocolVersion> {
    return unwrapResponse(
      client.POST(
        "/api/v1/kernel/protocol-versions/{versionRef}/submit-for-review",
        {
          params: { path: { versionRef: unversioned(versionRef) } },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
    );
  },

  /** Returns an InReview protocol version back to Draft so its design can be revised. */
  returnProtocolVersionToDraft({
    versionRef,
    reason,
    idempotencyKey,
  }: KernelProtocolLifecycleRequest): Promise<KernelProtocolVersion> {
    return unwrapResponse(
      client.POST(
        "/api/v1/kernel/protocol-versions/{versionRef}/return-to-draft",
        {
          params: { path: { versionRef: unversioned(versionRef) } },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
    );
  },

  /** Approves an InReview protocol version with a 21 CFR Part 11 step-up signature bound to its frozen content hash. */
  approveProtocolVersion({
    versionRef,
    meaning = "Approved",
    expectedContentHash,
    attestationId,
    reviewAttestationRef,
    sigToken,
    reason,
    idempotencyKey,
  }: KernelApproveProtocolVersionInput): Promise<KernelProtocolVersion> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/protocol-versions/{versionRef}/approve", {
        params: { path: { versionRef: unversioned(versionRef) } },
        body: {
          meaning,
          ...(expectedContentHash
            ? { expected_content_hash: expectedContentHash }
            : {}),
          ...(attestationId ? { attestation_id: attestationId } : {}),
          ...(reviewAttestationRef
            ? { review_attestation_ref: reviewAttestationRef }
            : {}),
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
          "X-Sig-Token": sigToken,
        },
      })
    );
  },

  /** Saves an unmasked SimulationResult as an attestable SimulationReport artifact version (K-SIM-5). */
  saveSimulationReport({
    studyRef,
    title,
    result,
    artifactId,
    artifactVersionId,
    reason,
    idempotencyKey,
  }: KernelSaveSimulationReportInput): Promise<KernelArtifact> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/studies/{studyRef}/simulation-reports", {
        params: { path: { studyRef: unversioned(studyRef) } },
        body: {
          title,
          result,
          ...(artifactId ? { artifact_id: artifactId } : {}),
          ...(artifactVersionId
            ? { artifact_version_id: artifactVersionId }
            : {}),
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Lists evidence artifacts registered for a study, newest first. */
  listStudyArtifacts(studyRef: string): Promise<KernelArtifact[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/artifacts", {
        params: { path: { studyRef: unversioned(studyRef) } },
      })
    );
  },

  /** Registers an immutable evidence artifact version (e.g. EthicsApproval, RegulatoryApproval, CtaAcknowledgement). */
  registerStudyArtifact({
    studyRef,
    kind,
    title,
    contentHash,
    mediaType,
    sizeBytes,
    byteLength,
    storageRef,
    storageKey,
    artifactId,
    artifactVersionId,
    reason,
    idempotencyKey,
  }: KernelRegisterArtifactInput): Promise<KernelArtifact> {
    const resolvedSizeBytes = Math.max(1, sizeBytes ?? byteLength ?? 1);
    const resolvedMediaType =
      (mediaType ?? "application/pdf").trim().toLowerCase() ||
      "application/pdf";
    const resolvedStorageRef =
      (storageRef ?? storageKey ?? "").trim() ||
      `urn:cadence:artifact:${idempotencyKey}`;
    return unwrapResponse(
      client.POST("/api/v1/kernel/studies/{studyRef}/artifacts", {
        params: { path: { studyRef: unversioned(studyRef) } },
        body: {
          kind,
          title,
          content_hash: contentHash,
          media_type: resolvedMediaType,
          size_bytes: resolvedSizeBytes,
          storage_ref: resolvedStorageRef,
          artifact_id: artifactId ?? null,
          artifact_version_id: artifactVersionId ?? null,
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Lists bitemporal protocol-version effectivities declared for a study. */
  listStudyEffectivities(studyRef: string): Promise<KernelEffectivity[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/effectivities", {
        params: { path: { studyRef: unversioned(studyRef) } },
      })
    );
  },

  /** Declares a Pending protocol-version effectivity for a study, country, or study site. */
  declareProtocolEffectivity({
    versionRef,
    scopeLevel = "Study",
    country,
    studySiteRef,
    declaredAt,
    effectivityId,
    reason,
    idempotencyKey,
  }: KernelDeclareEffectivityInput): Promise<KernelEffectivity> {
    return unwrapResponse(
      client.POST(
        "/api/v1/kernel/protocol-versions/{versionRef}/effectivities",
        {
          params: { path: { versionRef: unversioned(versionRef) } },
          body: {
            scope_level: scopeLevel,
            country: country ?? null,
            study_site_ref: studySiteRef ? unversioned(studySiteRef) : null,
            declared_at: declaredAt ?? null,
            effectivity_id: effectivityId ?? null,
          },
          headers: {
            "X-Change-Reason": encodeChangeReason(reason),
            "Idempotency-Key": idempotencyKey,
          },
        }
      )
    );
  },

  /** Transitions an effectivity's status (e.g. Pending -> Effective) with supporting approval artifact evidence. */
  changeEffectivityStatus({
    effectivityRef,
    status,
    effectiveAt,
    evidenceRefs,
    reason,
    idempotencyKey,
  }: KernelChangeEffectivityStatusInput): Promise<KernelEffectivity> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/effectivities/{effectivityRef}/status", {
        params: { path: { effectivityRef: unversioned(effectivityRef) } },
        body: {
          status,
          effective_at: effectiveAt ?? null,
          evidence_artifact_version_refs: (evidenceRefs ?? []).map((ref) =>
            unversioned(ref)
          ),
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Lists study sites linked to a study in the kernel together with their active governing protocol versions. */
  listStudySites(
    studyRef: string,
    query: { validAt?: string; knownAt?: string } = {}
  ): Promise<KernelStudySite[]> {
    return unwrapResponse(
      client.GET("/api/v1/kernel/studies/{studyRef}/sites", {
        params: {
          path: { studyRef: unversioned(studyRef) },
          query: {
            valid_at: query.validAt || undefined,
            known_at: query.knownAt || undefined,
          },
        },
      })
    );
  },

  /** Registers/links a site to a study in the kernel (Selected status). */
  linkStudySite({
    studyRef,
    siteCode,
    siteName,
    country,
    institutionRef,
    siteId,
    studySiteId,
    reason,
    idempotencyKey,
  }: KernelLinkStudySiteInput): Promise<KernelStudySite> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/studies/{studyRef}/sites", {
        params: { path: { studyRef: unversioned(studyRef) } },
        body: {
          site_code: siteCode,
          site_name: siteName ?? null,
          country: country ?? null,
          institution_ref: institutionRef ? unversioned(institutionRef) : null,
          site_id: siteId ?? null,
          study_site_id: studySiteId ?? null,
        },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },

  /** Activates a linked study site in the kernel once at least one Approved protocol version is Effective for it. */
  activateStudySite({
    studySiteRef,
    reason,
    idempotencyKey,
  }: KernelActivateStudySiteInput): Promise<KernelStudySite> {
    return unwrapResponse(
      client.POST("/api/v1/kernel/study-sites/{studySiteRef}/activate", {
        params: { path: { studySiteRef: unversioned(studySiteRef) } },
        headers: {
          "X-Change-Reason": encodeChangeReason(reason),
          "Idempotency-Key": idempotencyKey,
        },
      })
    );
  },
};
