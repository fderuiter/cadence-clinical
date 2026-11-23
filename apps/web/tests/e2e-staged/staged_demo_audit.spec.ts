import path from "path";
import { expect, test, type Page } from "@playwright/test";
import { STAGES } from "../../src/canvas/lifecycle";
import { DEMO_AVAILABLE_SCREENS } from "../../src/router/availability.js";

const STAGED_ORIGIN = "http://127.0.0.1:4180";
const STAFF_DEMO_BASE = `${STAGED_ORIGIN}/cadence-clinical/demo/`;
const PORTAL_DEMO_BASE = `${STAGED_ORIGIN}/cadence-clinical/subject-portal/`;
const DEFAULT_OBS_REF = `observation:${"0".repeat(8)}-0000-0000-0000-000000000001`;
const SCREENSHOT_DIR = process.env.DEMO_AUDIT_SCREENSHOT_DIR || "";

const SCREEN_HASH_PATHS: Record<string, { path: string; persona?: string }> = {
  study: { path: "/studies/CADENCE-101", persona: "super_admin" },
  ecrf: { path: "/ecrf", persona: "super_admin" },
  observation: {
    path: `/observations/${DEFAULT_OBS_REF}`,
    persona: "super_admin",
  },
  ctms: { path: "/ctms", persona: "super_admin" },
  ssu: { path: "/ssu", persona: "super_admin" },
  etmf: { path: "/etmf", persona: "super_admin" },
  training: { path: "/training", persona: "super_admin" },
  mdr: { path: "/mdr", persona: "super_admin" },
  digitization: { path: "/digitization", persona: "super_admin" },
  "icf-builder": { path: "/icf-builder", persona: "super_admin" },
  "econsent-authoring": { path: "/econsent-authoring", persona: "super_admin" },
  "amendment-diff": { path: "/amendment-diff", persona: "super_admin" },
  imaging: { path: "/imaging", persona: "super_admin" },
  devices: { path: "/devices", persona: "super_admin" },
  "device-investigation": {
    path: "/device-investigation",
    persona: "super_admin",
  },
  adjudication: { path: "/adjudication", persona: "super_admin" },
  cec: { path: "/cec", persona: "super_admin" },
  audit: { path: "/audit", persona: "super_admin" },
  auditor: { path: "/auditor", persona: "super_admin" },
  rules: { path: "/rules", persona: "super_admin" },
  coding: { path: "/coding", persona: "super_admin" },
  "data-lock": { path: "/data-lock", persona: "super_admin" },
  lakehouse: { path: "/lakehouse", persona: "super_admin" },
  sensors: { path: "/sensors", persona: "super_admin" },
  labs: { path: "/labs", persona: "super_admin" },
  dct: { path: "/dct", persona: "super_admin" },
  safety: { path: "/safety", persona: "super_admin" },
  exports: { path: "/exports", persona: "super_admin" },
  quality: { path: "/quality", persona: "super_admin" },
  notifications: { path: "/notifications", persona: "super_admin" },
  tickets: { path: "/tickets", persona: "super_admin" },
  documents: { path: "/documents", persona: "super_admin" },
  knowledge: { path: "/knowledge", persona: "super_admin" },
  "participant-wallet": {
    path: "/participant-wallet",
    persona: "super_admin",
  },
  "lay-summaries": { path: "/lay-summaries", persona: "super_admin" },
};

function attachNetworkAudit(page: Page) {
  const status501: string[] = [];
  const status4xx5xx: string[] = [];
  const offOrigin: string[] = [];
  const failedRequests: string[] = [];
  let allowFailClosedProbe = false;

  page.on("request", (req) => {
    const url = req.url();
    if (!url.startsWith("http://") && !url.startsWith("https://")) return;
    const origin = new URL(url).origin;
    if (origin !== STAGED_ORIGIN) {
      offOrigin.push(`${req.method()} ${url}`);
    }
  });

  page.on("response", (res) => {
    const url = res.url();
    if (!url.startsWith("http://") && !url.startsWith("https://")) return;
    if (allowFailClosedProbe && url.includes("__unmocked_audit_probe__")) {
      return;
    }
    const status = res.status();
    if (status === 501) {
      status501.push(`${status} ${res.request().method()} ${url}`);
    }
    if (status >= 400) {
      status4xx5xx.push(`${status} ${res.request().method()} ${url}`);
    }
  });

  page.on("requestfailed", (req) => {
    const errText = req.failure()?.errorText || "UNKNOWN";
    if (errText.includes("ERR_ABORTED")) return;
    failedRequests.push(`${errText} ${req.method()} ${req.url()}`);
  });

  return {
    status501,
    status4xx5xx,
    offOrigin,
    failedRequests,
    setAllowFailClosedProbe(enabled: boolean) {
      allowFailClosedProbe = enabled;
    },
    assertClean(contextLabel: string) {
      expect(status501, `Unexpected 501 on ${contextLabel}`).toEqual([]);
      expect(status4xx5xx, `Unexpected >=400 on ${contextLabel}`).toEqual([]);
      expect(offOrigin, `Unexpected off-origin on ${contextLabel}`).toEqual([]);
    },
  };
}

async function dismissOnboardingIfPresent(page: Page) {
  await page.evaluate(() => {
    try {
      for (const persona of [
        "super_admin",
        "sponsor_designer",
        "data_manager",
        "site_crc",
        "site_investigator",
        "cra_monitor",
        "safety_officer",
        "auditor",
      ]) {
        localStorage.setItem(`onboarding_${persona}_tour_completed`, "true");
        localStorage.setItem(`onboarding_${persona}_bypass_overview`, "true");
      }
      localStorage.setItem("onboarding_tour_completed", "true");
      localStorage.setItem("onboarding_bypass_overview", "true");
    } catch {
      // Ignore storage access errors
    }
  });
}

async function maybeCaptureScreenshot(page: Page, filename: string) {
  if (!SCREENSHOT_DIR) return;
  await page.screenshot({
    path: path.join(SCREENSHOT_DIR, filename),
    fullPage: false,
  });
}

async function ensureStaffDemoReady(page: Page) {
  await page.goto(STAFF_DEMO_BASE, { waitUntil: "domcontentloaded" });
  await dismissOnboardingIfPresent(page);
  await page.waitForFunction(
    () => Boolean(navigator.serviceWorker?.controller),
    null,
    { timeout: 20000 }
  );
  const personaSelect = page.locator("#persona-select");
  if ((await personaSelect.count()) > 0) {
    await personaSelect.selectOption("super_admin");
  }
}

test.describe("Staged Co-Hosted Demo Portals Browser Audit", () => {
  test("visits all 35 DEMO_AVAILABLE_SCREENS and all 15 Study Canvas sections with zero 501s, >=400s, off-origin calls, or error banners", async ({
    page,
  }) => {
    const audit = attachNetworkAudit(page);
    await ensureStaffDemoReady(page);

    const screenNames = [...DEMO_AVAILABLE_SCREENS];
    expect(screenNames.length).toBe(35);
    for (const screenName of screenNames) {
      expect(
        SCREEN_HASH_PATHS[screenName],
        `Missing route mapping for DEMO_AVAILABLE_SCREENS entry '${screenName}'`
      ).toBeDefined();
    }

    for (const screenName of screenNames) {
      const target = SCREEN_HASH_PATHS[screenName];
      await page.goto(`${STAFF_DEMO_BASE}#${target.path}`);
      await page.waitForTimeout(250);

      const currentHash = await page.evaluate(() => window.location.hash);
      expect(
        currentHash,
        `Route '${screenName}' unexpectedly redirected to unavailable/forbidden`
      ).not.toMatch(/^#\/(unavailable|forbidden)(\b|\/|\?)/);

      const bodyText = await page.locator("body").innerText();
      expect(bodyText).not.toContain("DEMO_UNMOCKED_ENDPOINT");
      expect(bodyText).not.toContain("DEMO_OFF_ORIGIN_BLOCKED");
      audit.assertClean(`screen:${screenName}`);
    }

    const canvasSections = STAGES.flatMap((stage) =>
      stage.sections.map((section) => ({
        stageId: stage.id,
        sectionId: section.id,
      }))
    );
    expect(canvasSections.length).toBe(15);

    for (const { stageId, sectionId } of canvasSections) {
      await page.goto(
        `${STAFF_DEMO_BASE}#/studies/CADENCE-101?stage=${stageId}&section=${sectionId}`
      );
      await expect(page.locator("#study-canvas")).toBeVisible();
      await page.waitForTimeout(250);

      const bodyText = await page.locator("body").innerText();
      expect(bodyText).not.toContain("DEMO_UNMOCKED_ENDPOINT");
      audit.assertClean(`canvas:${stageId}/${sectionId}`);
    }
  });

  test("exercises Study Canvas validation workflows and non-mutating eCRF pre-commit form preview", async ({
    page,
  }) => {
    const audit = attachNetworkAudit(page);
    await ensureStaffDemoReady(page);

    await page.goto(
      `${STAFF_DEMO_BASE}#/studies/CADENCE-101?stage=data&section=validation`
    );
    await expect(page.locator("#study-rule-packs")).toBeVisible();
    await expect(
      page.locator('[data-testid="rule-pack-vs-vital-ranges-v1"]')
    ).toBeVisible();
    await expect(
      page.locator('[data-testid="rule-pack-sdtm-conformance-core-v1"]')
    ).toBeVisible();

    // 1. Preview draft rule pack
    const previewRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/rule-packs/") &&
        res.url().endsWith("/preview") &&
        res.request().method() === "POST"
    );
    await page.locator("#btn-preview-pack-sdtm-conformance-core-1").click();
    expect((await previewRespPromise).status()).toBe(200);
    await expect(
      page.locator('[data-testid="pack-preview-sdtm-conformance-core"]')
    ).toBeVisible();

    // 2. Part 11 approve draft rule pack
    await page.locator("#btn-approve-pack-sdtm-conformance-core-1").click();
    const sigModal = page.locator("#signature-capture-modal");
    await expect(sigModal).toBeVisible();
    const pwdInput = sigModal.locator('input[type="password"]');
    if ((await pwdInput.count()) > 0) {
      await pwdInput.fill("demo-stepup-credential"); // pragma: allowlist secret
    }
    await sigModal.locator("#sig-reason").selectOption({ index: 1 });
    const approveRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/kernel/library-items/") &&
        res.url().endsWith("/approve") &&
        res.request().method() === "POST"
    );
    await sigModal.locator("#btn-confirm-sig").click();
    expect((await approveRespPromise).status()).toBe(200);

    // 3. Pin newly approved rule pack
    await page.locator("#btn-pin-pack-sdtm-conformance-core-1").click();
    await page
      .locator("#validation-pin-reason-input")
      .fill("Pin SDTM conformance checks for CADENCE-101");
    const pinRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/kernel/studies/") &&
        res.url().endsWith("/standards") &&
        res.request().method() === "PUT"
    );
    await page.locator("#btn-confirm-pin-sdtm-conformance-core").click();
    expect((await pinRespPromise).status()).toBe(200);
    await expect(
      page.locator('[data-testid="pinned-rule-packs-count"]')
    ).toContainText("2 pinned");

    // 4. Inspect validation finding explanation
    const explainBtn = page
      .locator('[data-testid="validation-findings-list"] button')
      .filter({ hasText: /Explain|Why/i })
      .first();
    if ((await explainBtn.count()) > 0) {
      await explainBtn.click();
      await expect(
        page.locator('[data-testid="finding-explanation"]')
      ).toBeVisible();
    }
    await maybeCaptureScreenshot(page, "01-study-canvas-validation.png");

    // 5. Open eCRF form via finding deep link or /ecrf and trigger pre-commit preview without mutating saved value
    const openFormLink = page
      .locator('[data-testid="validation-findings-list"] a')
      .first();
    if ((await openFormLink.count()) > 0) {
      await openFormLink.click();
    } else {
      await page.goto(`${STAFF_DEMO_BASE}#/ecrf`);
    }

    await expect(page.locator('[data-testid="ecrf-form-items"]')).toBeVisible();
    const sysbpInput = page.locator("#ecrf-item-item-sysbp");
    await expect(sysbpInput).toBeVisible();
    const initialSavedValue = await sysbpInput.inputValue();

    const formPreviewPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/execution/forms/") &&
        res.url().endsWith("/preview") &&
        res.request().method() === "POST"
    );
    await sysbpInput.fill("210");
    const formPreviewResp = await formPreviewPromise;
    expect(formPreviewResp.status()).toBe(200);

    await expect(
      page.locator('[data-testid="ecrf-preview-warnings-item-sysbp"]')
    ).toBeVisible();
    await expect(page.locator("#ecrf-preview-banner")).toBeVisible();
    await maybeCaptureScreenshot(page, "02-ecrf-inline-preview-warning.png");

    // Verify saved form state on the MSW store was NOT mutated by pre-commit preview
    const formResourceUrl = formPreviewResp.url().replace(/\/preview$/, "");
    const persistedSysbp = await page.evaluate(async (url) => {
      const res = await fetch(url);
      const body = await res.json();
      const item = (body.items || []).find(
        (entry: { item_id: string; value?: string | { value?: string } }) =>
          entry.item_id === "item-sysbp"
      );
      const raw = item?.value;
      return typeof raw === "object" && raw !== null
        ? (raw.value ?? null)
        : (raw ?? null);
    }, formResourceUrl);
    expect(persistedSysbp).toBe(initialSavedValue);
    expect(persistedSysbp).not.toBe("210");

    audit.assertClean("validation-and-ecrf-preview");
  });

  test("completes stateful Lay Summary, Quality RBQM RACT/Dossier, and Aggregate Safety DSUR/QPPV/Triage actions", async ({
    page,
  }) => {
    const audit = attachNetworkAudit(page);
    await ensureStaffDemoReady(page);

    // 1. Lay Summary receipt confirmation & publishing
    await page.goto(`${STAFF_DEMO_BASE}#/lay-summaries`);
    await expect(page.locator("#section-lay-summaries")).toBeVisible();
    await page
      .getByRole("button", { name: /Acknowledge & Confirm Receipt/i })
      .click();
    await page.locator("#chk-ack").check();
    const receiptRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/payments/lay-summaries/") &&
        res.url().endsWith("/confirm-receipt") &&
        res.request().method() === "POST"
    );
    await page
      .getByRole("button", {
        name: /Generate Cryptographically Sealed Confirmation Receipt/i,
      })
      .click();
    expect([200, 201]).toContain((await receiptRespPromise).status());
    await expect(page.locator(".certificate-card")).toBeVisible();

    await page
      .getByRole("button", { name: /Sponsor & IRB Publishing Studio/i })
      .click();
    const publishInputs = page.locator(".tab-content .form-input");
    await publishInputs.nth(4).fill("CADENCE-101 Long-Term Extension Results");
    const publishRespPromise = page.waitForResponse(
      (res) =>
        res.url().endsWith("/api/v1/payments/lay-summaries") &&
        res.request().method() === "POST"
    );
    await page
      .getByRole("button", {
        name: /Publish IRB-Approved Lay Summary Live to Participants/i,
      })
      .click();
    expect([200, 201]).toContain((await publishRespPromise).status());

    // 2. Quality RBQM RACT seed defaults, SRI compute, and CSM dossier compile
    await page.goto(`${STAFF_DEMO_BASE}#/quality`);
    await page
      .getByRole("button", { name: /TransCelerate RACT Matrix/i })
      .click();
    await expect(
      page.getByRole("button", { name: /Seed Defaults/i })
    ).toBeVisible();
    const seedRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/quality/rbqm/ract/seed-default") &&
        res.request().method() === "POST"
    );
    await page.getByRole("button", { name: /Seed Defaults/i }).click();
    expect([200, 201]).toContain((await seedRespPromise).status());

    const sriRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/quality/rbqm/site-risk-profiles/compute") &&
        res.request().method() === "POST"
    );
    await page.getByRole("button", { name: /Compute SRI/i }).click();
    expect([200, 201]).toContain((await sriRespPromise).status());

    await page
      .getByRole("button", { name: /Inspection Dossier/i })
      .last()
      .click();
    const dossierSignInputs = page.locator(
      ".dossier-sign-form input.form-control"
    );
    await expect(dossierSignInputs.first()).toBeVisible();
    if ((await dossierSignInputs.nth(0).inputValue()) === "") {
      await dossierSignInputs.nth(0).fill("Dr. Clara Vance");
    }
    if ((await dossierSignInputs.nth(1).inputValue()) === "") {
      await dossierSignInputs.nth(1).fill("Head of Quality Assurance");
    }
    const dossierRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/quality/rbqm/csm/dossiers/compile") &&
        res.request().method() === "POST"
    );
    await page
      .getByRole("button", {
        name: /Compile & Cryptographically Sign Dossier/i,
      })
      .click();
    expect([200, 201]).toContain((await dossierRespPromise).status());
    await expect(page.locator(".dossier-seal-banner")).toBeVisible();
    await maybeCaptureScreenshot(page, "03-quality-ract-dossier.png");

    // 3. Aggregate Safety exposure, DSUR compile, QPPV sign-off, and rule triage
    await page.goto(`${STAFF_DEMO_BASE}#/safety`);
    await expect(
      page.getByRole("button", { name: /Compute Exposure/i })
    ).toBeVisible();
    const exposureRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/safety/exposure/calculate") &&
        res.request().method() === "POST"
    );
    await page.getByRole("button", { name: /Compute Exposure/i }).click();
    expect([200, 201]).toContain((await exposureRespPromise).status());

    const compileRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/safety/periodic-reports/compile") &&
        res.request().method() === "POST"
    );
    await page
      .getByRole("button", { name: /Compile Regulatory Report/i })
      .click();
    expect([200, 201]).toContain((await compileRespPromise).status());
    await expect(page.locator(".snapshot-banner")).toBeVisible();

    await page
      .getByRole("button", { name: /QPPV Sign-Off/i })
      .first()
      .click();
    await page.locator("#qppv-secret").fill("demo-qppv-stepup"); // pragma: allowlist secret
    const qppvRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/safety/periodic-reports/") &&
        res.url().endsWith("/qppv-sign") &&
        res.request().method() === "POST"
    );
    await page
      .getByRole("button", { name: /Confirm Electronic Signature/i })
      .click();
    expect([200, 201]).toContain((await qppvRespPromise).status());

    await page.getByRole("tab", { name: /Expedited Clocks & Triage/i }).click();
    const triageRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/safety/rules/evaluate") &&
        res.request().method() === "POST"
    );
    await page
      .getByRole("button", { name: /Evaluate Regulatory Clocks/i })
      .click();
    expect([200, 201]).toContain((await triageRespPromise).status());
    await expect(page.locator(".sim-results-card")).toBeVisible();
    await maybeCaptureScreenshot(page, "04-safety-dsur-qppv.png");

    audit.assertClean("lay-summaries-quality-safety");
  });

  test("hydrates Subject Portal demo wallet ($125.00), completes idempotent payout, and reconciles inst_daily_diary to SYNCED with +$25.00 stipend", async ({
    page,
  }) => {
    const audit = attachNetworkAudit(page);

    await page.goto(`${PORTAL_DEMO_BASE}#/wallet`, {
      waitUntil: "domcontentloaded",
    });
    await page.waitForFunction(
      () => Boolean(navigator.serviceWorker?.controller),
      null,
      { timeout: 20000 }
    );

    const balanceAmount = page.locator("#wallet-balance-amount");
    await expect(balanceAmount).toHaveText("$125.00");
    const disburseBtn = page.locator("#btn-disburse-balance");
    await expect(disburseBtn).toBeEnabled();
    await maybeCaptureScreenshot(page, "05-subject-portal-wallet-hydrated.png");

    // Submit idempotent payout in modal ($25.00 -> $100.00 remaining)
    await disburseBtn.click();
    await expect(page.locator("#wallet-disburse-modal")).toBeVisible();
    await page.locator("#disburse-amount-input").fill("25.00");
    const disburseRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/payments/participant/me/disburse") &&
        res.request().method() === "POST"
    );
    await page.locator("#btn-disburse-confirm").click();
    expect((await disburseRespPromise).status()).toBe(200);
    await expect(
      page.locator("#wallet-disbursement-confirmation")
    ).toBeVisible();
    await expect(balanceAmount).toHaveText("$100.00");

    // Navigate to Tasks, complete & sign inst_daily_diary (assign_01), and verify SYNCED + $25.00 stipend credit
    await page.goto(`${PORTAL_DEMO_BASE}#/tasks`);
    const assign01Start = page.locator("#task-card-assign_01 .btn-start-task");
    await expect(assign01Start).toBeVisible();
    await assign01Start.click();

    await expect(page.locator("#vssbp")).toBeVisible();
    await page.locator("#vssbp").fill("120");
    await page.locator("#vsdpb").fill("78");
    await page.locator("#vshr").fill("68");
    const painSlider = page.locator("#pain_scale");
    await painSlider.evaluate((el: HTMLInputElement) => {
      el.value = "2";
      el.setAttribute("data-touched", "true");
      el.dispatchEvent(new Event("input", { bubbles: true }));
      el.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const noSymptomsRadio = page.locator(
      'input[name="has_symptoms"][value="No"]'
    );
    if ((await noSymptomsRadio.count()) > 0) {
      await noSymptomsRadio.check({ force: true });
    }

    await page.locator("#btn-submit-questionnaire").click();
    await expect(page.locator("#portal-sign-modal")).toBeVisible();
    await page.locator("#sign-password").fill("1234"); // pragma: allowlist secret

    const syncRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/interop/epro/sync") &&
        res.request().method() === "POST"
    );
    const stipendRespPromise = page.waitForResponse(
      (res) =>
        res.url().includes("/api/v1/payments/participant/me/diary-stipend") &&
        res.request().method() === "POST"
    );
    await page.locator("#btn-modal-sign").click();

    const syncResp = await syncRespPromise;
    expect(syncResp.status()).toBe(200);
    const syncJson = await syncResp.json();
    expect(syncJson.results?.[0]?.status).toBe("SYNCED");

    const stipendResp = await stipendRespPromise;
    expect(stipendResp.status()).toBe(200);
    const stipendJson = await stipendResp.json();
    expect(stipendJson.creditedAmount).toBe(25);

    await expect(page.locator("#task-card-assign_01")).toHaveCount(0);

    await page.goto(`${PORTAL_DEMO_BASE}#/wallet`);
    await expect(balanceAmount).toHaveText("$125.00");
    await maybeCaptureScreenshot(page, "06-subject-portal-synced-stipend.png");

    audit.assertClean("subject-portal-wallet-and-diary-sync");
  });

  test("reloads Subject Portal offline from portal-cache-v1 with zero net::ERR_FAILED errors and preserves fail-closed 501 boundary", async ({
    context,
    page,
  }) => {
    const audit = attachNetworkAudit(page);

    await page.goto(PORTAL_DEMO_BASE, { waitUntil: "networkidle" });
    await page.waitForFunction(
      () => Boolean(navigator.serviceWorker?.controller),
      null,
      { timeout: 20000 }
    );

    // Confirm unified SW (/cadence-clinical/subject-portal/sw.js?msw=1) pre-cached index.html and hashed JS/CSS chunks
    const cacheCheck = await page.evaluate(async () => {
      const swUrl = navigator.serviceWorker.controller?.scriptURL || "";
      const cacheKeys = await caches.keys();
      const cache = await caches.open("portal-cache-v1");
      const requests = await cache.keys();
      const urls = requests.map((r) => new URL(r.url).pathname);
      return {
        swUrl,
        cacheKeys,
        hasIndex: urls.some((u) => u.endsWith("/subject-portal/index.html")),
        jsChunks: urls.filter((u) => u.endsWith(".js")).length,
        cssChunks: urls.filter((u) => u.endsWith(".css")).length,
      };
    });

    expect(cacheCheck.swUrl).toContain(
      "/cadence-clinical/subject-portal/sw.js?msw=1"
    );
    expect(cacheCheck.cacheKeys).toContain("portal-cache-v1");
    expect(cacheCheck.hasIndex).toBe(true);
    expect(cacheCheck.jsChunks).toBeGreaterThan(0);
    expect(cacheCheck.cssChunks).toBeGreaterThan(0);

    // Go offline and reload
    audit.failedRequests.length = 0;
    await context.setOffline(true);
    try {
      await page.reload({ waitUntil: "domcontentloaded" });
      await expect(page.locator("#app")).toBeVisible();
      await expect(page.locator("#tasks-list, #view-tasks")).toBeVisible();
      const staticFailures = audit.failedRequests.filter(
        (entry) => !entry.includes("/api/")
      );
      expect(staticFailures).toEqual([]);
      await maybeCaptureScreenshot(
        page,
        "07-subject-portal-offline-reload.png"
      );
    } finally {
      await context.setOffline(false);
    }

    // Verify fail-closed 501 boundary remains enforced
    audit.setAllowFailClosedProbe(true);
    const probeStatus = await page.evaluate(async () => {
      const res = await fetch("/api/v1/__unmocked_audit_probe__");
      const body = await res.json();
      return { status: res.status, error: body.error };
    });
    expect(probeStatus).toEqual({
      status: 501,
      error: "DEMO_UNMOCKED_ENDPOINT",
    });
  });
});
