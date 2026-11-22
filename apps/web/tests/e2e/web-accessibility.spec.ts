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

async function expectFocusInside(
  page: Page,
  containerSelector: string,
  contextLabel: string
) {
  const isInside = await page.evaluate((sel) => {
    const container = document.querySelector(sel);
    const active = document.activeElement;
    return Boolean(
      container &&
      active &&
      active !== document.body &&
      container.contains(active)
    );
  }, containerSelector);
  expect(
    isInside,
    `Expected document.activeElement to stay inside ${containerSelector} (${contextLabel})`
  ).toBe(true);
}

async function expectFocusContainedOnTabCycle(
  page: Page,
  containerSelector: string,
  contextLabel: string,
  tabPresses = 6
) {
  await expectFocusInside(
    page,
    containerSelector,
    `${contextLabel} initial focus`
  );
  for (let i = 0; i < tabPresses; i++) {
    await page.keyboard.press("Tab");
    await expectFocusInside(
      page,
      containerSelector,
      `${contextLabel} after Tab #${i + 1}`
    );
  }
  for (let i = 0; i < 2; i++) {
    await page.keyboard.press("Shift+Tab");
    await expectFocusInside(
      page,
      containerSelector,
      `${contextLabel} after Shift+Tab #${i + 1}`
    );
  }
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
    await inspectBtn.focus();
    await inspectBtn.click();

    const drawerDialog = page.locator('.metadata-drawer[role="dialog"]');
    await expect(drawerDialog).toBeVisible();
    await expect(drawerDialog).toHaveAttribute("aria-modal", "true");
    await expectFocusContainedOnTabCycle(
      page,
      '.metadata-drawer[role="dialog"]',
      "DocumentManagerView ClinicalDrawer (/etmf)",
      6
    );

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
    const previewFromDrawerBtn = drawerDialog.locator(
      ".btn-preview-from-drawer"
    );
    await previewFromDrawerBtn.focus();
    await previewFromDrawerBtn.click();
    const pdfDialog = page.locator('.pdf-preview-modal[role="dialog"]');
    await expect(pdfDialog).toBeVisible();
    await expectFocusContainedOnTabCycle(
      page,
      '.pdf-preview-modal[role="dialog"]',
      "Stacked PdfPreviewModal (/etmf)",
      4
    );

    // First Escape closes ONLY top PdfPreviewModal while ClinicalDrawer stays open and restores focus inside drawer
    await page.keyboard.press("Escape");
    await expect(pdfDialog).toBeHidden();
    await expect(drawerDialog).toBeVisible();
    await expect(previewFromDrawerBtn).toBeFocused();

    // Second Escape closes ClinicalDrawer and restores focus to .btn-inspect-doc
    await page.keyboard.press("Escape");
    await expect(drawerDialog).toBeHidden();
    await expect(inspectBtn).toBeFocused();

    // 2. /training: StudyTrainingView -> ProtocolTrainingConsole showBatchModal & showBulkSignModal
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
    await openBatchBtn.focus();
    await openBatchBtn.click();

    const trainingModal = page.locator(
      '#section-study-training [role="dialog"]'
    );
    await expect(trainingModal).toBeVisible();
    await expect(trainingModal).toHaveAttribute("aria-modal", "true");
    await expectFocusContainedOnTabCycle(
      page,
      '#section-study-training [role="dialog"]',
      "ProtocolTrainingConsole showBatchModal (/training)",
      5
    );
    await expectNoWcagViolations(
      page,
      "ProtocolTrainingConsole Batch Assignment ClinicalModal (/training)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/training-clinical-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(trainingModal).toBeHidden();
    await expect(openBatchBtn).toBeFocused();

    // Switch to Delegation Bulk Sign-Off tab and test showBulkSignModal
    const delegationTab = page
      .locator(".tab-btn")
      .filter({ hasText: "Delegation" });
    await expect(delegationTab).toBeVisible();
    await delegationTab.click();

    const selectAllDelegations = page
      .locator("#section-study-training thead input[type='checkbox']")
      .first();
    await expect(selectAllDelegations).toBeVisible();
    await selectAllDelegations.check();

    const openBulkSignBtn = page.locator(".delegation-actions-bar button");
    await expect(openBulkSignBtn).toBeEnabled();
    await openBulkSignBtn.focus();
    await openBulkSignBtn.click();

    await expect(trainingModal).toBeVisible();
    await expect(trainingModal).toHaveAttribute("aria-modal", "true");
    await expectFocusContainedOnTabCycle(
      page,
      '#section-study-training [role="dialog"]',
      "ProtocolTrainingConsole showBulkSignModal (/training)",
      6
    );
    await expectNoWcagViolations(
      page,
      "ProtocolTrainingConsole Bulk Delegation Sign-Off ClinicalModal (/training)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/training-bulk-sign-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(trainingModal).toBeHidden();
    await expect(openBulkSignBtn).toBeFocused();

    // 3. /ctms: CtmsView ClinicalModal Tab containment, Escape dismissal, and trigger focus return
    await gotoHashRoute(page, "/ctms");
    await expect(page.locator("#section-ctms")).toBeVisible();
    await page.locator("#tab-financials").click();
    const createTemplateBtn = page.locator("#btn-create-template");
    await expect(createTemplateBtn).toBeVisible();
    await createTemplateBtn.focus();
    await createTemplateBtn.click();

    const ctmsModal = page.locator('[role="dialog"]').first();
    await expect(ctmsModal).toBeVisible();
    await expect(ctmsModal).toHaveAttribute("aria-modal", "true");
    await expectFocusContainedOnTabCycle(
      page,
      '[role="dialog"]',
      "CtmsView Create Budget Template ClinicalModal (/ctms)",
      8
    );
    await expectNoWcagViolations(
      page,
      "CtmsView Create Budget Template ClinicalModal (/ctms)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/ctms-clinical-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(ctmsModal).toBeHidden();
    await expect(createTemplateBtn).toBeFocused();

    // 4. /data-lock: DataLockView ClinicalModal (formerly suppressed backdrop)
    await gotoHashRoute(page, "/data-lock");
    const lockTrigger = page
      .locator(".btn-mini-lock, .btn-mini-freeze, .btn-mini-unlock")
      .first();
    await expect(lockTrigger).toBeVisible();
    await lockTrigger.focus();
    await lockTrigger.click();

    const dataLockModal = page.locator('[role="dialog"]').first();
    await expect(dataLockModal).toBeVisible();
    await expect(dataLockModal).toHaveAttribute("aria-modal", "true");
    await expectFocusContainedOnTabCycle(
      page,
      '[role="dialog"]',
      "DataLockView ClinicalModal (/data-lock)",
      5
    );
    await expectNoWcagViolations(
      page,
      "DataLockView ClinicalModal (/data-lock)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/datalock-clinical-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(dataLockModal).toBeHidden();
    await expect(lockTrigger).toBeFocused();
  });

  test("OnboardingTour retains focus on step-2 transition with G59 target shortcut, and #mobile-nav-menu closes on Escape with trigger focus restoration", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await gotoHashRoute(page, "/overview");
    await expect(page.locator("#overview-hub-root")).toBeVisible();

    const checklistTrigger = page.locator("#btn-onboarding-checklist-trigger");
    await expect(checklistTrigger).toBeVisible();
    await checklistTrigger.focus();
    await checklistTrigger.click();

    const checklistDrawer = page.locator("#onboarding-checklist-drawer");
    await expect(checklistDrawer).toBeVisible();
    await expectFocusContainedOnTabCycle(
      page,
      "#onboarding-checklist-drawer",
      "OnboardingChecklistDrawer",
      5
    );

    await page.locator("#btn-drawer-start-tour").click();
    const tourPopover = page.locator(".onboarding-popover-card");
    await expect(tourPopover).toBeVisible();
    await expect(tourPopover).toHaveAttribute("aria-modal", "true");
    await expectFocusContainedOnTabCycle(
      page,
      ".onboarding-popover-card",
      "OnboardingTour (Step 1 modal)",
      5
    );

    // Advance to anchored Step 2 (#nav-studies): focus must stay inside .onboarding-popover-card (never dropping to document.body)
    await tourPopover.locator(".btn-tour-next").click();
    await expect(tourPopover.locator(".btn-tour-focus-target")).toBeVisible();
    const step2AriaModal = await tourPopover.getAttribute("aria-modal");
    expect(step2AriaModal).toBeNull();
    await expectFocusInside(
      page,
      ".onboarding-popover-card",
      "OnboardingTour (Step 2 anchored transition)"
    );
    await expectNoWcagViolations(page, "OnboardingTour (Step 2 anchored)");
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/onboarding-tour-step2.png" });
    }

    // WCAG G59 target focus shortcut focuses #nav-studies while keeping it non-inert, and F6 returns focus to popover
    await tourPopover.locator(".btn-tour-focus-target").click();
    await expect(page.locator("#nav-studies")).toBeFocused();
    await page.keyboard.press("F6");
    await expectFocusInside(
      page,
      ".onboarding-popover-card",
      "OnboardingTour (Step 2 after F6 return)"
    );

    // Escape dismisses anchored Step 2 and restores focus to #btn-onboarding-checklist-trigger
    await page.keyboard.press("Escape");
    await expect(tourPopover).toBeHidden();
    await expect(checklistTrigger).toBeFocused();

    // Mobile viewport: #mobile-nav-menu closes on Escape, sets aria-expanded="false", and restores focus to #btn-mobile-menu-toggle
    await page.setViewportSize({ width: 390, height: 844 });
    const mobileToggle = page.locator("#btn-mobile-menu-toggle");
    const mobileMenu = page.locator("#mobile-nav-menu");
    await expect(mobileToggle).toBeVisible();
    await expect(mobileToggle).toHaveAttribute("aria-expanded", "false");

    await mobileToggle.click();
    await expect(mobileToggle).toHaveAttribute("aria-expanded", "true");
    await expect(mobileMenu).toHaveClass(/is-open/);
    await expectNoWcagViolations(page, "AppShell #mobile-nav-menu open");
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/mobile-nav-menu.png" });
    }

    await mobileMenu.locator("a").first().focus();
    await page.keyboard.press("Escape");
    await expect(mobileToggle).toHaveAttribute("aria-expanded", "false");
    await expect(mobileMenu).not.toHaveClass(/is-open/);
    await expect(mobileToggle).toBeFocused();
  });
});
