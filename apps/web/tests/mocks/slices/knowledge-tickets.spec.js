import { describe, expect, it } from "vitest";
import { useDemoSliceServer } from "./sliceTestHarness.js";

describe("MSW domain slice: knowledge-tickets", () => {
  const api = useDemoSliceServer();

  it("resolves contextual help, searches SOP articles, enforces GxP ticket guards, and resets tickets", async () => {
    const help = await api.requestJson(
      "/api/v1/knowledge/contextual-help?route=/ecrf&persona=super_admin"
    );
    expect(help.status).toBe(200);
    expect(help.data.primary_article).toBeTruthy();

    const articles = await api.requestJson("/api/v1/knowledge/articles");
    expect(articles.status).toBe(200);
    expect(articles.data.length).toBeGreaterThanOrEqual(1);

    const missingReason = await api.requestJson("/api/v1/tickets", {
      method: "POST",
      body: JSON.stringify({
        title: "Missing reason ticket",
        description: "Attempt without X-Change-Reason header.",
      }),
    });
    expect(missingReason.status).toBe(400);

    const created = await api.requestJson("/api/v1/tickets", {
      method: "POST",
      headers: { "X-Change-Reason": "Escalate visit window discrepancy" },
      body: JSON.stringify({
        title: "Week 4 Lab Window Clarification",
        description: "Verify acceptable +/- 2 day window for SUBJ-1001.",
        category: "PROTOCOL_DEVIATION",
        priority: "HIGH",
        gxp_severity: "MINOR",
        study_id: "DEMO-01",
        site_id: "SITE-101",
      }),
    });
    expect(created.status).toBe(201);
    expect(created.data.status).toBe("OPEN");

    const listed = await api.requestJson(
      "/api/v1/tickets?study_id=DEMO-01&status=OPEN"
    );
    expect(listed.status).toBe(200);
    expect(listed.data.some((t) => t.id === created.data.id)).toBe(true);

    api.store.reset();
    const afterReset = await api.requestJson(
      "/api/v1/tickets?study_id=DEMO-01"
    );
    expect(afterReset.data).toEqual([]);
  });
});
