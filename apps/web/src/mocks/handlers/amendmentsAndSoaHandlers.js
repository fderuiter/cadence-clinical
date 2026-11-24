import { buildAmendmentReconsentPayload } from "demo-fixtures/mocks";
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
      "*/api/v1/execution/amendments/:studyId/subject-impact",
      ({ params }) =>
        HttpResponse.json({
          study_id: params.studyId,
          target_version: "2.0",
          impacted_subjects: [],
          total_impacted: 0,
        })
    ),
    http.post("*/api/v1/designer/amendments/diff", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        study_id: body?.study_id || "DEMO-01",
        base_version_tag: body?.base_version_tag || "1.0.0",
        amended_version_tag: body?.amended_version_tag || "2.0.0",
        impact_summary: {
          impacted_sites: 2,
          subjects_requiring_reconsent: 2,
          added_procedures: 1,
          modified_visits: 1,
        },
        soa_matrix_diffs: [],
      });
    }),
    http.post("*/api/v1/designer/amendments/branch", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        status: "BRANCHED",
        study_id: body?.study_id || "DEMO-01",
        version_tag: body?.target_version_tag || "2.0.0",
      });
    }),
    http.post("*/api/v1/execution/amendments/publish", () =>
      HttpResponse.json({ status: "PUBLISHED" })
    ),
    http.post("*/api/v1/execution/amendments/reconsent", () =>
      HttpResponse.json(buildAmendmentReconsentPayload({ status: "COMPLETED" }))
    ),
    http.post("*/api/v1/execution/amendments/bulk-reconsent", () =>
      HttpResponse.json({ status: "COMPLETED", updated_count: 1 })
    ),
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
