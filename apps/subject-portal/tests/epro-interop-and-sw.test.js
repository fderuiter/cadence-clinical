import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import fs from "fs";
import path from "path";
import { state, redeemActivationCode } from "../src/index.js";
import { signIn } from "../src/features/auth.js";
import { AVAILABLE_SCREENS } from "../src/router/availability.js";

describe("Subject Portal OIDC subject_code, Activation Redemption, and Built SW Manifest", () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    state.session.userId = null;
    state.session.token = null;
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.assignments = [];
    state.instruments = {};
    state.notifications = [];
    state.compliance = null;
  });

  afterEach(() => {
    delete window.Keycloak;
    delete window.keycloakInstance;
    window.__MOCK_TEST_ENV__ = true;
    vi.restoreAllMocks();
  });

  it("reads subject_code claim from Keycloak token so SUBJ-101-001 becomes active session userId", async () => {
    window.__MOCK_TEST_ENV__ = false;
    window.Keycloak = class MockKeycloak {
      constructor() {
        this.authenticated = true;
        this.token = "eyJhbGciOiJSUzI1NiJ9.mock";
        this.subject = "7b8f9c12-1111-4444-8888-abcdef012345";
        this.tokenParsed = {
          preferred_username: "subj.101.001",
          subject_code: "SUBJ-101-001",
        };
      }
      async init() {
        return true;
      }
    };

    await signIn();

    expect(state.session.userId).toBe("SUBJ-101-001");
    expect(state.session.token).toBe("eyJhbGciOiJSUzI1NiJ9.mock");
    expect(localStorage.getItem("oidc_user_id")).toBe("SUBJ-101-001");
  });

  it("redeems a 30-day activation code with X-Change-Reason and reloads participant assignments", async () => {
    state.session.token = "jwt-subject-token";
    state.session.userId = "unlinked-oidc-sub";

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(async (url, opts) => {
        const href = String(url);
        if (
          href.endsWith("/api/v1/execution/subjects/activation-codes/redeem")
        ) {
          expect(opts?.method).toBe("POST");
          expect(opts?.headers?.["X-Change-Reason"]).toBe(
            "BYOD device activation at clinic"
          );
          return new Response(
            JSON.stringify({
              subject_id: "SUBJ-101-001",
              study_id: "CADENCE-101",
              site_id: "SITE-101",
              redeemed_at: "2026-11-20T12:05:00Z",
            }),
            { status: 200, headers: { "Content-Type": "application/json" } }
          );
        }
        if (href.endsWith("/api/v1/interop/assignments/subject/SUBJ-101-001")) {
          return new Response(
            JSON.stringify([
              {
                id: "assign_subj_101_001_daily",
                subject_id: "SUBJ-101-001",
                instrument_id: "inst_daily_diary",
                instrument_name: "Daily Health & Vital Diary",
                start_date: "2026-11-19T00:00:00Z",
                end_date: "2026-12-20T00:00:00Z",
                status: "PENDING",
                version_index: 1,
              },
            ]),
            { status: 200, headers: { "Content-Type": "application/json" } }
          );
        }
        if (
          href.endsWith("/api/v1/interop/subjects/SUBJ-101-001/instruments")
        ) {
          return new Response(
            JSON.stringify([
              {
                id: "inst_daily_diary",
                name: "Daily Health & Vital Diary",
                items: { vssbp: { label: "Systolic BP", type: "numeric" } },
                version_index: 1,
              },
            ]),
            { status: 200, headers: { "Content-Type": "application/json" } }
          );
        }
        if (href.endsWith("/api/v1/interop/subjects/SUBJ-101-001/compliance")) {
          return new Response(
            JSON.stringify({
              subject_id: "SUBJ-101-001",
              compliance_rate: 100,
              completed_count: 0,
              pending_count: 1,
              overdue_count: 0,
              total_count: 1,
              assignments: [],
            }),
            { status: 200, headers: { "Content-Type": "application/json" } }
          );
        }
        if (
          href.endsWith("/api/v1/interop/subjects/SUBJ-101-001/notifications")
        ) {
          return new Response(JSON.stringify([]), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          });
        }
        throw new Error(`Unexpected fetch URL: ${href}`);
      });

    const redeemed = await redeemActivationCode(
      "CAD-7K9P-3M2X-8Q4W",
      "BYOD device activation at clinic"
    );

    expect(redeemed.subject_id).toBe("SUBJ-101-001");
    expect(state.session.userId).toBe("SUBJ-101-001");
    expect(localStorage.getItem("oidc_user_id")).toBe("SUBJ-101-001");
    expect(state.assignments).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });

  it("keeps wallet and lay-summaries excluded from AVAILABLE_SCREENS", () => {
    expect(AVAILABLE_SCREENS.has("wallet")).toBe(false);
    expect(AVAILABLE_SCREENS.has("view-wallet")).toBe(false);
    expect(AVAILABLE_SCREENS.has("lay-summaries")).toBe(false);
    expect(AVAILABLE_SCREENS.has("view-lay-summaries")).toBe(false);
  });

  it("injects hashed /subject-portal/assets/* bundle paths into dist/sw.js with zero /src/* 404 entries", () => {
    const distDir = path.resolve(__dirname, "../dist");
    const distSwPath = path.join(distDir, "sw.js");
    expect(fs.existsSync(distSwPath)).toBe(true);

    const distSw = fs.readFileSync(distSwPath, "utf8");
    const match = distSw.match(/const ASSETS = (\[[\s\S]*?\]);/);
    expect(match).not.toBeNull();

    const assets = JSON.parse(match[1]);
    expect(assets).toContain("/subject-portal/");
    expect(assets).toContain("/subject-portal/index.html");
    expect(assets).toContain("/subject-portal/manifest.json");
    expect(
      assets.some((entry) => entry.startsWith("/subject-portal/assets/"))
    ).toBe(true);
    expect(assets.some((entry) => entry.includes("/subject-portal/src/"))).toBe(
      false
    );

    // Every asset path in dist/sw.js must resolve to a real file in dist/ (zero 404s on install)
    for (const assetUrl of assets) {
      expect(assetUrl.startsWith("/subject-portal/")).toBe(true);
      const rel = assetUrl.slice("/subject-portal/".length) || "index.html";
      const fileOnDisk = path.join(distDir, rel);
      expect(fs.existsSync(fileOnDisk)).toBe(true);
    }
  });
});
