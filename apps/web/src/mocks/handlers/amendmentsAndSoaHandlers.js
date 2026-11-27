import { http, HttpResponse } from "msw";
import { readJsonBody } from "./shared.js";

export function createAmendmentsAndSoaHandlers(store) {
  return [
    http.get("*/api/v1/execution/coding/assignments", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/coding/synonyms/jobs", () =>
      HttpResponse.json([])
    ),
    http.get("*/api/v1/execution/coding/synonyms", () => HttpResponse.json([])),
    http.get(
      "*/api/v1/studies/:studyId/versions/:versionId/soa-projection",
      ({ params }) => {
        const soaState = store.state.soaProjection;
        return HttpResponse.json({
          study_id: params.studyId,
          version_id: params.versionId,
          arms: soaState.arms,
          epochs: soaState.epochs,
          encounters: soaState.encounters,
          rows: soaState.rows,
        });
      }
    ),
    http.post(
      "*/api/v1/studies/:studyId/versions/:versionId/arms",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const armId = body?.id || "ARM-DEMO";
        store.upsertSoaArm(armId, body?.properties || body);
        return HttpResponse.json(
          { id: armId, properties: body?.properties || {} },
          { status: 201 }
        );
      }
    ),
    http.put(
      "*/api/v1/studies/:studyId/versions/:versionId/arms/:armId",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        store.upsertSoaArm(String(params.armId), body?.properties || body);
        return HttpResponse.json({
          id: params.armId,
          properties: body?.properties || {},
        });
      }
    ),
    http.post(
      "*/api/v1/studies/:studyId/versions/:versionId/epochs",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const epochId = body?.id || "EP-DEMO";
        store.upsertSoaEpoch(epochId, body?.properties || body);
        return HttpResponse.json(
          { id: epochId, properties: body?.properties || {} },
          { status: 201 }
        );
      }
    ),
    http.put(
      "*/api/v1/studies/:studyId/versions/:versionId/epochs/:epochId",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        store.upsertSoaEpoch(String(params.epochId), body?.properties || body);
        return HttpResponse.json({
          id: params.epochId,
          properties: body?.properties || {},
        });
      }
    ),
    http.post(
      "*/api/v1/studies/:studyId/versions/:versionId/visits",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const visitId = body?.id || "V-DEMO";
        store.upsertSoaVisit(visitId, body?.properties || body);
        return HttpResponse.json(
          { id: visitId, properties: body?.properties || {} },
          { status: 201 }
        );
      }
    ),
    http.put(
      "*/api/v1/studies/:studyId/versions/:versionId/visits/:visitId",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        store.upsertSoaVisit(String(params.visitId), body?.properties || body);
        return HttpResponse.json({
          id: params.visitId,
          properties: body?.properties || {},
        });
      }
    ),
    http.post(
      "*/api/v1/studies/:studyId/versions/:versionId/procedures",
      async ({ request }) => {
        const body = await readJsonBody(request);
        const procId = body?.id || "ACT-DEMO";
        store.upsertSoaProcedure(procId, body?.properties || body);
        return HttpResponse.json(
          { id: procId, properties: body?.properties || {} },
          { status: 201 }
        );
      }
    ),
    http.put(
      "*/api/v1/studies/:studyId/versions/:versionId/procedures/:procedureId",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        store.upsertSoaProcedure(
          String(params.procedureId),
          body?.properties || body
        );
        return HttpResponse.json({
          id: params.procedureId,
          properties: body?.properties || {},
        });
      }
    ),
    http.post(
      "*/api/v1/studies/:studyId/versions/:versionId/links/:linkType",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        store.linkSoaVisitProcedure(body?.properties || body);
        return HttpResponse.json({
          status: "LINKED",
          link_type: params.linkType,
          ...body,
        });
      }
    ),
  ];
}
