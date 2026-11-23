import { openApiClient, unwrapResponse } from "./client";
import type { paths } from "./types";

export const statisticalComputingService = {
  /**
   * Registers a version-controlled statistical program (R or Python).
   */
  createProgram(payload: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sce/programs", {
        body: payload as paths["/api/v1/interop/sce/programs"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  /**
   * Lists statistical programs for a study.
   */
  getPrograms(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sce/programs", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  /**
   * Validates package lockfile integrity (uv.lock / renv.lock) and qualification.
   */
  validateLockfile(payload: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sce/validate-lockfile", {
        body: payload as paths["/api/v1/interop/sce/validate-lockfile"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  /**
   * Issues a formal database lock authorization token for unblinded analysis.
   */
  issueLockToken(payload: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sce/lock-tokens", {
        body: payload as paths["/api/v1/interop/sce/lock-tokens"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  /**
   * Lists active database lock authorization tokens for a study.
   */
  getLockTokens(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sce/lock-tokens", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  /**
   * Revokes an active database lock authorization token.
   */
  revokeLockToken(
    tokenId: string,
    reason = "Revocation",
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sce/lock-tokens/{token_id}/revoke", {
        params: {
          path: { token_id: tokenId },
          query: { reason },
        },
        ...options,
      })
    );
  },

  /**
   * Executes a statistical program within an isolated containerized sandbox.
   */
  executeProgram(payload: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sce/execute", {
        body: payload as paths["/api/v1/interop/sce/execute"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  /**
   * Lists historical execution runs for a study.
   */
  getExecutionRuns(
    studyId: string,
    programId: string | null = null,
    snapshotId: string | null = null,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sce/runs", {
        params: {
          query: {
            study_id: studyId,
            program_id: programId ?? undefined,
            snapshot_id: snapshotId ?? undefined,
          },
        },
        ...options,
      })
    );
  },

  /**
   * Retrieves full execution run manifest and output artifacts.
   */
  getExecutionRun(runId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sce/runs/{run_id}", {
        params: { path: { run_id: runId } },
        ...options,
      })
    );
  },

  /**
   * Cryptographically verifies an execution run manifest seal and electronic signature block.
   */
  verifyRunManifest(runId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sce/runs/{run_id}/verify", {
        params: { path: { run_id: runId } },
        ...options,
      })
    );
  },
};
