import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const WCAG_AA_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

async function gotoHashRoute(page: Page, routePath: string) {
  await page.goto(`#${routePath}`);
}

async function expectNoWcagViolations(page: Page, contextLabel: string) {
  const results = await new AxeBuilder({ page })
    // Exclude non-interactive regulatory watermark overlay per ADR-2388
    .exclude(".watermark-overlay-container")
    .options({ rules: { "target-size": { enabled: true } } })
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

async function expectNoTargetSizeViolations(page: Page, contextLabel: string) {
  const results = await new AxeBuilder({ page })
    .exclude(".watermark-overlay-container")
    .options({ rules: { "target-size": { enabled: true } } })
    .withRules(["target-size"])
    .analyze();

  expect(
    results.violations,
    `SC 2.5.8 target-size violations on ${contextLabel}: ${JSON.stringify(
      results.violations.map((v) => ({
        id: v.id,
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

async function expectVisibleBoxesAtLeast24px(
  page: Page,
  selector: string,
  contextLabel: string
) {
  const locator = page.locator(selector);
  const total = await locator.count();
  let checked = 0;

  for (let i = 0; i < total; i++) {
    const item = locator.nth(i);
    if (!(await item.isVisible().catch(() => false))) {
      continue;
    }
    const box = await item.boundingBox();
    if (!box) {
      continue;
    }
    checked++;
    expect(
      box.width,
      `${contextLabel} (${selector} #${i}) width ${box.width.toFixed(2)}px < 24px`
    ).toBeGreaterThanOrEqual(23.95);
    expect(
      box.height,
      `${contextLabel} (${selector} #${i}) height ${box.height.toFixed(2)}px < 24px`
    ).toBeGreaterThanOrEqual(23.95);
  }

  expect(
    checked,
    `Expected at least one visible element matching ${selector} on ${contextLabel}`
  ).toBeGreaterThan(0);
}

async function focusWithKeyboardAndMeasureDualRing(
  page: Page,
  selector: string
) {
  const target = page.locator(selector).first();
  await expect(target).toBeVisible();
  await target.scrollIntoViewIfNeeded();
  // Establish keyboard modality in Chromium before focusing the target element
  await page.keyboard.press("Tab");
  await target.focus();

  return target.evaluate((el) => {
    function parseRgba(str: string): [number, number, number, number] | null {
      const m = str.match(
        /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([0-9.]+))?\s*\)/
      );
      if (!m) return null;
      return [
        Number(m[1]),
        Number(m[2]),
        Number(m[3]),
        m[4] === undefined ? 1 : Number(m[4]),
      ];
    }
    function luminance([r, g, b]: [number, number, number]) {
      const [rl, gl, bl] = [r, g, b].map((v) => {
        const s = v / 255;
        return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
      });
      return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
    }
    function contrast(
      a: [number, number, number],
      b: [number, number, number]
    ) {
      const l1 = luminance(a);
      const l2 = luminance(b);
      return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    }
    function resolveOpaqueBackground(
      start: Element | null
    ): [number, number, number] {
      let cur: Element | null = start;
      while (cur) {
        const bg = parseRgba(window.getComputedStyle(cur).backgroundColor);
        if (bg && bg[3] >= 0.9) {
          return [bg[0], bg[1], bg[2]];
        }
        cur = cur.parentElement;
      }
      return [255, 255, 255];
    }

    const style = window.getComputedStyle(el);
    const outlineParsed = parseRgba(style.outlineColor) || [0, 0, 0, 1];
    const outlineRgb: [number, number, number] = [
      outlineParsed[0],
      outlineParsed[1],
      outlineParsed[2],
    ];
    const shadowParsed = parseRgba(style.boxShadow);
    const haloRgb: [number, number, number] | null = shadowParsed
      ? [shadowParsed[0], shadowParsed[1], shadowParsed[2]]
      : null;

    const parentSurfaceRgb = resolveOpaqueBackground(el.parentElement);
    const selfSurfaceRgb = resolveOpaqueBackground(el);

    const parentContrast = haloRgb
      ? Math.max(
          contrast(outlineRgb, parentSurfaceRgb),
          contrast(haloRgb, parentSurfaceRgb)
        )
      : contrast(outlineRgb, parentSurfaceRgb);

    const selfContrast = haloRgb
      ? Math.max(
          contrast(outlineRgb, selfSurfaceRgb),
          contrast(haloRgb, selfSurfaceRgb)
        )
      : contrast(outlineRgb, selfSurfaceRgb);

    return {
      matchesFocusVisible: el.matches(":focus-visible"),
      outlineStyle: style.outlineStyle,
      outlineWidthPx: parseFloat(style.outlineWidth),
      outlineOffsetPx: parseFloat(style.outlineOffset),
      hasHaloShadow: Boolean(haloRgb) && style.boxShadow !== "none",
      parentContrast,
      selfContrast,
    };
  });
}

test.describe("Clinical Web Application WCAG 2.2 Level AA Contrast & Control Gates", () => {
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

  test("Enforces >= 24x24px pointer targets (SC 2.5.8) with zero axe-core target-size violations across /etmf, /training, /ctms, /ssu, /studies/CADENCE-101, /ecrf, and /knowledge", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    // 1. /etmf: .btn-node-export buttons
    await gotoHashRoute(page, "/etmf");
    await expect(page.locator(".document-manager-layout")).toBeVisible();
    await expect(page.locator(".btn-node-export").first()).toBeVisible();
    await expectVisibleBoxesAtLeast24px(
      page,
      ".btn-node-export",
      "DocumentManagerView (/etmf)"
    );
    await expectNoTargetSizeViolations(
      page,
      "DocumentManagerView (/etmf tree)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-24px-targets.png" });
    }

    // 2. /training: #training-protocol-filter select
    await gotoHashRoute(page, "/training");
    await expect(page.locator("#section-study-training")).toBeVisible();
    await expect(page.locator("#training-protocol-filter")).toBeVisible();
    await expectVisibleBoxesAtLeast24px(
      page,
      "#training-protocol-filter",
      "StudyTrainingView (/training)"
    );
    await expectNoTargetSizeViolations(page, "StudyTrainingView (/training)");

    // 3. /ctms and /ssu: .btn-sm compact action buttons
    await gotoHashRoute(page, "/ctms");
    await expect(page.locator("#section-ctms")).toBeVisible();
    await expectVisibleBoxesAtLeast24px(page, ".btn-sm", "CtmsView (/ctms)");
    await expectNoWcagViolations(page, "CtmsView (/ctms)");

    await gotoHashRoute(page, "/ssu");
    await expect(page.locator("#section-ctms")).toBeVisible();
    await page.locator("#tab-startup").click();
    await expect(page.locator(".ssu-greenlight-console")).toBeVisible();
    await expectVisibleBoxesAtLeast24px(page, ".btn-sm", "SsuView (/ssu)");
    await expectNoWcagViolations(page, "SsuView (/ssu)");

    // 4. /studies/CADENCE-101: Participants table buttons, inspector copy button, and Locks controls
    await gotoHashRoute(
      page,
      "/studies/CADENCE-101?stage=conduct&section=participants"
    );
    await expect(page.locator("#study-canvas")).toBeVisible();
    await expect(page.locator("#participants-section")).toBeVisible();
    await expect(page.locator(".row-select").first()).toBeVisible();
    await page.locator(".row-select").first().click();
    await expectVisibleBoxesAtLeast24px(
      page,
      "#study-canvas button",
      "StudyCanvasView (/studies/CADENCE-101 participants)"
    );
    await expectNoTargetSizeViolations(
      page,
      "StudyCanvasView (/studies/CADENCE-101 participants)"
    );

    await gotoHashRoute(
      page,
      "/studies/CADENCE-101?stage=closeout&section=locks"
    );
    await expect(page.locator("#study-locks-section")).toBeVisible();
    await expectVisibleBoxesAtLeast24px(
      page,
      "#study-locks-active-only, #study-locks-refresh",
      "StudyCanvasView (/studies/CADENCE-101 locks)"
    );
    await expectNoTargetSizeViolations(
      page,
      "StudyCanvasView (/studies/CADENCE-101 locks)"
    );

    // 5. /ecrf: inputs, radios, and CRA SDV checkboxes (while preserving 48px .clinical-input controls)
    await gotoHashRoute(page, "/ecrf");
    await expect(page.locator("#section-ecrf")).toBeVisible();
    const roleSelect = page.locator("#role-tester-select");
    if (await roleSelect.isVisible().catch(() => false)) {
      await roleSelect.selectOption("cra");
    }
    await expectVisibleBoxesAtLeast24px(
      page,
      '#section-ecrf input:not([type="hidden"]), #section-ecrf select',
      "EcrfView (/ecrf inputs and selects)"
    );
    await expectVisibleBoxesAtLeast24px(
      page,
      '#section-ecrf input[type="radio"]',
      "EcrfView (/ecrf radios)"
    );
    await expectVisibleBoxesAtLeast24px(
      page,
      '#section-ecrf input[type="checkbox"]',
      "EcrfView (/ecrf checkboxes)"
    );
    const clinicalTextBox = await page
      .locator(
        '.clinical-input input[type="text"], .clinical-input input[type="number"]'
      )
      .first()
      .boundingBox();
    expect(clinicalTextBox?.height ?? 0).toBeGreaterThanOrEqual(47.95);
    await expectNoWcagViolations(page, "EcrfView (/ecrf CRA SDV mode)");
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/ecrf-24px-controls.png" });
    }

    // 6. /knowledge: .tag-chip buttons
    await gotoHashRoute(page, "/knowledge");
    await expect(page.locator("#section-knowledge-hub")).toBeVisible();
    await expect(page.locator(".tag-chip").first()).toBeVisible();
    await expectVisibleBoxesAtLeast24px(
      page,
      ".tag-chip",
      "KnowledgeView (/knowledge .tag-chip)"
    );
    await expectNoTargetSizeViolations(page, "KnowledgeView (/knowledge)");
  });

  test("Displays two-color :focus-visible rings at >= 3:1 contrast on dark imaging controls (.toolbar-select, .toolbar-btn.active, .field-input, .diameter-input) and .ai-reason-input", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    await gotoHashRoute(page, "/imaging");
    await expect(page.locator("#section-imaging-viewer")).toBeVisible();
    await expect(page.locator(".mpr-toolbar")).toBeVisible();
    await expect(page.locator(".oncology-calipers-panel")).toBeVisible();
    await expectNoTargetSizeViolations(page, "ImagingView (/imaging)");

    for (const selector of [
      ".toolbar-select",
      ".toolbar-btn.active",
      ".field-input",
      ".diameter-input",
    ]) {
      await expectVisibleBoxesAtLeast24px(
        page,
        selector,
        `ImagingView (${selector})`
      );
      const metrics = await focusWithKeyboardAndMeasureDualRing(page, selector);
      expect(
        metrics.matchesFocusVisible,
        `${selector} should match :focus-visible`
      ).toBe(true);
      expect(
        metrics.outlineStyle,
        `${selector} outline-style must not be none`
      ).not.toBe("none");
      expect(metrics.outlineWidthPx).toBeGreaterThanOrEqual(2);
      expect(
        metrics.hasHaloShadow,
        `${selector} must render inner 2px halo box-shadow`
      ).toBe(true);
      expect(
        metrics.parentContrast,
        `${selector} dual focus ring contrast on parent surface (${metrics.parentContrast.toFixed(2)}:1) < 3:1`
      ).toBeGreaterThanOrEqual(3);
      expect(
        metrics.selfContrast,
        `${selector} dual focus ring contrast on control surface (${metrics.selfContrast.toFixed(2)}:1) < 3:1`
      ).toBeGreaterThanOrEqual(3);
    }

    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/focus-ring-imaging-dark.png" });
    }

    // Verify .ai-reason-input computed :focus-visible ring against both light drawer surface and dark surface
    await page.evaluate(() => {
      const probeHost = document.createElement("div");
      probeHost.id = "ai-reason-focus-probe-host";
      probeHost.style.cssText =
        "position: fixed; bottom: 12px; right: 12px; padding: 12px; background: var(--semantic-color-surface); z-index: 9999;";
      const textarea = document.createElement("textarea");
      textarea.className = "ai-reason-input";
      textarea.setAttribute("aria-label", "AI Audit Justification Probe");
      probeHost.appendChild(textarea);
      document.body.appendChild(probeHost);
    });

    const aiReasonMetrics = await focusWithKeyboardAndMeasureDualRing(
      page,
      "#ai-reason-focus-probe-host .ai-reason-input"
    );
    expect(aiReasonMetrics.matchesFocusVisible).toBe(true);
    expect(aiReasonMetrics.outlineStyle).not.toBe("none");
    expect(aiReasonMetrics.outlineWidthPx).toBeGreaterThanOrEqual(2);
    expect(aiReasonMetrics.hasHaloShadow).toBe(true);
    expect(aiReasonMetrics.parentContrast).toBeGreaterThanOrEqual(3);
    expect(aiReasonMetrics.selfContrast).toBeGreaterThanOrEqual(3);

    await page.evaluate(() => {
      document.getElementById("ai-reason-focus-probe-host")?.remove();
    });
  });
});
