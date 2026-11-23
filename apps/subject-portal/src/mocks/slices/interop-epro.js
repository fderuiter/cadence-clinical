import { DEFAULT_DEMO_PARTICIPANT } from "demo-fixtures";
import {
  MOCK_ASSIGNMENTS,
  MOCK_INSTRUMENTS,
  MOCK_NOTIFICATIONS,
} from "../../data/mock-data.js";
import { demoNowIso, demoNowMs } from "../../utils/demoMode.js";

const DEFAULT_DEMO_SUBJECT_ID = DEFAULT_DEMO_PARTICIPANT?.subjectCode || "";

function clonePortalData(value) {
  return JSON.parse(JSON.stringify(value));
}

export function createInteropEproSliceStore() {
  const state = {
    assignments: [],
    instruments: {},
    notifications: [],
    submissions: [],
    complianceScore: 92,
  };

  function reset() {
    state.assignments = clonePortalData(MOCK_ASSIGNMENTS);
    state.instruments = clonePortalData(MOCK_INSTRUMENTS);
    state.notifications = clonePortalData(MOCK_NOTIFICATIONS);
    state.submissions = [];
    state.complianceScore = 92;
  }

  reset();

  return {
    state,
    reset,
  };
}

export function createInteropEproSliceHandlers({
  http,
  HttpResponse,
  slice,
  nextSeq,
}) {
  return [
    http.get("*/api/v1/interop/assignments/subject/:subjectId", ({ params }) =>
      HttpResponse.json(
        slice.state.assignments.map((item) => ({
          ...item,
          subject_id: params.subjectId || DEFAULT_DEMO_SUBJECT_ID,
        }))
      )
    ),
    http.get("*/api/v1/interop/subjects/:subjectId/instruments", () =>
      HttpResponse.json(Object.values(slice.state.instruments))
    ),
    http.get("*/api/v1/interop/instruments/:id", ({ params }) => {
      const found = slice.state.instruments[params.id] ||
        Object.values(slice.state.instruments)[0] || {
          id: params.id,
          title: "Synthetic ePRO Diary",
          questions: [],
        };
      return HttpResponse.json(found);
    }),
    http.get("*/api/v1/interop/subjects/:subjectId/compliance", ({ params }) =>
      HttpResponse.json({
        subject_id: params.subjectId || DEFAULT_DEMO_SUBJECT_ID,
        adherence_percentage: slice.state.complianceScore,
        completed_count: 11 + slice.state.submissions.length,
        missed_count: 1,
        streak_days: 7 + slice.state.submissions.length,
      })
    ),
    http.get("*/api/v1/interop/subjects/:subjectId/notifications", () =>
      HttpResponse.json(slice.state.notifications)
    ),
    http.post(
      "*/api/v1/interop/notifications/:notificationId/acknowledge",
      ({ params }) => {
        const target = slice.state.notifications.find(
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
      slice.state.submissions.push(record);
      slice.state.complianceScore = Math.min(
        100,
        slice.state.complianceScore + 4
      );
      if (body?.assignment_id) {
        const assignment = slice.state.assignments.find(
          (a) => a.id === body.assignment_id
        );
        if (assignment) assignment.status = "COMPLETED";
      }
      return HttpResponse.json(record);
    }),
    http.post("*/api/v1/interop/epro/sync", async ({ request }) => {
      const body = await request.json().catch(() => ({}));
      const items = Array.isArray(body?.submissions) ? body.submissions : [];
      slice.state.submissions.push(...items);
      return HttpResponse.json({
        synced_count: items.length,
        status: "SYNCED",
        synced_at: demoNowIso(nextSeq()),
      });
    }),
  ];
}
