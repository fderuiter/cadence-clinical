import { generateGatewaySignature } from "ui";

const GATEWAY_URL = "http://localhost:8000";
const DEFAULT_SECRET = "internal-gateway-secret-12345"; // pragma: allowlist secret

/**
 * Helper to construct gateway-signed request headers.
 */
async function getSignedHeaders({
  userId,
  roles,
  changeReason,
  secret = DEFAULT_SECRET,
}) {
  const timestamp = String(Date.now() / 1000);
  const signature = await generateGatewaySignature(
    userId,
    roles,
    timestamp,
    "2",
    changeReason,
    secret
  );

  return {
    "Content-Type": "application/json",
    "X-User-Id": userId,
    "X-User-Roles": roles,
    "X-Gateway-Timestamp": timestamp,
    "X-Gateway-Signature": signature,
    "X-Signature-Version": "2",
    "X-Change-Reason": changeReason,
  };
}

/**
 * Thin API Client for SoA projection and entity mutations.
 */
export const soaClient = {
  /**
   * Fetches the complete SoA matrix projection for a given study and version.
   */
  async getSoAProjection(studyId, versionId, { userId, roles }) {
    const headers = await getSignedHeaders({
      userId,
      roles,
      changeReason: "Get projection",
    });
    const response = await fetch(
      `${GATEWAY_URL}/api/v1/studies/${studyId}/versions/${versionId}/soa-projection`,
      { headers }
    );
    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      throw new Error(
        err.detail || `Failed to fetch SoA projection: ${response.status}`
      );
    }
    return response.json();
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
    { userId, roles, changeReason, method = "POST" }
  ) {
    const headers = await getSignedHeaders({ userId, roles, changeReason });
    const isPut = method === "PUT";
    const url = `${GATEWAY_URL}/api/v1/studies/${studyId}/versions/${versionId}/${entityType}${isPut ? `/${entityId}` : ""}`;
    const body = isPut ? { properties } : { id: entityId, properties };

    const response = await fetch(url, {
      method,
      headers,
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      throw new Error(
        err.detail || `Failed to mutate ${entityType}: ${response.status}`
      );
    }
    return response.json();
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
  async createLink(
    studyId,
    versionId,
    linkType,
    payload,
    { userId, roles, changeReason }
  ) {
    const headers = await getSignedHeaders({ userId, roles, changeReason });
    const url = `${GATEWAY_URL}/api/v1/studies/${studyId}/versions/${versionId}/links/${linkType}`;

    const response = await fetch(url, {
      method: "POST",
      headers,
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      throw new Error(
        err.detail || `Failed to establish link ${linkType}: ${response.status}`
      );
    }
    return response.json();
  },

  /**
   * Verifies re-supplied credentials to obtain a short-lived signature token (sig_token).
   */
  async verifySignature({ username, password, totp = null, action }, token = null) {
    const headers = {
      "Content-Type": "application/json",
    };
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }

    const response = await fetch(`${GATEWAY_URL}/api/v1/auth/signature-verification`, {
      method: "POST",
      headers,
      body: JSON.stringify({
        username,
        password,
        totp,
        action,
      }),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      throw new Error(
        err.detail || `Signature verification failed: ${response.status}`
      );
    }
    return response.json();
  },

  /**
   * Performs PI atomic batch sign-off.
   */
  async batchSignOff(
    { studyId, targetType, targetIds, signingReason },
    { userId, roles, changeReason, sigToken },
    token = null
  ) {
    const signedHeaders = await getSignedHeaders({ userId, roles, changeReason });
    const headers = {
      ...signedHeaders,
      "X-Sig-Token": sigToken,
    };
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }

    const response = await fetch(`${GATEWAY_URL}/api/v1/execution/batch-sign-off`, {
      method: "POST",
      headers,
      body: JSON.stringify({
        study_id: studyId,
        target_type: targetType,
        target_ids: targetIds,
        signing_reason: signingReason,
      }),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      if (
        response.status === 401 ||
        err.detail === "REAUTHENTICATION_REQUIRED" ||
        err.error === "REAUTHENTICATION_REQUIRED"
      ) {
        const error = new Error("REAUTHENTICATION_REQUIRED");
        error.status = response.status;
        error.detail = err.detail || "REAUTHENTICATION_REQUIRED";
        throw error;
      }
      throw new Error(
        err.detail || `Batch sign-off failed: ${response.status}`
      );
    }
    return response.json();
  },
};
