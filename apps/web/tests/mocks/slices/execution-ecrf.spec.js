import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: execution-ecrf", () => {
  const api = useDemoSliceServer();

  it("persists subject enrollment, state transitions, and cross-slice kernel enrollment lookup", async () => {
    const initial = await api.requestJson(
      "/api/v1/execution/subjects?study_id=DEMO-01"
    );
    expect(initial.status).toBe(200);
    expect(initial.data.items.length).toBe(2);

    const created = await api.requestJson("/api/v1/execution/subjects", {
      method: "POST",
      headers: { "X-Change-Reason": "Enroll synthetic participant" },
      body: JSON.stringify({
        study_id: "DEMO-01",
        site_id: "SITE-101",
        subject_id: "SUBJ-1004",
        status: "SCREENED",
      }),
    });
    expect(created.status).toBe(201);
    expect(created.data.subject_id).toBe("SUBJ-1004");

    const patched = await api.requestJson(
      `/api/v1/execution/subjects/${created.data.id}/state`,
      {
        method: "PATCH",
        headers: { "X-Change-Reason": "Confirm eligibility" },
        body: JSON.stringify({ status: "ENROLLED" }),
      }
    );
    expect(patched.status).toBe(200);

    const lookup = await api.requestJson(
      "/api/v1/kernel/enrollments/lookup?study_code=DEMO-01&subject_code=SUBJ-1004"
    );
    expect(lookup.status).toBe(200);
    expect(lookup.data).toMatchObject({
      study_code: "DEMO-01",
      site_code: "SITE-101",
      subject_code: "SUBJ-1004",
      enrollment_ref: `enrollment:${created.data.id}@1`,
    });

    const wrongSiteRes = await api.fetchPath(
      "/api/v1/kernel/enrollments/lookup?study_code=DEMO-01&subject_code=SUBJ-1004&site_code=SITE-999"
    );
    expect(wrongSiteRes.status).toBe(404);
    expect(wrongSiteRes.headers.get("Content-Type")).toContain(
      "application/problem+json"
    );

    const schemes = await api.requestJson(
      "/api/v1/execution/randomization/schemes?study_id=DEMO-01"
    );
    expect(schemes.status).toBe(200);
    expect(schemes.data.length).toBeGreaterThanOrEqual(1);

    const kits = await api.requestJson(
      "/api/v1/execution/rtsm/kits/inventory/DEMO-01/SITE-101"
    );
    expect(kits.status).toBe(200);
    expect(Array.isArray(kits.data)).toBe(true);
  });

  it("persists eCRF saves, non-mutating form previews, signatures, queries, and canvas form listings", async () => {
    const enrollmentId = "00000000-0000-0000-0000-000000001001"; // deid-ignore
    const missingEnrollment = await api.fetchPath("/api/v1/execution/forms");
    expect(missingEnrollment.status).toBe(400);

    const opened = await api.requestJson("/api/v1/execution/forms", {
      method: "POST",
      headers: { "X-Change-Reason": "Open baseline vitals form" },
      body: JSON.stringify({
        enrollment_id: enrollmentId,
        visit_code: "VISIT-BASELINE",
        form_code: "FORM-VITALS",
      }),
    });
    expect(opened.status).toBe(201);
    const formId = opened.data.form_id;

    const before = await api.requestJson(`/api/v1/execution/forms/${formId}`);
    const sbpBefore = before.data.items.find(
      (it) => it.item_id === "item-sysbp"
    );
    expect(sbpBefore.value.value).toBe("128");

    const preview = await api.requestJson(
      `/api/v1/execution/forms/${formId}/preview`,
      {
        method: "POST",
        body: JSON.stringify({
          values: [
            {
              item_id: "item-sysbp",
              value: {
                type: "quantity",
                value: "75",
                unit: "mm[Hg]",
                comparator: null,
              },
            },
            {
              item_id: "item-diabp",
              value: {
                type: "quantity",
                value: "82",
                unit: "mm[Hg]",
                comparator: null,
              },
            },
          ],
        }),
      }
    );
    expect(preview.status).toBe(200);
    expect(preview.data.form_id).toBe(formId);
    expect(
      preview.data.warnings.some((w) => w.rule_id === "RULE-BP-INVERTED")
    ).toBe(true);

    const afterPreview = await api.requestJson(
      `/api/v1/execution/forms/${formId}`
    );
    expect(
      afterPreview.data.items.find((it) => it.item_id === "item-sysbp")?.value
        ?.value
    ).toBe("128");

    const saved = await api.requestJson(
      `/api/v1/execution/forms/${formId}/values`,
      {
        method: "PUT",
        headers: {
          "Idempotency-Key": "idem-save-1",
          "X-Change-Reason": "Source worksheet update",
        },
        body: JSON.stringify({
          values: [
            {
              item_id: "item-sysbp",
              expected_revision: sbpBefore.revision,
              value: {
                type: "quantity",
                value: "122",
                unit: "mm[Hg]",
                comparator: null,
              },
            },
          ],
        }),
      }
    );
    expect(saved.status).toBe(200);

    const raised = await api.requestJson(
      `/api/v1/execution/forms/${formId}/queries`,
      {
        method: "POST",
        headers: { "X-Change-Reason": "Verify cuff position" },
        body: JSON.stringify({
          observation_id: sbpBefore.observation_id,
          revision: 2,
          text: "Please confirm seated 5-minute rest before reading.",
        }),
      }
    );
    expect(raised.status).toBe(201);
    expect(raised.data.status).toBe("open");

    const studyQueries = await api.requestJson(
      "/api/v1/execution/forms/queries?study_id=00000000-0000-0000-0000-000000000001&status=open&site=SITE-101" // deid-ignore
    );
    expect(studyQueries.status).toBe(200);
    expect(studyQueries.data.length).toBeGreaterThanOrEqual(1);

    const answered = await api.requestJson(
      `/api/v1/execution/forms/${formId}/queries/${raised.data.query_id}/answer`,
      {
        method: "POST",
        headers: { "X-Change-Reason": "Site coordinator response" },
        body: JSON.stringify({
          text: "Confirmed seated rest per protocol section 6.2.",
        }),
      }
    );
    expect(answered.status).toBe(200);
    expect(answered.data.status).toBe("answered");

    const signed = await api.requestJson(
      `/api/v1/execution/forms/${formId}/signatures`,
      {
        method: "POST",
        headers: { "X-Change-Reason": "Investigator review complete" },
        body: JSON.stringify({ meaning: "approved" }),
      }
    );
    expect(signed.status).toBe(201);

    const summaries = await api.requestJson(
      `/api/v1/execution/forms?enrollment_id=${enrollmentId}`
    );
    const baselineSummary = summaries.data.find((s) => s.form_id === formId);
    expect(baselineSummary).toMatchObject({
      complete: true,
      missing_required_count: 0,
      open_query_count: 1,
      answered_query_count: 1,
      signed: true,
      signature_superseded: false,
      signature_count: 1,
    });

    const unscheduled = await api.requestJson("/api/v1/execution/forms", {
      method: "POST",
      headers: { "X-Change-Reason": "Open unscheduled adverse event form" },
      body: JSON.stringify({
        enrollment_id: enrollmentId,
        collection_spec_id: "form-adverse-event",
        planned_activity_id: null,
        collected_at: "2026-08-05T14:00:00Z", // deid-ignore
      }),
    });
    expect(unscheduled.status).toBe(201);
    expect(unscheduled.data.collection_title).toBe("Adverse Event (CDASH AE)");

    const chem = await api.requestJson("/api/v1/execution/forms", {
      method: "POST",
      headers: { "X-Change-Reason": "Open chemistry panel in canvas" },
      body: JSON.stringify({
        enrollment_id: enrollmentId,
        collection_spec_id: "form-chem-panel",
        planned_activity_id: "visit-week4",
      }),
    });
    expect(chem.status).toBe(201);
  });
});
