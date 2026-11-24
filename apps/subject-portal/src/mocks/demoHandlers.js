import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_STUDY,
} from "demo-fixtures";
import {
  buildAmendmentReconsentPayload,
  buildDemoHealthPayload,
  buildSignatureVerificationOutcome,
} from "demo-fixtures/mocks";
import { http, HttpResponse } from "msw";
import {
  MOCK_APPROVED_CONTENT,
  MOCK_ASSIGNMENTS,
  MOCK_COMPREHENSION_CHECK,
  MOCK_INSTRUMENTS,
  MOCK_NOTIFICATIONS,
} from "../data/mock-data.js";
import { demoNowIso, demoNowMs } from "../utils/demoMode.js";

const DEFAULT_DEMO_SUBJECT_ID =
  DEFAULT_DEMO_PARTICIPANT?.subjectCode || "SUBJ-101-001";
const DEFAULT_DEMO_STUDY_ID = DEMO_STUDY?.code || "CADENCE-101";
const DEFAULT_DEMO_SITE_ID = DEFAULT_DEMO_SITE?.code || "SITE-101";

const DEFAULT_PENDING_RECONSENT = {
  id: "req_amendment_v2",
  study_id: DEMO_STUDY?.code || "",
  protocol_version: "2.0",
  change_summary:
    "Protocol amendment v2.0 schedule clarification requiring participant re-consent.",
};

function clonePortalData(value) {
  return JSON.parse(JSON.stringify(value));
}

function createInitialPortalWallet(subjectId = DEFAULT_DEMO_SUBJECT_ID) {
  return {
    id: `wallet_${subjectId}`,
    subjectId,
    studyId: DEFAULT_DEMO_STUDY_ID,
    siteId: DEFAULT_DEMO_SITE_ID,
    currentBalance: 125.0,
    lifetimeStipends: 125.0,
    lifetimeReimbursements: 0.0,
    lifetimeDisbursements: 0.0,
    taxableYtd: 125.0,
    is1099ThresholdExceeded: false,
    w9Status: "NOT_REQUIRED",
    currency: "USD",
    status: "ACTIVE",
    virtualCardMask: "**** **** **** 4821",
    virtualCardExpiry: "12/29",
    achRoutingNumber: "*****0021",
    achRoutingMask: "*****0021",
    achAccountMask: "ACH-***4321",
    achAccountHolder: `Participant ${subjectId}`,
    recentTransactions: [],
    expenseClaims: [],
    creditedKeys: new Set(),
    expenseIdempotencyMap: new Map(),
    disburseIdempotencyMap: new Map(),
  };
}

export function createPortalDemoStore() {
  const state = {
    assignments: [],
    instruments: {},
    notifications: [],
    submissions: [],
    pendingReconsents: [],
    consentCaptured: false,
    walletBalance: 125.0,
    complianceScore: 92,
    actionSeq: 0,
    walletLedger: new Map(),
    ocrTokens: new Map(),
    consumedOcrTokens: new Set(),
    recordedReceiptDigests: new Set(),
    walletActionSeq: 0,
  };

  function resetWalletLedger() {
    state.walletLedger.clear();
    state.ocrTokens.clear();
    state.consumedOcrTokens.clear();
    state.recordedReceiptDigests.clear();
    state.walletActionSeq = 0;
  }

  function getOrCreateWallet(subjectId = DEFAULT_DEMO_SUBJECT_ID) {
    if (!state.walletLedger.has(subjectId)) {
      state.walletLedger.set(subjectId, createInitialPortalWallet(subjectId));
    }
    return state.walletLedger.get(subjectId);
  }

  function nextWalletTimestampMs() {
    state.walletActionSeq += 1;
    return demoNowMs(state.walletActionSeq);
  }

  function nextWalletTimestampIso() {
    state.walletActionSeq += 1;
    return demoNowIso(state.walletActionSeq);
  }

  function reset() {
    state.assignments = clonePortalData(MOCK_ASSIGNMENTS);
    state.instruments = clonePortalData(MOCK_INSTRUMENTS);
    state.notifications = clonePortalData(MOCK_NOTIFICATIONS);
    state.submissions = [];
    state.pendingReconsents = [clonePortalData(DEFAULT_PENDING_RECONSENT)];
    state.consentCaptured = false;
    state.walletBalance = 125.0;
    state.complianceScore = 92;
    state.actionSeq = 0;
    resetWalletLedger();
  }

  reset();

  return {
    state,
    reset,
    resetWalletLedger,
    getOrCreateWallet,
    nextWalletTimestampMs,
    nextWalletTimestampIso,
  };
}

export const portalDemoStore = createPortalDemoStore();

export function createPortalDemoHandlers(store = portalDemoStore) {
  const nextSeq = () => {
    store.state.actionSeq += 1;
    return store.state.actionSeq;
  };

  return [
    http.get("*/health", () =>
      HttpResponse.json(
        buildDemoHealthPayload({ surface: "portal", value: "demo" })
      )
    ),
    http.post("*/api/v1/auth/signature-verification", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const outcome = buildSignatureVerificationOutcome(body, {
        tokenPrefix: "portal_demo_sig_",
        defaultAction: "portal.sign",
        timestampMs: demoNowMs(nextSeq()),
      });
      return HttpResponse.json(outcome.body, { status: outcome.status });
    }),
    http.get("*/api/v1/interop/assignments/subject/:subjectId", ({ params }) =>
      HttpResponse.json(
        store.state.assignments.map((item) => ({
          ...item,
          subject_id: params.subjectId || DEFAULT_DEMO_SUBJECT_ID,
        }))
      )
    ),
    http.get("*/api/v1/interop/subjects/:subjectId/instruments", () =>
      HttpResponse.json(Object.values(store.state.instruments))
    ),
    http.get("*/api/v1/interop/instruments/:id", ({ params }) => {
      const found = store.state.instruments[params.id] ||
        Object.values(store.state.instruments)[0] || {
          id: params.id,
          title: "Synthetic ePRO Diary",
          questions: [],
        };
      return HttpResponse.json(found);
    }),
    http.get("*/api/v1/interop/subjects/:subjectId/compliance", ({ params }) =>
      HttpResponse.json({
        subject_id: params.subjectId || DEFAULT_DEMO_SUBJECT_ID,
        adherence_percentage: store.state.complianceScore,
        completed_count: 11 + store.state.submissions.length,
        missed_count: 1,
        streak_days: 7 + store.state.submissions.length,
      })
    ),
    http.get("*/api/v1/interop/subjects/:subjectId/notifications", () =>
      HttpResponse.json(store.state.notifications)
    ),
    http.post(
      "*/api/v1/interop/notifications/:notificationId/acknowledge",
      ({ params }) => {
        const target = store.state.notifications.find(
          (n) => String(n.id) === String(params.notificationId)
        );
        if (target) {
          target.is_read = true;
          target.acknowledged = true;
          target.status = "ACKNOWLEDGED";
          target.acknowledged_at = demoNowIso(nextSeq());
        }
        return HttpResponse.json(
          target || {
            id: params.notificationId,
            is_read: true,
            acknowledged: true,
            status: "ACKNOWLEDGED",
          }
        );
      }
    ),
    http.post("*/api/v1/interop/epro/submit", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const seq = nextSeq();
      const record = {
        submission_id: `sub_${demoNowMs(seq)}`,
        status: "ACCEPTED",
        received_at: demoNowIso(seq),
        ...body,
      };
      store.state.submissions.push(record);
      store.state.complianceScore = Math.min(
        100,
        store.state.complianceScore + 4
      );
      if (body?.assignment_id) {
        const assignment = store.state.assignments.find(
          (a) => a.id === body.assignment_id
        );
        if (assignment) assignment.status = "COMPLETED";
      }
      return HttpResponse.json(record);
    }),
    http.post("*/api/v1/interop/epro/sync", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const items = Array.isArray(body?.submissions) ? body.submissions : [];
      store.state.submissions.push(...items);
      return HttpResponse.json({
        synced_count: items.length,
        status: "SYNCED",
        synced_at: demoNowIso(nextSeq()),
      });
    }),
    http.get("*/api/v1/econsent/templates/:templateId/approved-content", () =>
      HttpResponse.json(clonePortalData(MOCK_APPROVED_CONTENT))
    ),
    http.get(
      "*/api/v1/econsent/templates/:templateId/versions/:version/comprehension-checks",
      () => HttpResponse.json(clonePortalData(MOCK_COMPREHENSION_CHECK))
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
        store.state.consentCaptured = true;
        const seq = nextSeq();
        return HttpResponse.json({
          consent_id: `icf_demo_${demoNowMs(seq)}`,
          status: "SIGNED",
          signed_at: demoNowIso(seq),
        });
      }
    ),
    http.get("*/api/v1/econsent/reconsent/pending/:studyId", () =>
      HttpResponse.json(store.state.pendingReconsents)
    ),
    http.post("*/api/v1/execution/amendments/reconsent", () =>
      HttpResponse.json(
        buildAmendmentReconsentPayload({
          status: "RECORDED",
          recordedAt: demoNowIso(nextSeq()),
        })
      )
    ),
    http.post(
      "*/api/v1/econsent/reconsent/complete/:requirementId",
      ({ params }) => {
        store.state.pendingReconsents = store.state.pendingReconsents.filter(
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
