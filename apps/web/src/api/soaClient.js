import { apiClient } from "./apiClient";
import { generateGatewaySignature } from "ui";

export const soaClient = {
  async getSignedHeaders(changeReason = "") {
    const timestamp = String(Math.floor(Date.now() / 1000));
    const signature = await generateGatewaySignature(
      "usr_dm_fderuiter",
      "data_manager",
      timestamp,
      "2",
      changeReason,
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
  async getSoAProjection(studyId, versionId, options = {}) {
    const { changeReason = "Get projection" } = options;
    const signedHeaders = await this.getSignedHeaders(changeReason);
    return apiClient.get(
      `/api/v1/studies/${studyId}/versions/${versionId}/soa-projection`,
      {
        changeReason,
        headers: { ...signedHeaders },
      }
    );
  },
  async mutateEntity(
    studyId,
    versionId,
    entityType,
    entityId,
    properties,
    options = {}
  ) {
    const { changeReason, method = "POST" } = options;
    const isPut = method.toUpperCase() === "PUT";
    const path = `/api/v1/studies/${studyId}/versions/${versionId}/${entityType}${isPut ? `/${entityId}` : ""}`;
    const body = isPut ? { properties } : { id: entityId, properties };

    if (isPut) {
      return apiClient.put(path, body, { changeReason });
    } else {
      return apiClient.post(path, body, { changeReason });
    }
  },
  async saveArm(studyId, versionId, armId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "arms",
      armId,
      properties,
      options
    );
  },
  async saveEpoch(studyId, versionId, epochId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "epochs",
      epochId,
      properties,
      options
    );
  },
  async saveVisit(studyId, versionId, visitId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "visits",
      visitId,
      properties,
      options
    );
  },
  async saveProcedure(studyId, versionId, procedureId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "procedures",
      procedureId,
      properties,
      options
    );
  },
  async createLink(studyId, versionId, linkType, payload, options = {}) {
    const { changeReason } = options;
    return apiClient.post(
      `/api/v1/studies/${studyId}/versions/${versionId}/links/${linkType}`,
      payload,
      { changeReason }
    );
  },
  async verifySignature({
    username,
    password,
    totp = null,
    action,
    batchId = null,
  }) {
    return apiClient.post("/api/v1/auth/signature-verification", {
      username,
      password,
      totp,
      action,
      batch_id: batchId,
    });
  },
  async batchSignOff(
    { studyId, targetType, targetIds, signingReason },
    { changeReason, sigToken }
  ) {
    return apiClient.post(
      "/api/v1/execution/batch-sign-off",
      {
        study_id: studyId,
        target_type: targetType,
        target_ids: targetIds,
        signing_reason: signingReason,
      },
      {
        changeReason,
        headers: {
          "X-Sig-Token": sigToken,
        },
      }
    );
  },
};
