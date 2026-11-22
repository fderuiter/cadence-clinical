import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const WCAG_AA_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

async function gotoHashRoute(page: Page, routePath: string) {
  await page.goto(`#${routePath}`);
}

async function expectNoWcagViolations(page: Page, contextLabel: string) {
  const results = await new AxeBuilder({ page })
    // Exclude non-interactive regulatory watermark overlays per ADR-2388
    .exclude(".watermark-overlay-container")
    .exclude(".watermark-overlay")
    .withTags(WCAG_AA_TAGS)
    .analyze();

  expect(
    results.violations,
    `WCAG 2.2 AA violations on ${contextLabel}: ${JSON.stringify(
      results.violations.map((v) => ({
        id: v.id,
        impact: v.impact,
        nodes: v.nodes.map((n) => ({
          target: n.target,
          failureSummary: n.failureSummary,
        })),
      })),
      null,
      2
    )}`
  ).toEqual([]);
}

async function getBorderContrastRatio(page: Page, selector: string) {
  return page
    .locator(selector)
    .first()
    .evaluate((el) => {
      function parseRgb(str: string): [number, number, number] {
        const m = str.match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
        return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : [255, 255, 255];
      }
      function luminance([r, g, b]: [number, number, number]) {
        const [rl, gl, bl] = [r, g, b].map((v) => {
          const s = v / 255;
          return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
        });
        return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
      }
      const style = window.getComputedStyle(el);
      const borderRgb = parseRgb(style.borderTopColor);
      const bgRgb = parseRgb(style.backgroundColor);
      const l1 = luminance(borderRgb);
      const l2 = luminance(bgRgb);
      return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    });
}

test.describe("Clinical Web Application WCAG 2.2 Level AA Contrast & Control Gates", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      const win = window as Window & {
        __cadenceCspViolations?: Array<Record<string, string>>;
      };
      win.__cadenceCspViolations = [];
      document.addEventListener("securitypolicyviolation", (event) => {
        win.__cadenceCspViolations?.push({
          effectiveDirective: event.effectiveDirective,
          violatedDirective: event.violatedDirective,
          blockedURI: event.blockedURI,
          sourceFile: event.sourceFile,
          sample: event.sample,
        });
      });
    });
  });

  test.afterEach(async ({ page }) => {
    const violations = await page
      .evaluate(() => {
        const win = window as Window & {
          __cadenceCspViolations?: Array<Record<string, string>>;
        };
        return win.__cadenceCspViolations || [];
      })
      .catch(() => []);
    expect(
      violations,
      `Unexpected browser Content-Security-Policy violations: ${JSON.stringify(violations, null, 2)}`
    ).toEqual([]);
  });

  test("LoginView passes WCAG 2.2 AA contrast in both authenticated session state and signed-out login prompt", async ({
    page,
  }) => {
    await gotoHashRoute(page, "/login");
    await expect(page.locator("#section-login")).toBeVisible();
    await expect(page.locator(".auth-success-alert")).toBeVisible();
    await expectNoWcagViolations(page, "LoginView (active demo session)");

    // Sign out to audit unauthenticated LoginView card, subtitle, and demo sign-in action
    await page.locator("#section-login .btn-logout").click();
    await expect(page.locator(".btn-login-demo")).toBeVisible();
    await expectNoWcagViolations(page, "LoginView (signed-out prompt)");
  });

  test("OverviewView, OnboardingChecklistDrawer, and OnboardingTour (including final Finish Tour step) pass WCAG 2.2 AA", async ({
    page,
  }) => {
    await gotoHashRoute(page, "/overview");
    await expect(page.locator("#overview-hub-root")).toBeVisible();
    await expect(page.locator(".btn-task-launch").first()).toBeVisible();
    await expectNoWcagViolations(page, "OverviewView");

    const taskLaunchBorderRatio = await getBorderContrastRatio(
      page,
      ".btn-task-launch"
    );
    expect(taskLaunchBorderRatio).toBeGreaterThanOrEqual(3);

    // Open OnboardingChecklistDrawer and audit pending status pills and task navigation buttons
    await page.locator("#btn-onboarding-checklist-trigger").click();
    await expect(page.locator("#onboarding-checklist-drawer")).toBeVisible();
    await expect(
      page.locator("#onboarding-checklist-drawer .status-pending").first()
    ).toBeVisible();
    await expectNoWcagViolations(page, "OnboardingChecklistDrawer");

    // Launch OnboardingTour and audit Step 1 controls + final step .btn-tour-complete
    await page.locator("#btn-drawer-start-tour").click();
    const tourPopover = page.locator(".onboarding-popover-card");
    await expect(tourPopover).toBeVisible();
    await expectNoWcagViolations(page, "OnboardingTour (Step 1)");

    for (let step = 0; step < 10; step++) {
      const finishBtn = tourPopover.locator(".btn-tour-complete");
      if (await finishBtn.isVisible().catch(() => false)) {
        break;
      }
      const nextBtn = tourPopover.locator(".btn-tour-next");
      await expect(nextBtn).toBeVisible();
      await nextBtn.click();
    }

    await expect(tourPopover.locator(".btn-tour-complete")).toBeVisible();
    await expectNoWcagViolations(
      page,
      "OnboardingTour (Final Step .btn-tour-complete)"
    );
  });

  test("CtmsView KPI cards, clinical tabs, SsuGreenlightConsole matrix & single-site views, and RTSM header pass WCAG 2.2 AA", async ({
    page,
  }) => {
    await gotoHashRoute(page, "/ctms");
    await expect(page.locator("#section-ctms")).toBeVisible();
    await expect(page.locator("#kpi-total-subjects")).toBeVisible();
    await expectNoWcagViolations(page, "CtmsView (Operations tab)");

    // Study Startup & Greenlight Console (SsuGreenlightConsole)
    await page.locator("#tab-startup").click();
    await expect(page.locator(".ssu-greenlight-console")).toBeVisible();
    await expect(page.locator(".metric-sub").first()).toBeVisible();
    await expectNoWcagViolations(
      page,
      "SsuGreenlightConsole (Portfolio Matrix)"
    );

    const singleViewBtn = page.locator("#btn-view-single");
    if (await singleViewBtn.isVisible().catch(() => false)) {
      await singleViewBtn.click();
      await expectNoWcagViolations(
        page,
        "SsuGreenlightConsole (Single-Site Readiness)"
      );
    }

    // RTSM & Dose Escalation dark header chrome
    await page.locator("#tab-rtsm").click();
    await expectNoWcagViolations(page, "CtmsView (RTSM tab)");
  });

  test("EcrfView shared clinical inputs, radio groups, and query panel pass WCAG 2.2 AA and SC 1.4.11 >= 3:1 border contrast", async ({
    page,
  }) => {
    await gotoHashRoute(page, "/ecrf");
    await expect(page.locator("main")).toBeVisible();
    await expectNoWcagViolations(page, "EcrfView");

    const clinicalControl = page.locator(
      ".clinical-input input, .clinical-input select, .clinical-input textarea"
    );
    if ((await clinicalControl.count()) > 0) {
      const inputBorderRatio = await getBorderContrastRatio(
        page,
        ".clinical-input input, .clinical-input select, .clinical-input textarea"
      );
      expect(inputBorderRatio).toBeGreaterThanOrEqual(3);
    }

    const radioGrid = page.locator(".clinical-radio-grid");
    if ((await radioGrid.count()) > 0) {
      const radioBorderRatio = await getBorderContrastRatio(
        page,
        ".clinical-radio-grid"
      );
      expect(radioBorderRatio).toBeGreaterThanOrEqual(3);
    }

    await gotoHashRoute(page, "/plans");
    await expect(page.locator(".plans-view")).toBeVisible();
    await expectNoWcagViolations(page, "PlansView");
  });

  test("Migrated ClinicalDrawer and ClinicalModal overlays on /etmf, /training, and /data-lock pass WCAG 2.2 AA, LIFO Escape, and reduced-motion", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    // 1. /etmf: DocumentManagerView ClinicalDrawer + stacked PdfPreviewModal LIFO Escape
    await page.emulateMedia({ reducedMotion: "reduce" });
    await gotoHashRoute(page, "/etmf");
    await expect(page.locator(".document-manager-layout")).toBeVisible();

    const zoneWithDocs = page
      .locator(".zone-header")
      .filter({ has: page.locator(".zone-badge") })
      .first();
    await expect(zoneWithDocs).toBeVisible();
    await zoneWithDocs.click();

    const sectionWithDocs = page
      .locator(".section-header")
      .filter({ has: page.locator(".section-badge") })
      .first();
    await expect(sectionWithDocs).toBeVisible();
    await sectionWithDocs.click();

    const artifactWithDocs = page
      .locator(".artifact-node")
      .filter({ has: page.locator(".artifact-badge") })
      .first();
    await expect(artifactWithDocs).toBeVisible();
    await artifactWithDocs.click();

    const inspectBtn = page.locator(".btn-inspect-doc").first();
    await expect(inspectBtn).toBeVisible();
    await inspectBtn.click();

    const drawerDialog = page.locator('.metadata-drawer[role="dialog"]');
    await expect(drawerDialog).toBeVisible();
    await expect(drawerDialog).toHaveAttribute("aria-modal", "true");

    const animationDurationSec = await drawerDialog.evaluate((el) =>
      parseFloat(window.getComputedStyle(el).animationDuration)
    );
    expect(animationDurationSec).toBeLessThanOrEqual(0.001);

    await expectNoWcagViolations(
      page,
      "DocumentManagerView ClinicalDrawer (/etmf)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-clinical-drawer.png" });
    }

    // Launch stacked PdfPreviewModal from inside open ClinicalDrawer
    await drawerDialog.locator(".btn-preview-from-drawer").click();
    const pdfDialog = page.locator('.pdf-preview-modal[role="dialog"]');
    await expect(pdfDialog).toBeVisible();

    // First Escape closes ONLY top PdfPreviewModal while ClinicalDrawer stays open
    await page.keyboard.press("Escape");
    await expect(pdfDialog).toBeHidden();
    await expect(drawerDialog).toBeVisible();

    // Second Escape closes ClinicalDrawer
    await page.keyboard.press("Escape");
    await expect(drawerDialog).toBeHidden();

    // 2. /training: StudyTrainingView -> ProtocolTrainingConsole ClinicalModal
    await page.evaluate(() => {
      const origFetch = window.fetch.bind(window);
      window.fetch = async (input, init) => {
        const url =
          typeof input === "string"
            ? input
            : input instanceof URL
              ? input.href
              : input.url;
        if (
          url.includes("/api/v1/training/delegations") &&
          !url.includes("bulk-signoff") &&
          (!init?.method || init.method.toUpperCase() === "GET")
        ) {
          return new Response(
            JSON.stringify([
              {
                id: "del-01",
                study_id: "CADENCE-101",
                site_id: "SITE-101",
                staff_user_id: "usr-01",
                staff_name: "Dr. Elena Rostova",
                role: "Sub-Investigator",
                task_code: "INFORMED_CONSENT",
                duties: "Informed Consent Administration",
                status: "PENDING",
                signed_off: false,
              },
            ]),
            {
              status: 200,
              headers: { "Content-Type": "application/json" },
            }
          );
        }
        return origFetch(input, init);
      };
    });
    await gotoHashRoute(page, "/training");
    await expect(page.locator("#section-study-training")).toBeVisible();
    const batchTab = page
      .locator(".tab-btn")
      .filter({ hasText: "Batch Assignments" });
    await expect(batchTab).toBeVisible();
    await batchTab.click();

    const firstSiteCheckbox = page
      .locator(".batch-filter-grid input[type='checkbox']")
      .first();
    await expect(firstSiteCheckbox).toBeVisible();
    await firstSiteCheckbox.check();

    const openBatchBtn = page.locator(".batch-preview-bar button");
    await expect(openBatchBtn).toBeEnabled();
    await openBatchBtn.click();

    const trainingModal = page.locator(
      '#section-study-training [role="dialog"]'
    );
    await expect(trainingModal).toBeVisible();
    await expect(trainingModal).toHaveAttribute("aria-modal", "true");
    await expectNoWcagViolations(
      page,
      "ProtocolTrainingConsole Batch Assignment ClinicalModal (/training)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/training-clinical-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(trainingModal).toBeHidden();

    // 3. /data-lock: DataLockView ClinicalModal (formerly suppressed backdrop)
    await gotoHashRoute(page, "/data-lock");
    const lockTrigger = page
      .locator(".btn-mini-lock, .btn-mini-freeze, .btn-mini-unlock")
      .first();
    await expect(lockTrigger).toBeVisible();
    await lockTrigger.click();

    const dataLockModal = page.locator('[role="dialog"]').first();
    await expect(dataLockModal).toBeVisible();
    await expect(dataLockModal).toHaveAttribute("aria-modal", "true");
    await expectNoWcagViolations(
      page,
      "DataLockView ClinicalModal (/data-lock)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/datalock-clinical-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(dataLockModal).toBeHidden();
  });

  test("DocumentsHubView MediaPreviewModal HLS video and blob PDF previews pass WCAG 2.2 AA and fire zero CSP violations", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_CSP_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await gotoHashRoute(page, "/documents");
    await expect(page.locator(".documents-hub-layout")).toBeVisible();

    const headCsp = await page.evaluate(() => {
      const headHtml = document.head.innerHTML;
      const cspMeta = document.head.querySelector(
        'meta[http-equiv="Content-Security-Policy"]'
      );
      return {
        content: cspMeta?.getAttribute("content") || "",
        cspIndex: headHtml.search(
          /<meta\b[^>]*http-equiv\s*=\s*["']?Content-Security-Policy/i
        ),
        firstScriptIndex: headHtml.search(/<script\b/i),
        firstLinkIndex: headHtml.search(/<link\b/i),
      };
    });

    expect(headCsp.cspIndex).toBeGreaterThanOrEqual(0);
    if (headCsp.firstScriptIndex >= 0) {
      expect(headCsp.cspIndex).toBeLessThan(headCsp.firstScriptIndex);
    }
    if (headCsp.firstLinkIndex >= 0) {
      expect(headCsp.cspIndex).toBeLessThan(headCsp.firstLinkIndex);
    }
    expect(headCsp.content).toContain("script-src 'self'");
    expect(headCsp.content).toContain("media-src 'self' blob:");
    expect(headCsp.content).toContain("worker-src 'self' blob:");
    expect(headCsp.content).toContain("connect-src 'self'");
    expect(headCsp.content).toContain("frame-src 'self' blob:");
    expect(headCsp.content).not.toContain("localhost");
    expect(headCsp.content).not.toContain("127.0.0.1");

    const ssoHtmlCsp = await page.evaluate(async () => {
      const res = await fetch("silent-check-sso.html");
      const html = await res.text();
      return {
        status: res.status,
        cspIndex: html.search(
          /<meta\b[^>]*http-equiv\s*=\s*["']?Content-Security-Policy/i
        ),
        scriptIndex: html.search(/<script\b/i),
        hasLocalhost: html.includes("localhost") || html.includes("127.0.0.1"),
      };
    });
    expect(ssoHtmlCsp.status).toBe(200);
    expect(ssoHtmlCsp.cspIndex).toBeGreaterThanOrEqual(0);
    expect(ssoHtmlCsp.cspIndex).toBeLessThan(ssoHtmlCsp.scriptIndex);
    expect(ssoHtmlCsp.hasLocalhost).toBe(false);

    // 1. PDF preview via blob: iframe URL inside MediaPlayer.vue (frame-src 'self' blob:)
    const pdfPreviewBtn = page.locator(
      'button[aria-label="Preview DEMO-01-Protocol-Synopsis.pdf"]'
    );
    await expect(pdfPreviewBtn).toBeVisible();
    await pdfPreviewBtn.click();

    const pdfIframe = page.locator('[data-testid="pdf-iframe"]');
    await expect(pdfIframe).toBeVisible();
    await expect
      .poll(async () => (await pdfIframe.getAttribute("src")) || "")
      .toMatch(/^blob:/);

    await expectNoWcagViolations(
      page,
      "DocumentsHubView MediaPreviewModal PDF blob preview"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/web-pdf-blob-preview-csp.png" });
    }

    await page.locator(".media-preview-modal .close-modal-btn").click();
    await expect(pdfIframe).toBeHidden();

    // 2. HLS adaptive stream preview via MediaSource + Worker blob: URLs inside MediaPlayer.vue
    const hlsPreviewBtn = page.locator(
      'button[aria-label="Preview DEMO-01-Site-Initiation-Briefing.mp4"]'
    );
    await expect(hlsPreviewBtn).toBeVisible();
    await hlsPreviewBtn.click();

    const videoEl = page.locator(".media-preview-modal video.video-element");
    await expect(videoEl).toBeVisible();
    await expect(page.locator("#quality-select")).toBeVisible();
    await expect
      .poll(async () =>
        videoEl.evaluate(
          (el) =>
            (el as HTMLVideoElement).currentSrc || el.getAttribute("src") || ""
        )
      )
      .toMatch(/^blob:/);

    await expectNoWcagViolations(
      page,
      "DocumentsHubView MediaPreviewModal HLS video preview"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/web-hls-preview-csp.png" });
    }

    await page.locator(".media-preview-modal .close-modal-btn").click();
    await expect(videoEl).toBeHidden();
  });
});
