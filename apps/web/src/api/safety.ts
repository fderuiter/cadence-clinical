import { openApiClient, unwrapResponse } from "./client";

export interface SignalAlertQueryParams {
  study_id?: string;
  target_drug?: string;
  status?: string;
  severity?: string;
}

export interface BulkReviewAlertsPayload {
  alert_ids: string[];
  decision: string;
  justification: string;
  clinical_summary?: string;
  assigned_safety_officer?: string;
  recommended_action?: string;
}

export interface BulkSignAlertsPayload {
  alert_ids: string[];
  user_id: string;
  signature_secret: string;
  decision: string;
  justification: string;
  meaning_of_signature?: string;
  clinical_summary?: string;
}

export interface SafetySignalAlertDTO {
  id: string;
  target_drug?: string;
  meddra_term: string;
  term_level: string;
  meddra_soc?: string;
  case_count_a: number;
  expected_count_e: number;
  prr: number;
  prr_ci_lower: number;
  prr_ci_upper: number;
  chi_square_yates: number;
  ror: number;
  ror_ci_lower: number;
  ror_ci_upper: number;
  ebgm: number;
  eb05: number;
  eb95: number;
  severity: string;
  status: string;
  triage_summary: string;
  assigned_safety_officer?: string | null;
  review_decision?: string | null;
  review_justification?: string | null;
  clinical_summary?: string | null;
  reviewed_by?: string | null;
  reviewed_at?: string | null;
  esignature_manifest_id?: string | null;
  study_id?: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string;
  version_index: number;
}

export interface BulkSignAlertsResponse {
  updated_count: number;
  status: string;
  esignature_manifest_id: string;
  results: Array<{
    alert_id: string;
    status: string;
    reviewed_by: string;
    reviewed_at: string;
    esignature_manifest_id: string;
    message: string;
  }>;
  alerts: SafetySignalAlertDTO[];
  message: string;
}

export const safetyApi = {
  listSignalAlerts(params: SignalAlertQueryParams = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/safety/signals/alerts" as any, {
        params: { query: params },
      })
    );
  },

  bulkReviewAlerts(payload: BulkReviewAlertsPayload) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/safety/signals/alerts/bulk-review" as any, {
        body: payload as any,
      })
    );
  },

  bulkSignAlerts(payload: BulkSignAlertsPayload) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/safety/signals/alerts/bulk-sign" as any, {
        body: payload as any,
      })
    );
  },
};
