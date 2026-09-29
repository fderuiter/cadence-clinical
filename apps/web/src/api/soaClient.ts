import { openApiClient, unwrapResponse } from "./client";
import { generateGatewaySignature } from "ui";
import type { paths } from "./types";

export const soaClient = {
  /**
   * Helper to sign request headers for GxP validation of identity.
   */
  async getSignedHeaders(changeReason = "") {
    const timestamp = String(Math.floor(Date.now() / 1000));
    const signature = await generateGatewaySignature(
      {
        user_id: "usr_dm_fderuiter",
        role: "data_manager",
        timestamp,
        version: "2",
        change_reason: changeReason,
      },
      "internal-gateway-secret-12345"
    );
    return {
      "X-User-Id": "usr_dm_fderuiter",
      "X-User-Roles": "data_manager",
      "X-Gateway-Timestamp": timestamp,
      "X-Gateway-Signature": signature,
      "X-Signature-Version": "2",
    };
  },

  /**
   * Fetches the complete SoA matrix projection for a given study and version.
   */
  async getSoAProjection(studyId: string, versionId: string, options: { changeReason?: string } = {}) {
    const { changeReason = "Get projection" } = options;
    const signedHeaders = await this.getSignedHeaders(changeReason);
    return unwrapResponse(
      openApiClient.GET("/api/v1/studies/{study_id}/versions/{version_id}/soa-projection", {
        params: {
          path: {
            study_id: studyId,
            version_id: versionId,
          },
        },
        headers: {
          ...signedHeaders,
          "X-Change-Reason": changeReason,
        },
      })
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
        openApiClient.PUT("/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}", {
          params: { path: { study_id: studyId, version_id: versionId, arm_id: armId } },
          body: { properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}"]["put"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/arms", {
        params: { path: { study_id: studyId, version_id: versionId } },
        body: { id: armId, properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/arms"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": changeReason },
      })
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
        openApiClient.PUT("/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}", {
          params: { path: { study_id: studyId, version_id: versionId, epoch_id: epochId } },
          body: { properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}"]["put"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/epochs", {
        params: { path: { study_id: studyId, version_id: versionId } },
        body: { id: epochId, properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/epochs"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": changeReason },
      })
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
        openApiClient.PUT("/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}", {
          params: { path: { study_id: studyId, version_id: versionId, visit_id: visitId } },
          body: { properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}"]["put"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/visits", {
        params: { path: { study_id: studyId, version_id: versionId } },
        body: { id: visitId, properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/visits"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": changeReason },
      })
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
        openApiClient.PUT("/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}", {
          params: { path: { study_id: studyId, version_id: versionId, procedure_id: procedureId } },
          body: { properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}"]["put"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/procedures", {
        params: { path: { study_id: studyId, version_id: versionId } },
        body: { id: procedureId, properties } as unknown as paths["/api/v1/studies/{study_id}/versions/{version_id}/procedures"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": changeReason },
      })
    );
  },

  /**
   * Establishes link association.
   */
  async createLink(
    studyId: string,
    versionId: string,
    linkType: "epoch-visit" | "visit-procedure" | "timing" | "arm-applicability" | string,
    payload: unknown,
    options: { changeReason?: string } = {}
  ) {
    const { changeReason = "" } = options;
    if (linkType === "epoch-visit") {
      return unwrapResponse(
        openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit", {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    if (linkType === "visit-procedure") {
      return unwrapResponse(
        openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure", {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    if (linkType === "timing") {
      return unwrapResponse(
        openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/links/timing", {
          params: { path: { study_id: studyId, version_id: versionId } },
          body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/timing"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        })
      );
    }
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability", {
        params: { path: { study_id: studyId, version_id: versionId } },
        body: payload as paths["/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": changeReason },
      })
    );
  },

  async verifySignature({
    username,
    password,
    totp = null,
    action,
    batchId = null,
  }: {
    username: string;
    password?: string;
    totp?: string | null;
    action: string;
    batchId?: string | null;
  }) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/signatures/verify", {
        body: {
          username,
          password: password || "",
          totp: totp ?? undefined,
          action,
          batch_id: batchId ?? undefined,
        } as never,
      })
    );
  },

  async batchSignOff(
    {
      studyId,
      targetType,
      targetIds,
      signingReason,
    }: {
      studyId: string;
      targetType: string;
      targetIds: string[];
      signingReason: string;
    },
    { changeReason = "", sigToken = "" }: { changeReason?: string; sigToken?: string } = {}
  ) {
    const headers: Record<string, string> = {};
    if (sigToken) headers["X-Sig-Token"] = sigToken;
    if (changeReason) headers["X-Change-Reason"] = changeReason;

    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/batch-sign-off", {
        body: {
          study_id: studyId,
          target_type: targetType,
          target_ids: targetIds,
          signing_reason: signingReason,
        },
        headers,
      })
    );
  },
};
