import { buildStepUpVerificationPayload, SEMANTIC_ACTIONS } from "ui";
import { encodeChangeReason } from "./changeReason";
import { openApiClient, unwrapResponse } from "./client";
import type { paths } from "./types";

export const soaClient = {
  /**
   * Fetches the complete SoA matrix projection for a given study and version.
   */
  async getSoAProjection(
    studyId: string,
    versionId: string,
    options: { changeReason?: string } = {}
  ) {
    const { changeReason = "Get projection" } = options;
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/studies/{study_id}/versions/{version_id}/soa-projection",
        {
          params: {
            path: {
              study_id: studyId,
              version_id: versionId,
            },
          },
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },

  /**
   * Creates or updates a Study Arm.
   */
  async saveArm(
    studyId: string,
    versionId: string,
    armId: string,
    properties: Record<string, unknown>,
    options: { changeReason?: string; method?: string } = {}
  ) {
    const { changeReason = "", method = "POST" } = options;
    if (method.toUpperCase() === "PUT") {
      return unwrapResponse(
        openApiClient.PUT(
          "/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}",
          {
            params: {
              path: { study_id: studyId, version_id: versionId, arm_id: armId },
            },
            body: {
              properties,
            } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}"]["put"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/studies/{study_id}/versions/{version_id}/arms",
        {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: {
            id: armId,
            properties,
          } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/arms"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },

  /**
   * Creates or updates an Epoch.
   */
  async saveEpoch(
    studyId: string,
    versionId: string,
    epochId: string,
    properties: Record<string, unknown>,
    options: { changeReason?: string; method?: string } = {}
  ) {
    const { changeReason = "", method = "POST" } = options;
    if (method.toUpperCase() === "PUT") {
      return unwrapResponse(
        openApiClient.PUT(
          "/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}",
          {
            params: {
              path: {
                study_id: studyId,
                version_id: versionId,
                epoch_id: epochId,
              },
            },
            body: {
              properties,
            } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}"]["put"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/studies/{study_id}/versions/{version_id}/epochs",
        {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: {
            id: epochId,
            properties,
          } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/epochs"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },

  /**
   * Creates or updates a Visit.
   */
  async saveVisit(
    studyId: string,
    versionId: string,
    visitId: string,
    properties: Record<string, unknown>,
    options: { changeReason?: string; method?: string } = {}
  ) {
    const { changeReason = "", method = "POST" } = options;
    if (method.toUpperCase() === "PUT") {
      return unwrapResponse(
        openApiClient.PUT(
          "/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}",
          {
            params: {
              path: {
                study_id: studyId,
                version_id: versionId,
                visit_id: visitId,
              },
            },
            body: {
              properties,
            } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}"]["put"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/studies/{study_id}/versions/{version_id}/visits",
        {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: {
            id: visitId,
            properties,
          } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/visits"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },

  /**
   * Creates or updates a Procedure.
   */
  async saveProcedure(
    studyId: string,
    versionId: string,
    procedureId: string,
    properties: Record<string, unknown>,
    options: { changeReason?: string; method?: string } = {}
  ) {
    const { changeReason = "", method = "POST" } = options;
    if (method.toUpperCase() === "PUT") {
      return unwrapResponse(
        openApiClient.PUT(
          "/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}",
          {
            params: {
              path: {
                study_id: studyId,
                version_id: versionId,
                procedure_id: procedureId,
              },
            },
            body: {
              properties,
            } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}"]["put"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/studies/{study_id}/versions/{version_id}/procedures",
        {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: {
            id: procedureId,
            properties,
          } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/procedures"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },

  /**
   * Establishes link association.
   */
  async createLink(
    studyId: string,
    versionId: string,
    linkType:
      | "epoch-visit"
      | "visit-procedure"
      | "timing"
      | "arm-applicability"
      | string,
    payload: unknown,
    options: { changeReason?: string } = {}
  ) {
    const { changeReason = "" } = options;
    if (linkType === "epoch-visit") {
      return unwrapResponse(
        openApiClient.POST(
          "/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit",
          {
            params: { path: { study_id: studyId, version_id: versionId } },
            body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit"]["post"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    if (linkType === "visit-procedure") {
      return unwrapResponse(
        openApiClient.POST(
          "/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure",
          {
            params: { path: { study_id: studyId, version_id: versionId } },
            body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure"]["post"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    if (linkType === "timing") {
      return unwrapResponse(
        openApiClient.POST(
          "/api/v1/studies/{study_id}/versions/{version_id}/links/timing",
          {
            params: { path: { study_id: studyId, version_id: versionId } },
            body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/timing"]["post"]["requestBody"]["content"]["application/json"],
            headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
          }
        )
      );
    }
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability",
        {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },

  async verifySignature({
    username,
    password,
    totp = null,
    action,
    semanticAction,
    batchId = null,
  }: {
    username: string;
    password?: string;
    totp?: string | null;
    action: string;
    semanticAction?: string;
    batchId?: string | null;
  }): Promise<{ sig_token: string; expires_in?: number }> {
    let semantic = semanticAction;
    if (!semantic) {
      if (action.includes("capture-consent")) {
        semantic = SEMANTIC_ACTIONS.ECONSENT_CAPTURE;
      } else if (action.includes("doa/sign-off")) {
        semantic = SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF;
      } else if (
        action.includes("monitoring-visits/") &&
        action.endsWith("/sign-off")
      ) {
        semantic = SEMANTIC_ACTIONS.CTMS_MONITORING_VISIT_SIGN_OFF;
      } else if (action.includes("letters/compile")) {
        semantic = SEMANTIC_ACTIONS.CTMS_LETTER_COMPILE;
      } else if (action.includes("startup/matrix/batch-certify")) {
        semantic = SEMANTIC_ACTIONS.CTMS_GREENLIGHT_BATCH_CERTIFY;
      } else if (action.includes("greenlight/certify")) {
        semantic = SEMANTIC_ACTIONS.CTMS_GREENLIGHT_CERTIFY;
      } else if (action.includes("remittance-advice/sign")) {
        semantic = SEMANTIC_ACTIONS.PAYMENTS_REMITTANCE_SIGN;
      } else if (
        action.includes("/imaging/adjudications/") &&
        action.endsWith("/chair-adjudicate")
      ) {
        semantic = SEMANTIC_ACTIONS.IMAGING_CHAIR_ADJUDICATE;
      } else if (action.includes("esource/reconcile/batch-accept")) {
        semantic = SEMANTIC_ACTIONS.INTEROP_ESOURCE_BATCH_ACCEPT;
      } else if (action.includes("esource/reconcile/accept")) {
        semantic = SEMANTIC_ACTIONS.INTEROP_ESOURCE_ACCEPT;
      } else if (action.includes("esource/reconcile/batch-reject")) {
        semantic = SEMANTIC_ACTIONS.INTEROP_ESOURCE_BATCH_REJECT;
      } else if (action.includes("esource/reconcile/reject")) {
        semantic = SEMANTIC_ACTIONS.INTEROP_ESOURCE_REJECT;
      } else if (
        action.includes("/execution/subjects/") &&
        action.endsWith("/state")
      ) {
        semantic = SEMANTIC_ACTIONS.EXECUTION_SUBJECT_UNBLIND;
      } else if (action.includes("sign-off")) {
        semantic = SEMANTIC_ACTIONS.ETMF_DOCUMENT_SIGN_OFF;
      } else if (action.includes("chair-adjudicate")) {
        semantic = SEMANTIC_ACTIONS.CEC_CHAIR_ADJUDICATE;
      } else if (action.includes("/soa/")) {
        semantic = SEMANTIC_ACTIONS.SOA_APPROVE;
      } else {
        semantic = "execution.signature.verify";
      }
    }

    const body = buildStepUpVerificationPayload({
      username,
      password: password || "",
      totp,
      action,
      semanticAction: semantic,
      batchId,
    });

    return unwrapResponse(
      (openApiClient as any).POST("/api/v1/auth/signature-verification", {
        body,
      })
    );
  },
};
