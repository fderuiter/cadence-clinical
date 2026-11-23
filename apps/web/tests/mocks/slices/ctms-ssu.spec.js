import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: ctms-ssu", () => {
  const api = useDemoSliceServer();

  it("persists SSU greenlight certification, site compliance activation, and blocks incomplete sites", async () => {
    const certify = await api.requestJson(
      "/api/v1/ctms/startup/sites/SITE-102/greenlight/certify?study_id=DEMO-01&country_code=US",
      {
        method: "POST",
        headers: { "X-Change-Reason": "SSU readiness sign-off" },
      }
    );
    expect(certify.status).toBe(200);

    const matrix = await api.requestJson(
      "/api/v1/ctms/startup/matrix?country_code=US"
    );
    expect(matrix.status).toBe(200);
    const site102 = matrix.data.find((s) => s.site_id === "SITE-102");
    expect(site102?.overall_status).toBe("APPROVED");
    expect(site102?.greenlight_token).toContain("SITE-102");

    const preCompliance = await api.requestJson(
      "/api/v1/execution/sites/SITE-102/compliance-status?study_id=DEMO-01"
    );
    expect(preCompliance.status).toBe(200);
    expect(preCompliance.data.is_activated).toBe(false);

    const activate = await api.requestJson(
      "/api/v1/execution/sites/SITE-102/activate",
      {
        method: "POST",
        headers: { "X-Change-Reason": "Greenlight package verified" },
        body: JSON.stringify({ study_id: "DEMO-01" }),
      }
    );
    expect(activate.status).toBe(200);

    const postCompliance = await api.requestJson(
      "/api/v1/execution/sites/SITE-102/compliance-status?study_id=DEMO-01"
    );
    expect(postCompliance.status).toBe(200);
    expect(postCompliance.data.is_activated).toBe(true);
    expect(postCompliance.data.activated_by).toBeTruthy();

    const blockedSite = await api.requestJson(
      "/api/v1/execution/sites/SITE-103/activate",
      {
        method: "POST",
        headers: { "X-Change-Reason": "Attempt blocked site activation" },
        body: JSON.stringify({ study_id: "DEMO-01" }),
      }
    );
    expect(blockedSite.status).toBe(400);

    api.store.reset();
    const resetCompliance = await api.requestJson(
      "/api/v1/execution/sites/SITE-102/compliance-status?study_id=DEMO-01"
    );
    expect(resetCompliance.data.is_activated).toBe(false);
  });
});
