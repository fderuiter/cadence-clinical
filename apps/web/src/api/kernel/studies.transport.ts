import { unwrapResponse } from "../client";
import {
  kernelClient,
  mutationHeaders,
  windowQuery,
  type KernelLineageWindow,
} from "./shared";
import type {
  KernelAmendmentImpact,
  KernelCreateOrganizationInput,
  KernelCreateSiteInput,
  KernelCreateStudyRequest,
  KernelLinkSiteToStudyInput,
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

export async function executeProtocolDraftImport(
  request: KernelProtocolImportRequest
): Promise<KernelProtocolDraftSummary> {
  const { executeProtocolDraftImport: runProtocolDraftImport } =
    await import("./protocolImport");
  return runProtocolDraftImport(request);
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
