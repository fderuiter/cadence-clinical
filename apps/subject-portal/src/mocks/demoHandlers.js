import { http, HttpResponse } from "msw";
import {
  MOCK_APPROVED_CONTENT,
  MOCK_ASSIGNMENTS,
  MOCK_COMPREHENSION_CHECK,
  MOCK_INSTRUMENTS,
  MOCK_NOTIFICATIONS,
} from "../data/mock-data.js";

const DEFAULT_PENDING_RECONSENT = {
  id: "req_amendment_v2",
  study_id: "STUDY-01",
  protocol_version: "2.0",
  change_summary:
    "Protocol amendment v2.0 schedule clarification requiring participant re-consent.",
};

function clonePortalData(value) {
  return JSON.parse(JSON.stringify(value));
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
    redeemedActivation: null,
  };

  function reset() {
    state.assignments = clonePortalData(MOCK_ASSIGNMENTS);
    state.instruments = clonePortalData(MOCK_INSTRUMENTS);
    state.notifications = clonePortalData(MOCK_NOTIFICATIONS);
    state.submissions = [];
    state.pendingReconsents = [clonePortalData(DEFAULT_PENDING_RECONSENT)];
    state.consentCaptured = false;
    state.walletBalance = 125.0;
    state.complianceScore = 92;
    state.redeemedActivation = null;
  }

  reset();

  return {
    state,
    reset,
  };
}

export const portalDemoStore = createPortalDemoStore();

export function createPortalDemoHandlers(store = portalDemoStore) {
  return [
    http.get("*/health", () =>
      HttpResponse.json({ status: "healthy", portal: "demo" })
    ),
    http.post("*/api/v1/auth/signature-verification", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      return HttpResponse.json({
        sig_token: `portal_demo_sig_${Date.now()}`,
        action: body?.action || "portal.sign",
        expires_in: 300,
      });
    }),
    http.get("*/api/v1/interop/assignments/subject/:subjectId", ({ params }) =>
      HttpResponse.json(
        store.state.assignments.map((item) => ({
          ...item,
          subject_id: params.subjectId || "subject_001",
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
        subject_id: params.subjectId || "subject_001",
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
          target.acknowledged_at = new Date().toISOString();
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
      const record = {
        submission_id: `sub_${Date.now()}`,
        status: "ACCEPTED",
        received_at: new Date().toISOString(),
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
        synced_at: new Date().toISOString(),
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
          evaluated_at: new Date().toISOString(),
        })
    ),
    http.post(
      "*/api/v1/econsent/templates/:templateId/versions/:version/capture-consent",
      () => {
        store.state.consentCaptured = true;
        return HttpResponse.json({
          consent_id: `icf_demo_${Date.now()}`,
          status: "SIGNED",
          signed_at: new Date().toISOString(),
        });
      }
    ),
    http.get("*/api/v1/econsent/reconsent/pending/:studyId", () =>
      HttpResponse.json(store.state.pendingReconsents)
    ),
    http.post("*/api/v1/execution/amendments/reconsent", () =>
      HttpResponse.json({
        status: "RECORDED",
        recorded_at: new Date().toISOString(),
      })
    ),
    http.post(
      "*/api/v1/execution/subjects/activation-codes/redeem",
      async ({ request }) => {
        const body = await request.json().catch(() => ({}));
        const code = String(body?.activation_code || "").trim();
        if (!code) {
          return HttpResponse.json(
            {
              title: "Validation failed",
              status: 400,
              errors: { activation_code: ["Activation code is required."] },
            },
            { status: 400 }
          );
        }
        const redeemedAt = new Date().toISOString();
        const expiresAt = new Date(
          Date.now() + 30 * 24 * 60 * 60 * 1000
        ).toISOString();
        const binding = {
          subject_id: "SUBJ-101-001",
          study_id: "STUDY-01",
          site_id: "SITE-101",
          redeemed_at: redeemedAt,
          expires_at: expiresAt,
        };
        store.state.redeemedActivation = binding;
        return HttpResponse.json(binding);
      }
    ),
    http.get("*/api/v1/execution/subjects/activation-codes/me", () => {
      if (!store.state.redeemedActivation) {
        return HttpResponse.json(
          { title: "No redeemed activation code found.", status: 404 },
          { status: 404 }
        );
      }
      return HttpResponse.json(store.state.redeemedActivation);
    }),
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
