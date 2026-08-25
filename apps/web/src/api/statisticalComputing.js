import { apiClient } from "./apiClient";

/**
 * Service client for the Containerized Statistical Computing Environment (SCE).
 * Provides REST endpoints for R & Python statistical programs, lockfile integrity verification,
 * database lock authorization tokens, sandboxed execution runs, and cryptographic manifest verification.
 */
export const statisticalComputingService = {
  /**
   * Registers a version-controlled statistical program (R or Python).
   */
  createProgram(payload, options = {}) {
    return apiClient.post("/api/v1/interop/sce/programs", payload, options);
  },

  /**
   * Lists statistical programs for a study.
   */
  getPrograms(studyId, options = {}) {
    return apiClient.get("/api/v1/interop/sce/programs", {
      ...options,
      params: { study_id: studyId },
    });
  },

  /**
   * Retrieves a specific statistical program by ID.
   */
  getProgram(programId, options = {}) {
    return apiClient.get(`/api/v1/interop/sce/programs/${programId}`, options);
  },

  /**
   * Validates package lockfile integrity (uv.lock / renv.lock) and qualification.
   */
  validateLockfile(payload, options = {}) {
    return apiClient.post(
      "/api/v1/interop/sce/validate-lockfile",
      payload,
      options
    );
  },

  /**
   * Issues a formal database lock authorization token for unblinded analysis.
   */
  issueLockToken(payload, options = {}) {
    return apiClient.post("/api/v1/interop/sce/lock-tokens", payload, options);
  },

  /**
   * Lists active database lock authorization tokens for a study.
   */
  getLockTokens(studyId, options = {}) {
    return apiClient.get("/api/v1/interop/sce/lock-tokens", {
      ...options,
      params: { study_id: studyId },
    });
  },

  /**
   * Revokes an active database lock authorization token.
   */
  revokeLockToken(tokenId, reason = "Revocation", options = {}) {
    return apiClient.post(
      `/api/v1/interop/sce/lock-tokens/${tokenId}/revoke`,
      null,
      { ...options, params: { reason } }
    );
  },

  /**
   * Executes a statistical program within an isolated containerized sandbox.
   */
  executeProgram(payload, options = {}) {
    return apiClient.post("/api/v1/interop/sce/execute", payload, options);
  },

  /**
   * Lists historical execution runs for a study.
   */
  getExecutionRuns(studyId, programId = null, snapshotId = null, options = {}) {
    const params = { study_id: studyId };
    if (programId) params.program_id = programId;
    if (snapshotId) params.snapshot_id = snapshotId;
    return apiClient.get("/api/v1/interop/sce/runs", { ...options, params });
  },

  /**
   * Retrieves full execution run manifest and output artifacts.
   */
  getExecutionRun(runId, options = {}) {
    return apiClient.get(`/api/v1/interop/sce/runs/${runId}`, options);
  },

  /**
   * Cryptographically verifies an execution run manifest seal and electronic signature block.
   */
  verifyRunManifest(runId, options = {}) {
    return apiClient.post(
      `/api/v1/interop/sce/runs/${runId}/verify`,
      null,
      options
    );
  },

  /**
   * Retrieves artifact content and preview.
   */
  getArtifact(runId, artifactId, options = {}) {
    return apiClient.get(
      `/api/v1/interop/sce/runs/${runId}/artifacts/${artifactId}`,
      options
    );
  },
};
