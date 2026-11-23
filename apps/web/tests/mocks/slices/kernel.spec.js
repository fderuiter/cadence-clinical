import { createPinia, setActivePinia } from "pinia";
import { describe, expect, it, vi } from "vitest";
import { useAuthStore } from "../../../src/stores/auth.js";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: kernel", () => {
  const api = useDemoSliceServer();

  it("persists kernel observation corrections and enforces expected_version conflict checks", async () => {
    const obsRef = "observation:00000000-0000-0000-0000-000000000001"; // deid-ignore
    const initial = await api.requestJson(
      `/api/v1/kernel/observations/${encodeURIComponent(obsRef)}`
    );
    expect(initial.status).toBe(200);
    expect(initial.data.version).toBe(1);

    const corrected = await api.requestJson(
      `/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/corrections`,
      {
        method: "POST",
        headers: {
          "Idempotency-Key": "idem-obs-correct-1",
          "X-Change-Reason": "Transcription correction against source",
        },
        body: JSON.stringify({
          expected_version: 1,
          value: {
            type: "quantity",
            value: "124",
            unit: "mm[Hg]",
            comparator: null,
          },
        }),
      }
    );
    expect(corrected.status).toBe(200);

    const stale = await api.requestJson(
      `/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/corrections`,
      {
        method: "POST",
        headers: {
          "Idempotency-Key": "idem-obs-stale",
          "X-Change-Reason": "Stale version check",
        },
        body: JSON.stringify({
          expected_version: 1,
          value: {
            type: "quantity",
            value: "120",
            unit: "mm[Hg]",
            comparator: null,
          },
        }),
      }
    );
    expect(stale.status).toBe(409);

    const reread = await api.requestJson(
      `/api/v1/kernel/observations/${encodeURIComponent(obsRef)}`
    );
    expect(reread.data.version).toBe(2);
    expect(reread.data.value.value).toBe("124");
    expect(reread.data.revisions.length).toBe(2);
    expect(reread.data.revisions[1].number).toBe(2);
  });

  it("supports kernel SDV verifications, retractions, node children, and zoom lineage", async () => {
    const obsRef = "observation:00000000-0000-0000-0000-000000000001"; // deid-ignore
    const enrollmentRef = "enrollment:00000000-0000-0000-0000-000000001001"; // deid-ignore

    const verified = await api.requestJson(
      `/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/verifications`,
      {
        method: "POST",
        headers: {
          "Idempotency-Key": "idem-sdv-1",
          "X-Change-Reason": "Verified against EHR source",
        },
        body: JSON.stringify({ enrollment_ref: enrollmentRef, revision: 1 }),
      }
    );
    expect(verified.status).toBe(201);
    expect(verified.data.kind).toBe("Verified");
    expect(verified.data.ref).toMatch(/^assertion:/);

    const retracted = await api.requestJson(
      `/api/v1/kernel/assertions/${encodeURIComponent(verified.data.ref)}/retraction`,
      {
        method: "POST",
        headers: {
          "Idempotency-Key": "idem-retract-1",
          "X-Change-Reason": "Withdrawn after chart update",
        },
        body: JSON.stringify({ enrollment_ref: enrollmentRef }),
      }
    );
    expect(retracted.status).toBe(200);
    expect(retracted.data.retracts).toBe(verified.data.ref);

    const studyRef = "study:00000000-0000-0000-0000-000000000001"; // deid-ignore
    const studyChildren = await api.requestJson(
      `/api/v1/kernel/nodes/${encodeURIComponent(studyRef)}/children`
    );
    expect(studyChildren.status).toBe(200);
    const site101 = studyChildren.data.find(
      (c) => c.node.kind === "study_site" && c.node.code === "SITE-101"
    );
    expect(site101).toBeTruthy();

    const siteChildren = await api.requestJson(
      `/api/v1/kernel/nodes/${encodeURIComponent(site101.node.ref)}/children`
    );
    expect(siteChildren.status).toBe(200);
    const subj1001 = siteChildren.data.find(
      (c) => c.node.kind === "enrollment" && c.node.code === "SUBJ-1001"
    );
    expect(subj1001?.node.ref).toBe(enrollmentRef);

    const zoomPath = await api.requestJson(
      `/api/v1/kernel/nodes/${encodeURIComponent(subj1001.node.ref)}/path`
    );
    expect(zoomPath.status).toBe(200);
    expect(zoomPath.data.find((s) => s.level === "study_site")?.label).toBe(
      "SITE-101"
    );
    expect(zoomPath.data.find((s) => s.level === "enrollment")?.label).toBe(
      "SUBJ-1001"
    );
  });

  it("serves stateful kernel entitlements, tier upgrades, rule-pack governance, and validation findings", async () => {
    const initial = await api.requestJson("/api/v1/kernel/entitlements");
    expect(initial.status).toBe(200);
    expect(initial.data.tier).toBe("Complete");

    const upgraded = await api.requestJson("/api/v1/kernel/entitlements/tier", {
      method: "POST",
      headers: {
        "Idempotency-Key": "demo-tier-select-1",
        "X-Change-Reason": "Evaluate Select tier in demo",
      },
      body: JSON.stringify({ tier: "Select" }),
    });
    expect(upgraded.status).toBe(200);
    expect(upgraded.data.tier).toBe("Select");
    expect(upgraded.data.product_families).toEqual([
      "Blueprint",
      "Capture",
      "Orbit",
      "Engage",
      "Allocate",
    ]);

    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const authStore = useAuthStore();
    authStore.startDemoSession();
    const loaded = await authStore.loadEntitlements({ force: true });
    expect(loaded?.tier).toBe("Select");
    vi.unstubAllEnvs();

    const studyRef = "study:00000000-0000-0000-0000-000000000001"; // deid-ignore
    const packsRes = await api.requestJson(
      `/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/rule-packs`
    );
    expect(packsRes.status).toBe(200);
    const draftPack = packsRes.data.rule_packs.find(
      (p) => p.status === "Draft"
    );
    expect(draftPack).toBeTruthy();

    const previewRes = await api.requestJson(
      `/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/rule-packs/${encodeURIComponent(draftPack.ref)}/preview`,
      { method: "POST" }
    );
    expect(previewRes.status).toBe(200);
    expect(previewRes.data.item_ref).toBe(draftPack.ref);

    const approveRes = await api.requestJson(
      `/api/v1/kernel/library-items/${encodeURIComponent(draftPack.ref)}/approve`,
      {
        method: "POST",
        headers: {
          "Idempotency-Key": "idem-approve-pack-1",
          "X-Change-Reason": "Approve cardiometabolic rule pack",
          "X-Sig-Token": "sig-demo-rule-pack",
        },
        body: JSON.stringify({ meaning: "Approved" }),
      }
    );
    expect(approveRes.status).toBe(200);
    expect(approveRes.data.status).toBe("Approved");

    const standardsRes = await api.requestJson(
      `/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/standards`,
      {
        method: "PUT",
        headers: {
          "Idempotency-Key": "idem-declare-pack-1",
          "X-Change-Reason": "Pin cardiometabolic rule pack",
        },
        body: JSON.stringify({
          rule_pack_refs: [
            ...packsRes.data.pinned_rule_pack_refs,
            draftPack.ref,
          ],
        }),
      }
    );
    expect(standardsRes.status).toBe(200);
    expect(standardsRes.data.pinned_rule_pack_refs).toContain(draftPack.ref);

    const findingsRes = await api.requestJson(
      `/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/validation/findings`
    );
    expect(findingsRes.status).toBe(200);
    expect(findingsRes.data.findings.length).toBeGreaterThanOrEqual(1);

    const singleFinding = await api.requestJson(
      `/api/v1/kernel/validation/findings/${encodeURIComponent(findingsRes.data.findings[0].ref)}`
    );
    expect(singleFinding.status).toBe(200);
    expect(singleFinding.data.finding.ref).toBe(
      findingsRes.data.findings[0].ref
    );

    api.store.reset();
    const resetEntitlements = await api.requestJson(
      "/api/v1/kernel/entitlements"
    );
    expect(resetEntitlements.data.tier).toBe("Complete");
  });
});
