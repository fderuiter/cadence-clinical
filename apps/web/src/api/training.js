/**
 * API Client for Study Protocol Amendment Training Curricula and EDC Write-Gating.
 *
 * Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
 */

import { api } from "./apiClient";

export const trainingApi = {
  /**
   * Fetch active training curricula for a study and protocol version.
   */
  async listCurricula(params = {}) {
    const res = await api.get("/api/v1/training/curricula", { params });
    return res.data;
  },

  /**
   * Fetch specific curriculum details including modules and quiz questions.
   */
  async getCurriculum(curriculumId) {
    const res = await api.get(`/api/v1/training/curricula/${curriculumId}`);
    return res.data;
  },

  /**
   * Generate delta-training curriculum upon protocol amendment publication.
   */
  async generateAmendmentCurriculum(payload) {
    const res = await api.post("/api/v1/training/curricula/generate-amendment", payload);
    return res.data;
  },

  /**
   * Assign a curriculum to a clinical site user.
   */
  async assignCurriculum(curriculumId, payload) {
    const res = await api.post(`/api/v1/training/curricula/${curriculumId}/assign`, payload);
    return res.data;
  },

  /**
   * List training assignments for a user, site, or study.
   */
  async listAssignments(params = {}) {
    const res = await api.get("/api/v1/training/assignments", { params });
    return res.data;
  },

  /**
   * Retrieve full details of an assignment including modules and quiz questions.
   */
  async getAssignmentDetail(assignmentId) {
    const res = await api.get(`/api/v1/training/assignments/${assignmentId}`);
    return res.data;
  },

  /**
   * Record time-on-page reading duration progress.
   */
  async recordProgress(assignmentId, payload) {
    const res = await api.post(`/api/v1/training/assignments/${assignmentId}/progress`, payload);
    return res.data;
  },

  /**
   * Submit comprehension quiz answers and receive score evaluation (>= 80% passing).
   */
  async submitQuiz(assignmentId, payload) {
    const res = await api.post(`/api/v1/training/assignments/${assignmentId}/submit-quiz`, payload);
    return res.data;
  },

  /**
   * Capture 21 CFR Part 11 electronic signature, generate sealed PDF certificate, and push to eTMF & eISF.
   */
  async signOffTraining(assignmentId, payload) {
    const res = await api.post(`/api/v1/training/assignments/${assignmentId}/sign-off`, payload);
    return res.data;
  },

  /**
   * Retrieve generated completion certificate and SHA-256 seal for an assignment.
   */
  async getCertificate(assignmentId) {
    const res = await api.get(`/api/v1/training/assignments/${assignmentId}/certificate`);
    return res.data;
  },

  /**
   * Check whether a user is qualified for EDC write permissions on a protocol version.
   */
  async checkGatingStatus(params = {}) {
    const res = await api.get("/api/v1/training/gating-status", { params });
    return res.data;
  },

  /**
   * Programmatically verify qualification gate.
   */
  async verifyGate(payload) {
    const res = await api.post("/api/v1/training/verify-gate", payload);
    return res.data;
  },
};
