import { http, HttpResponse } from "msw";
import { decodeReasonHeader, readJsonBody } from "./shared.js";

export function createKernelObservationsHandlers(store) {
  return [
    http.get(
      "*/api/v1/kernel/observations/:observationRef",
      ({ params, request }) => {
        const url = new URL(request.url);
        return HttpResponse.json(
          store.getObservation(
            decodeURIComponent(String(params.observationRef)),
            {
              studyCode:
                url.searchParams.get("studyId") ||
                url.searchParams.get("study_code") ||
                "",
              siteCode:
                url.searchParams.get("siteId") ||
                url.searchParams.get("site_code") ||
                "",
              subjectCode:
                url.searchParams.get("subjectId") ||
                url.searchParams.get("subject_code") ||
                "",
            }
          )
        );
      }
    ),
    http.post(
      "*/api/v1/kernel/observations/:observationRef/corrections",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const reason = decodeReasonHeader(request);
        const result = store.correctObservation(
          decodeURIComponent(String(params.observationRef)),
          body,
          reason
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
    http.post(
      "*/api/v1/kernel/observations/:observationRef/verifications",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.verifyObservation(
          decodeURIComponent(String(params.observationRef)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data, { status: 201 });
      }
    ),
    http.post(
      "*/api/v1/kernel/assertions/:assertionRef/retraction",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const result = store.retractAssertion(
          decodeURIComponent(String(params.assertionRef)),
          body,
          decodeReasonHeader(request)
        );
        if (result.conflict) {
          return HttpResponse.json(result.problem, { status: 409 });
        }
        return HttpResponse.json(result.data);
      }
    ),
  ];
}
