import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: protocol-soa", () => {
  const api = useDemoSliceServer();

  it("mutates and resets protocol rules, interactive SoA projection state, and shared amendment reconsent", async () => {
    const templates = await api.requestJson(
      "/api/v1/kernel/protocol-templates"
    );
    expect(templates.status).toBe(200);
    expect(templates.data.length).toBeGreaterThanOrEqual(1);

    const initialSoa = await api.requestJson(
      "/api/v1/studies/DEMO-01/versions/v2.0/soa-projection"
    );
    expect(initialSoa.status).toBe(200);
    expect(initialSoa.data.arms.length).toBe(2);

    const addArm = await api.requestJson(
      "/api/v1/studies/DEMO-01/versions/v2.0/arms",
      {
        method: "POST",
        body: JSON.stringify({
          id: "ARM-C",
          properties: { name: "Arm C: Open-Label Extension" },
        }),
      }
    );
    expect(addArm.status).toBe(201);

    const addVisit = await api.requestJson(
      "/api/v1/studies/DEMO-01/versions/v2.0/visits",
      {
        method: "POST",
        body: JSON.stringify({
          id: "V-WK8",
          properties: {
            name: "Week 8 Follow-up",
            epoch_id: "EP-TRT-A",
            sequence: 5,
          },
        }),
      }
    );
    expect(addVisit.status).toBe(201);

    const linkCell = await api.requestJson(
      "/api/v1/studies/DEMO-01/versions/v2.0/links/visit-procedure",
      {
        method: "POST",
        body: JSON.stringify({
          properties: {
            encounter_id: "V-WK8",
            activity_id: "ACT-VS",
            is_applicable: true,
            details: "End-of-treatment vitals",
          },
        }),
      }
    );
    expect(linkCell.status).toBe(200);

    const updatedSoa = await api.requestJson(
      "/api/v1/studies/DEMO-01/versions/v2.0/soa-projection"
    );
    expect(updatedSoa.data.arms.some((a) => a.arm_id === "ARM-C")).toBe(true);
    expect(
      updatedSoa.data.encounters.some((v) => v.encounter_id === "V-WK8")
    ).toBe(true);

    const createRule = await api.requestJson("/api/v1/studies/DEMO-01/rules", {
      method: "POST",
      body: JSON.stringify({
        name: "Diastolic Blood Pressure Ceiling",
        target_field: "DIABP",
        expression: "DIABP <= 130",
        severity: "WARNING",
      }),
    });
    expect(createRule.status).toBe(201);

    const reconsent = await api.requestJson(
      "/api/v1/execution/amendments/reconsent",
      {
        method: "POST",
        body: JSON.stringify({
          study_id: "DEMO-01",
          subject_id: "SUBJ-1001",
          target_version: "2.0",
        }),
      }
    );
    expect(reconsent.status).toBe(200);
    expect(reconsent.data.status).toBe("COMPLETED");

    api.store.reset();
    const resetSoa = await api.requestJson(
      "/api/v1/studies/DEMO-01/versions/v2.0/soa-projection"
    );
    expect(resetSoa.data.arms.length).toBe(2);
    expect(resetSoa.data.arms.some((a) => a.arm_id === "ARM-C")).toBe(false);
    const resetRules = await api.requestJson("/api/v1/studies/DEMO-01/rules");
    expect(resetRules.data.length).toBe(1);
  });
});
