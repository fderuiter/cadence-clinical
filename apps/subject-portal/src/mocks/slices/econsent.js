import { DEMO_STUDY } from "demo-fixtures";
import {
  createDemoAmendmentReconsentRoute,
  createDemoHealthRoute,
  createDemoSignatureVerificationRoute,
} from "demo-fixtures/routes";
import {
  MOCK_APPROVED_CONTENT,
  MOCK_COMPREHENSION_CHECK,
} from "../../data/mock-data.js";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";

const DEFAULT_PENDING_RECONSENT = Object.freeze({
  id: "req_amendment_v2",
  study_id: DEMO_STUDY?.code || "",
  protocol_version: "2.0",
  change_summary:
    "Protocol amendment v2.0 schedule clarification requiring participant re-consent.",
});

function cloneConsentData(value) {
  return JSON.parse(JSON.stringify(value));
}

export function createEconsentSliceStore() {
  const state = {
    pendingReconsents: [],
    consentCaptured: false,
  };

  function reset() {
    state.pendingReconsents = [cloneConsentData(DEFAULT_PENDING_RECONSENT)];
    state.consentCaptured = false;
  }

  reset();

  return {
    state,
    reset,
  };
}

export function createEconsentSliceHandlers({
  http,
  HttpResponse,
  slice,
  nextSeq,
}) {
  return [
    createDemoHealthRoute(
      { http, HttpResponse },
      { status: "healthy", portal: "demo" }
    ),
    createDemoSignatureVerificationRoute(
      { http, HttpResponse },
      {
        defaultAction: "portal.sign",
        createToken: () => `portal_demo_sig_${demoNowMs(nextSeq())}`,
      }
    ),
    http.get("*/api/v1/econsent/templates/:templateId/approved-content", () =>
      HttpResponse.json(cloneConsentData(MOCK_APPROVED_CONTENT))
    ),
    http.get(
      "*/api/v1/econsent/templates/:templateId/versions/:version/comprehension-checks",
      () => HttpResponse.json(cloneConsentData(MOCK_COMPREHENSION_CHECK))
    ),
    http.post(
      "*/api/v1/econsent/templates/:templateId/versions/:version/submit-answers",
      () =>
        HttpResponse.json({
          passed: true,
          score_pct: 100,
          evaluated_at: demoNowIso(nextSeq()),
        })
    ),
    http.post(
      "*/api/v1/econsent/templates/:templateId/versions/:version/capture-consent",
      () => {
        slice.state.consentCaptured = true;
        const seq = nextSeq();
        return HttpResponse.json({
          consent_id: `icf_demo_${demoNowMs(seq)}`,
          status: "SIGNED",
          signed_at: demoNowIso(seq),
        });
      }
    ),
    http.get("*/api/v1/econsent/reconsent/pending/:studyId", () =>
      HttpResponse.json(slice.state.pendingReconsents)
    ),
    createDemoAmendmentReconsentRoute(
      { http, HttpResponse },
      {
        buildResponse: () => ({
          status: "RECORDED",
          recorded_at: demoNowIso(nextSeq()),
        }),
      }
    ),
    http.post(
      "*/api/v1/econsent/reconsent/complete/:requirementId",
      ({ params }) => {
        slice.state.pendingReconsents = slice.state.pendingReconsents.filter(
          (item) => String(item.id) !== String(params.requirementId)
        );
        return HttpResponse.json({
          requirement_id: params.requirementId,
          status: "COMPLETED",
        });
      }
    ),
  ];
}
