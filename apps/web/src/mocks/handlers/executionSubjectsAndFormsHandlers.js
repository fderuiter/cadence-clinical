import { http, HttpResponse } from "msw";
import { resolveDemoCaller } from "../demoPolicy.js";
import { decodeReasonHeader, readJsonBody } from "./shared.js";

export function createExecutionSubjectsAndFormsHandlers(store) {
  return [
    http.get("*/api/v1/execution/subjects", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listSubjects({
          studyId: url.searchParams.get("study_id") || undefined,
          siteId: url.searchParams.get("site_id") || undefined,
          status: url.searchParams.get("status") || undefined,
          q: url.searchParams.get("q") || undefined,
          limit: url.searchParams.get("limit") || 50,
          offset: url.searchParams.get("offset") || 0,
        })
      );
    }),
    http.post("*/api/v1/execution/subjects", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.createSubject(body), { status: 201 });
    }),
    http.get("*/api/v1/execution/subjects/counts", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.getSubjectCounts({
          studyId: url.searchParams.get("study_id") || undefined,
          siteId: url.searchParams.get("site_id") || undefined,
        })
      );
    }),
    http.get("*/api/v1/execution/subjects/:subjectId", ({ params, request }) =>
      HttpResponse.json(
        store.getSubjectDetail(
          decodeURIComponent(String(params.subjectId)),
          resolveDemoCaller(request)
        )
      )
    ),
    http.patch(
      "*/api/v1/execution/subjects/:subjectId/state",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.updateSubjectState(
            decodeURIComponent(String(params.subjectId)),
            body
          )
        );
      }
    ),
    http.get(
      "*/api/v1/execution/sites/:siteId/compliance-status",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getSiteCompliance(
            decodeURIComponent(String(params.siteId)),
            url.searchParams.get("study_id") || "DEMO-01",
            url.searchParams.get("milestone") || "SITE_ACTIVATION"
          )
        );
      }
    ),
    http.post(
      "*/api/v1/execution/sites/:siteId/activate",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.activateSite(
          decodeURIComponent(String(params.siteId)),
          body?.study_id || "DEMO-01"
        );
        if (result.blocked) {
          return HttpResponse.json(result.problem, { status: 400 });
        }
        return HttpResponse.json(result.data);
      }
    ),

    // Register specific `/queries` routes BEFORE `:formId` catch-alls
    http.get("*/api/v1/execution/forms/queries/digests", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listQueryDigests(
          {
            studyId: url.searchParams.get("study_id"),
            site: url.searchParams.get("site"),
            recipientRole: url.searchParams.get("recipient_role"),
          },
          resolveDemoCaller(request)
        )
      );
    }),
    http.post("*/api/v1/execution/forms/queries/escalate", ({ request }) => {
      const url = new URL(request.url);
      const reason = decodeReasonHeader(request);
      if (!reason) {
        return HttpResponse.json(
          {
            title: "Reason for change required",
            status: 422,
            code: "K.Command.ReasonForChangeRequired",
            detail: "X-Change-Reason header is required.",
          },
          { status: 422 }
        );
      }
      return HttpResponse.json(
        store.escalateStudyQueries(
          url.searchParams.get("study_id"),
          reason,
          resolveDemoCaller(request)
        )
      );
    }),
    http.get("*/api/v1/execution/forms/queries", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listEnrollmentQueries(
          {
            enrollmentId: url.searchParams.get("enrollment_id"),
            studyId: url.searchParams.get("study_id"),
            status: url.searchParams.get("status"),
            owner: url.searchParams.get("owner"),
            origin: url.searchParams.get("origin"),
            priority: url.searchParams.get("priority"),
            escalated: url.searchParams.get("escalated"),
            assignee: url.searchParams.get("assignee"),
            site: url.searchParams.get("site"),
            participant: url.searchParams.get("participant"),
            valueChanged: url.searchParams.get("value_changed"),
          },
          resolveDemoCaller(request)
        )
      );
    }),
    http.get("*/api/v1/execution/forms/sdv-alerts", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listSdvAlerts(
          {
            studyId: url.searchParams.get("study_id"),
            enrollmentId: url.searchParams.get("enrollment_id"),
          },
          resolveDemoCaller(request)
        )
      );
    }),
    http.get(
      "*/api/v1/execution/forms/:formId/queries",
      ({ params, request }) =>
        HttpResponse.json(
          store.listFormQueries(
            decodeURIComponent(String(params.formId)),
            resolveDemoCaller(request)
          )
        )
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/queries",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.raiseQuery(
            decodeURIComponent(String(params.formId)),
            body,
            decodeReasonHeader(request)
          ),
          { status: 201 }
        );
      }
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/queries/:queryId/:action",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.transitionQuery(
          decodeURIComponent(String(params.formId)),
          decodeURIComponent(String(params.queryId)),
          String(params.action),
          body,
          decodeReasonHeader(request)
        );
        if (result.notFound) {
          return HttpResponse.json(result.problem, { status: 404 });
        }
        if (result.invalid) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.get("*/api/v1/execution/forms", ({ request }) => {
      const url = new URL(request.url);
      const enrollmentId = (url.searchParams.get("enrollment_id") || "").trim();
      if (!enrollmentId) {
        return HttpResponse.json(
          {
            title: "Missing enrollment_id",
            status: 400,
            detail: "Query parameter 'enrollment_id' is required.",
          },
          { status: 400 }
        );
      }
      return HttpResponse.json(store.listEnrollmentForms(enrollmentId));
    }),
    http.post("*/api/v1/execution/forms", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.openForm(body), { status: 201 });
    }),
    http.get("*/api/v1/execution/forms/:formId", ({ params }) =>
      HttpResponse.json(
        store.getForm(decodeURIComponent(String(params.formId)))
      )
    ),
    http.put(
      "*/api/v1/execution/forms/:formId/values",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.saveFormValues(
          decodeURIComponent(String(params.formId)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.post(
      "*/api/v1/execution/forms/:formId/signatures",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.signForm(
          decodeURIComponent(String(params.formId)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data, { status: 201 });
      }
    ),
  ];
}
