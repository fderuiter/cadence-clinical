import { apiClient } from "./apiClient";

/**
 * Thin API Client for SoA projection and entity mutations.
 */
export const soaClient = {
  /**
   * Fetches the complete SoA matrix projection for a given study and version.
   */
  async getSoAProjection(studyId, versionId, options = {}) {
    const { changeReason = "Get projection" } = options;
    return apiClient.get(
      `/api/v1/studies/${studyId}/versions/${versionId}/soa-projection`,
      { changeReason }
    );
  },

  /**
   * Universal mutation handler for SoA entities.
   */
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

  /**
   * Creates or updates a Study Arm.
   */
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

  /**
   * Creates or updates an Epoch.
   */
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

  /**
   * Creates or updates a Visit / Encounter.
   */
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

  /**
   * Creates or updates a Procedure.
   */
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

  /**
   * Establishes link association.
   */
  async createLink(studyId, versionId, linkType, payload, options = {}) {
    const { changeReason } = options;
    return apiClient.post(
      `/api/v1/studies/${studyId}/versions/${versionId}/links/${linkType}`,
      payload,
      { changeReason }
    );
  },

  /**
   * Verifies re-supplied credentials to obtain a short-lived signature token (sig_token).
   */
  async verifySignature({ username, password, totp = null, action }) {
    return apiClient.post("/api/v1/auth/signature-verification", {
      username,
      password,
      totp,
      action,
    });
  },

  /**
   * Performs PI atomic batch sign-off.
   */
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
