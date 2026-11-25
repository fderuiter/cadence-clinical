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

async function expectClinicalTabsKeyboardNavigation(
  page: Page,
  tablistSelector: string,
  label: string
) {
  const tablist = page.locator(tablistSelector).first();
  await expect(tablist, `${label}: tablist visible`).toBeVisible();
  await expect(tablist).toHaveAttribute("role", "tablist");

  const enabledTabs = tablist.locator('[role="tab"]:not([disabled])');
  const count = await enabledTabs.count();
  expect(count, `${label}: expected >= 2 enabled tabs`).toBeGreaterThanOrEqual(
    2
  );

  // Verify button resets (no native outset/inset borders), >= 44px hit height, >= 4.5:1 label contrast, and >= 3:1 active contrast
  const visualMetrics = await tablist.evaluate((listEl) => {
    type Rgba = [number, number, number, number];
    function parseRgba(str: string): Rgba {
      const m = str.match(
        /rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)(?:\s*[,/]\s*([\d.]+))?\s*\)/
      );
      if (!m) return [255, 255, 255, 0];
      return [
        Number(m[1]),
        Number(m[2]),
        Number(m[3]),
        m[4] === undefined ? 1 : Number(m[4]),
      ];
    }
    function resolveEffectiveBg(
      startEl: Element | null
    ): [number, number, number] {
      let cur: Element | null = startEl;
      while (cur) {
        const bg = parseRgba(window.getComputedStyle(cur).backgroundColor);
        if (bg[3] >= 0.95) {
          return [bg[0], bg[1], bg[2]];
        }
        cur = cur.parentElement;
      }
      const rootTheme = document.documentElement.getAttribute("data-theme");
      return rootTheme === "dark" ? [15, 23, 42] : [255, 255, 255];
    }
    function luminance([r, g, b]: [number, number, number]) {
      const [rl, gl, bl] = [r, g, b].map((v) => {
        const s = v / 255;
        return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
      });
      return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
    }
    function contrast(
      rgbA: [number, number, number],
      rgbB: [number, number, number]
    ) {
      const l1 = luminance(rgbA);
      const l2 = luminance(rgbB);
      return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    }

    const tabs = Array.from(
      listEl.querySelectorAll<HTMLElement>('[role="tab"]:not([disabled])')
    );
    const isUnderline = listEl.classList.contains("variant-underline");
    const hostBg = resolveEffectiveBg(listEl);

    const tabReports = tabs.map((tab) => {
      const cs = window.getComputedStyle(tab);
      const rect = tab.getBoundingClientRect();
      const tabBgRgba = parseRgba(cs.backgroundColor);
      const effectiveTabBg: [number, number, number] =
        tabBgRgba[3] >= 0.95
          ? [tabBgRgba[0], tabBgRgba[1], tabBgRgba[2]]
          : hostBg;
      const fgRgba = parseRgba(cs.color);
      const fgRgb: [number, number, number] = [fgRgba[0], fgRgba[1], fgRgba[2]];
      const borderBottomRgba = parseRgba(cs.borderBottomColor);
      const indicatorRgb: [number, number, number] = isUnderline
        ? [borderBottomRgba[0], borderBottomRgba[1], borderBottomRgba[2]]
        : effectiveTabBg;
      return {
        id: tab.id,
        selected: tab.getAttribute("aria-selected") === "true",
        height: rect.height,
        borderStyles: [
          cs.borderTopStyle,
          cs.borderRightStyle,
          cs.borderBottomStyle,
          cs.borderLeftStyle,
        ],
        borderBottomWidth: parseFloat(cs.borderBottomWidth || "0"),
        labelContrast: contrast(fgRgb, effectiveTabBg),
        indicatorContrastVsHost: contrast(indicatorRgb, hostBg),
        effectiveTabBg,
      };
    });

    const activeReport = tabReports.find((r) => r.selected) || null;
    const inactiveReport = tabReports.find((r) => !r.selected) || null;
    const activeVsInactiveContrast =
      !isUnderline && activeReport && inactiveReport
        ? contrast(activeReport.effectiveTabBg, inactiveReport.effectiveTabBg)
        : null;

    return {
      isUnderline,
      tabReports,
      activeReport,
      activeVsInactiveContrast,
    };
  });

  for (const report of visualMetrics.tabReports) {
    for (const bs of report.borderStyles) {
      expect(
        bs,
        `${label} (${report.id}): native outset/inset border`
      ).not.toBe("outset");
      expect(
        bs,
        `${label} (${report.id}): native outset/inset border`
      ).not.toBe("inset");
    }
    expect(
      report.height,
      `${label} (${report.id}): expected >= 44px tab height, got ${report.height}`
    ).toBeGreaterThanOrEqual(44);
    expect(
      report.labelContrast,
      `${label} (${report.id}): expected >= 4.5:1 label contrast, got ${report.labelContrast.toFixed(2)}`
    ).toBeGreaterThanOrEqual(4.5);
  }

  expect(
    visualMetrics.activeReport,
    `${label}: expected an active [aria-selected="true"] tab`
  ).not.toBeNull();
  expect(
    visualMetrics.activeReport!.indicatorContrastVsHost,
    `${label}: expected >= 3:1 active indicator contrast, got ${visualMetrics.activeReport!.indicatorContrastVsHost.toFixed(2)}`
  ).toBeGreaterThanOrEqual(3.0);
  if (visualMetrics.isUnderline) {
    expect(
      visualMetrics.activeReport!.borderBottomWidth,
      `${label}: expected underline active indicator width >= 2px`
    ).toBeGreaterThanOrEqual(2);
  } else if (visualMetrics.activeVsInactiveContrast !== null) {
    expect(
      visualMetrics.activeVsInactiveContrast,
      `${label}: expected >= 3:1 active vs inactive tab fill contrast`
    ).toBeGreaterThanOrEqual(3.0);
  }

  // Verify roving tabindex invariant: exactly 1 tab has tabindex="0"
  await expect(tablist.locator('[role="tab"][tabindex="0"]')).toHaveCount(1);

  const assertActiveTabAndPanel = async (tabIndex: number) => {
    const tab = enabledTabs.nth(tabIndex);
    await expect(tab).toBeFocused();
    await expect(tab).toHaveAttribute("aria-selected", "true");
    await expect(tab).toHaveAttribute("tabindex", "0");
    const bounds = await tab.evaluate((el) => {
      const list = el.closest('[role="tablist"]');
      const tabRect = el.getBoundingClientRect();
      const listRect = list
        ? list.getBoundingClientRect()
        : { left: 0, right: window.innerWidth };
      return {
        tabLeft: tabRect.left,
        tabRight: tabRect.right,
        listLeft: listRect.left,
        listRight: listRect.right,
      };
    });
    expect(
      bounds.tabLeft,
      `${label}: focused tab[${tabIndex}] clipped on left (${bounds.tabLeft} < ${bounds.listLeft})`
    ).toBeGreaterThanOrEqual(bounds.listLeft - 1);
    expect(
      bounds.tabRight,
      `${label}: focused tab[${tabIndex}] clipped on right (${bounds.tabRight} > ${bounds.listRight})`
    ).toBeLessThanOrEqual(bounds.listRight + 1);
    const tabId = await tab.getAttribute("id");
    const panelId = await tab.getAttribute("aria-controls");
    expect(tabId, `${label}: tab id defined`).toBeTruthy();
    expect(panelId, `${label}: aria-controls defined`).toBeTruthy();
    const panel = page.locator(`[id="${panelId}"]`);
    await expect(panel, `${label}: controlled tabpanel visible`).toBeVisible();
    await expect(panel).toHaveAttribute("role", "tabpanel");
    await expect(panel).toHaveAttribute("aria-labelledby", tabId!);
  };

  // Focus first enabled tab and ensure starting at index 0 via Home
  await enabledTabs.first().focus();
  await page.keyboard.press("Home");
  await assertActiveTabAndPanel(0);

  // ArrowRight advances to index 1
  await page.keyboard.press("ArrowRight");
  await assertActiveTabAndPanel(1);

  // End jumps to last enabled tab
  await page.keyboard.press("End");
  await assertActiveTabAndPanel(count - 1);

  // Home returns to index 0
  await page.keyboard.press("Home");
  await assertActiveTabAndPanel(0);

  // ArrowLeft wraps to last enabled tab, then Home restores initial tab
  await page.keyboard.press("ArrowLeft");
  await assertActiveTabAndPanel(count - 1);
  await page.keyboard.press("Home");
  await assertActiveTabAndPanel(0);
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
  let nativeDialogs: string[] = [];

  test.beforeEach(async ({ page }) => {
    nativeDialogs = [];
    page.on("dialog", async (dialog) => {
      nativeDialogs.push(`${dialog.type()}: ${dialog.message()}`);
      await dialog.dismiss();
    });

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
    expect(
      nativeDialogs,
      "Expected zero native blocking browser dialogs (alert/confirm/prompt)"
    ).toEqual([]);

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
    test.setTimeout(90_000);
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await gotoHashRoute(page, "/overview");
    await expect(page.locator("#overview-hub-root")).toBeVisible();
    await expect(page.locator(".btn-task-launch").first()).toBeVisible();
    await expectNoWcagViolations(page, "OverviewView");

    const taskLaunchBorderRatio = await getBorderContrastRatio(
      page,
      ".btn-task-launch"
    );
    expect(taskLaunchBorderRatio).toBeGreaterThanOrEqual(3);

    // Open OnboardingChecklistDrawer (via compact Account menu when <= 1280px) and audit pending status pills and task navigation buttons
    const compactActionsBtn = page.locator("#btn-header-actions-menu");
    const openOnboardingChecklistDrawer = async (auditCompactMenu = false) => {
      if (await compactActionsBtn.isVisible().catch(() => false)) {
        const menuOpen = await page
          .locator("#header-secondary-menu.is-open")
          .isVisible()
          .catch(() => false);
        if (!menuOpen) {
          await compactActionsBtn.click();
          await expect(
            page.locator("#header-secondary-menu.is-open")
          ).toBeVisible();
        }
        if (auditCompactMenu) {
          await expectNoWcagViolations(
            page,
            "AppShell compact secondary menu (#header-secondary-menu)"
          );
        }
      }
      await page.locator("#btn-onboarding-checklist-trigger").click();
    };
    await openOnboardingChecklistDrawer(true);
    await expect(page.locator("#onboarding-checklist-drawer")).toBeVisible();
    await expect(
      page.locator("#onboarding-checklist-drawer .status-pending").first()
    ).toBeVisible();
    await expectNoWcagViolations(page, "OnboardingChecklistDrawer");

    const expectTourButtonsMeet44pxAndMinFont = async (stepLabel: string) => {
      const buttons = page.locator(".onboarding-popover-card button");
      const count = await buttons.count();
      expect(count, `${stepLabel}: expected tour buttons`).toBeGreaterThan(0);
      for (let i = 0; i < count; i++) {
        const btn = buttons.nth(i);
        if (!(await btn.isVisible().catch(() => false))) continue;
        const box = await btn.boundingBox();
        expect(box, `${stepLabel}: button #${i} boundingBox`).not.toBeNull();
        expect(
          box!.height,
          `${stepLabel}: button #${i} height >= 44px`
        ).toBeGreaterThanOrEqual(43.9);
        expect(
          box!.width,
          `${stepLabel}: button #${i} width >= 44px`
        ).toBeGreaterThanOrEqual(43.9);
        const fontSizePx = await btn.evaluate((el) =>
          parseFloat(window.getComputedStyle(el).fontSize)
        );
        expect(
          fontSizePx,
          `${stepLabel}: button #${i} fontSize >= 12px (0.75rem)`
        ).toBeGreaterThanOrEqual(11.9);
      }
    };

    // Launch OnboardingTour and audit Step 1 controls + final step .btn-tour-complete
    await page.locator("#btn-drawer-start-tour").click();
    const tourPopover = page.locator(".onboarding-popover-card");
    await expect(tourPopover).toBeVisible();
    await expectTourButtonsMeet44pxAndMinFont(
      "OnboardingTour (Desktop Step 1)"
    );
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
    await expectTourButtonsMeet44pxAndMinFont(
      "OnboardingTour (Desktop Final Step)"
    );
    await expectNoWcagViolations(
      page,
      "OnboardingTour (Final Step .btn-tour-complete)"
    );
    await tourPopover.locator(".btn-tour-complete").click();
    await expect(tourPopover).toBeHidden();

    // Verify 768x1024 compact tablet anchors: >=44px #btn-mobile-menu-toggle, clamped bounds, and non-inert #main-content
    await page.setViewportSize({ width: 768, height: 1024 });
    const mobileMenuToggle = page.locator("#btn-mobile-menu-toggle");
    await expect(mobileMenuToggle).toBeVisible();
    const toggleBox = await mobileMenuToggle.boundingBox();
    expect(toggleBox).not.toBeNull();
    expect(toggleBox!.width).toBeGreaterThanOrEqual(43.9);
    expect(toggleBox!.height).toBeGreaterThanOrEqual(43.9);

    await gotoHashRoute(page, "/overview");
    await openOnboardingChecklistDrawer();
    await expect(page.locator("#onboarding-checklist-drawer")).toBeVisible();
    await page.locator("#btn-drawer-start-tour").click();
    await expect(tourPopover).toBeVisible();

    // Advance through intermediate steps on 768x1024 and verify clamped bounds + non-inert #main-content
    for (let stepNum = 2; stepNum <= 4; stepNum++) {
      await tourPopover.locator(".btn-tour-next").click();
      await expect(tourPopover).toContainText(`Step ${stepNum} of`);
      await expect(tourPopover).not.toHaveAttribute("aria-modal", "true");
      await expect(page.locator("#main-content")).not.toHaveAttribute("inert");
      const box = await tourPopover.boundingBox();
      expect(box).not.toBeNull();
      expect(box!.x).toBeGreaterThanOrEqual(11.5);
      expect(box!.x + box!.width).toBeLessThanOrEqual(768 - 11.5);
      expect(box!.y).toBeGreaterThanOrEqual(11.5);
      expect(box!.y + box!.height).toBeLessThanOrEqual(1024 - 11.5);
      await expectTourButtonsMeet44pxAndMinFont(
        `OnboardingTour (768x1024 Step ${stepNum})`
      );
      if (captureScreenshots && stepNum === 2) {
        await page.screenshot({
          path: "/tmp/onboarding-tour-tablet-768x1024.png",
        });
      }
    }
    await expectNoWcagViolations(
      page,
      "OnboardingTour (768x1024 Compact Nav Fallback)"
    );

    // Switch persona to site_crc on 768x1024 and verify eCRF workspace anchors without inert locks
    await gotoHashRoute(page, "/overview");
    await page.locator("#persona-select").selectOption("site_crc");
    await openOnboardingChecklistDrawer();
    await page.locator("#btn-drawer-start-tour").click();
    await expect(tourPopover).toBeVisible();

    for (let stepNum = 2; stepNum <= 4; stepNum++) {
      await tourPopover.locator(".btn-tour-next").click();
      await expect(tourPopover).toContainText(`Step ${stepNum} of`);
      await expect(tourPopover).not.toHaveAttribute("aria-modal", "true");
      await expect(page.locator("#main-content")).not.toHaveAttribute("inert");
      const box = await tourPopover.boundingBox();
      expect(box).not.toBeNull();
      expect(box!.x).toBeGreaterThanOrEqual(11.5);
      expect(box!.x + box!.width).toBeLessThanOrEqual(768 - 11.5);
      if (captureScreenshots && stepNum === 3) {
        await page.screenshot({
          path: "/tmp/onboarding-tour-site-crc-tablet.png",
        });
      }
    }
    await expectNoWcagViolations(
      page,
      "OnboardingTour (768x1024 site_crc eCRF Anchors)"
    );

    // Verify 200% zoom / short reflow viewport (1024x416 at zoom: 2) caps height within 90dvh and scrolls to action buttons
    await page.setViewportSize({ width: 1024, height: 416 });
    await page.evaluate(() => {
      document.documentElement.style.zoom = "2";
      window.dispatchEvent(new Event("resize"));
    });
    const zoomOverflowState = await tourPopover.evaluate((el) => {
      const style = window.getComputedStyle(el);
      el.scrollTop = el.scrollHeight;
      const rect = el.getBoundingClientRect();
      return {
        overflowY: style.overflowY,
        rectLeft: rect.left,
        rectRight: rect.right,
        rectTop: rect.top,
        rectBottom: rect.bottom,
        clientHeight: el.clientHeight,
        scrollHeight: el.scrollHeight,
      };
    });
    expect(zoomOverflowState.overflowY).toBe("auto");
    expect(zoomOverflowState.rectLeft).toBeGreaterThanOrEqual(11.5);
    expect(zoomOverflowState.rectRight).toBeLessThanOrEqual(1024 - 11.5);
    expect(zoomOverflowState.rectTop).toBeGreaterThanOrEqual(11.5);
    expect(zoomOverflowState.rectBottom).toBeLessThanOrEqual(416 - 11.5);
    await expect(tourPopover.locator(".btn-tour-next")).toBeVisible();
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/onboarding-tour-200pct-zoom.png",
      });
    }

    // Restore zoom and desktop viewport
    await page.evaluate(() => {
      document.documentElement.style.zoom = "";
      window.dispatchEvent(new Event("resize"));
    });
    await page.setViewportSize({ width: 1280, height: 720 });
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

    // 2. Regulated video preview in demo mode (no eager hls.js per ADR-2537) + runtime MediaSource & Worker blob: CSP verification (ADR-2498)
    const hlsPreviewBtn = page.locator(
      'button[aria-label="Preview DEMO-01-Site-Initiation-Briefing.mp4"]'
    );
    await expect(hlsPreviewBtn).toBeVisible();
    await hlsPreviewBtn.click();

    const videoEl = page.locator(".media-preview-modal video.video-element");
    await expect(videoEl).toBeVisible();
    await expect
      .poll(async () =>
        videoEl.evaluate(
          (el) =>
            (el as HTMLVideoElement).currentSrc || el.getAttribute("src") || ""
        )
      )
      .toContain("/api/v1/fileshare/demo-stream/master.m3u8");

    const blobMediaAndWorkerProbe = await videoEl.evaluate(async (el) => {
      const workerBlob = new Blob(
        ['self.onmessage = (e) => self.postMessage("ack:" + e.data);'],
        { type: "application/javascript" }
      );
      const workerUrl = URL.createObjectURL(workerBlob);
      let workerReply: string;
      try {
        const worker = new Worker(workerUrl);
        workerReply = await new Promise<string>((resolve, reject) => {
          worker.onmessage = (ev) => resolve(String(ev.data));
          worker.onerror = (err) =>
            reject(new Error(err.message || "Worker CSP error"));
          worker.postMessage("csp");
        });
        worker.terminate();
      } finally {
        URL.revokeObjectURL(workerUrl);
      }

      const mediaSource = new MediaSource();
      const mediaBlobUrl = URL.createObjectURL(mediaSource);
      try {
        (el as HTMLVideoElement).src = mediaBlobUrl;
        await new Promise<void>((resolve) => {
          mediaSource.addEventListener("sourceopen", () => resolve(), {
            once: true,
          });
          setTimeout(() => resolve(), 250);
        });
        return {
          workerReply,
          assignedSrc:
            (el as HTMLVideoElement).currentSrc || el.getAttribute("src") || "",
        };
      } finally {
        URL.revokeObjectURL(mediaBlobUrl);
      }
    });
    expect(blobMediaAndWorkerProbe.workerReply).toBe("ack:csp");
    expect(blobMediaAndWorkerProbe.assignedSrc).toMatch(/^blob:/);

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

  test("TicketsView, KnowledgeView, and ParticipantWalletView pass WCAG 2.2 AA scrollable-region-focusable, keyboard card activation, and non-trapping inline focus handoff", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    // 1. /tickets: Kanban scrollable regions + Enter/Space card activation
    await gotoHashRoute(page, "/tickets");
    const kanbanBoard = page.locator(".kanban-board");
    await expect(kanbanBoard).toBeVisible();
    await expect(kanbanBoard).toHaveAttribute("tabindex", "0");
    await expect(kanbanBoard).toHaveAttribute("role", "tabpanel");

    await expectNoWcagViolations(
      page,
      "TicketsView Kanban Board & Scrollable Columns (/tickets)"
    );

    const firstTicketCard = page.locator(".ticket-card").first();
    await expect(firstTicketCard).toBeVisible();
    await firstTicketCard.focus();
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/tickets-kanban-keyboard-focus.png",
      });
    }

    await page.keyboard.press("Enter");
    const ticketDialog = page.locator('[role="dialog"]').first();
    await expect(ticketDialog).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(ticketDialog).toBeHidden();

    await firstTicketCard.focus();
    await page.keyboard.press("Space");
    await expect(ticketDialog).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(ticketDialog).toBeHidden();

    // 2. /knowledge: SOP stream & reader scrollable regions + Enter/Space card activation
    await gotoHashRoute(page, "/knowledge");
    const articlesStream = page.locator(".articles-stream");
    await expect(articlesStream).toBeVisible();
    await expect(articlesStream).toHaveAttribute("tabindex", "0");
    await expect(articlesStream).toHaveAttribute("role", "region");

    const articleCards = page.locator(".article-card");
    await expect(articleCards.first()).toBeVisible();
    await articleCards.first().focus();
    await page.keyboard.press("Enter");
    await expect(articleCards.first()).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".reader-body-card")).toHaveAttribute(
      "tabindex",
      "0"
    );

    await expectNoWcagViolations(
      page,
      "KnowledgeView SOP Stream & Reader Pane (/knowledge)"
    );
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/knowledge-stream-keyboard-focus.png",
      });
    }

    if ((await articleCards.count()) > 1) {
      await articleCards.nth(1).focus();
      await page.keyboard.press("Space");
      await expect(articleCards.nth(1)).toHaveAttribute("aria-pressed", "true");
    }

    // 3. /participant-wallet: Inline wallet selection focuses heading without trapping unmodified Tab, and Close restores trigger
    await gotoHashRoute(page, "/participant-wallet");
    const reviewWalletBtn = page.locator('[id^="btn-inspect-wallet-"]').first();
    if ((await reviewWalletBtn.count()) > 0) {
      const triggerId = await reviewWalletBtn.getAttribute("id");
      await reviewWalletBtn.focus();
      await page.keyboard.press("Enter");

      const selectedHeading = page.locator("#selected-wallet-heading");
      await expect(selectedHeading).toBeVisible();
      await expect(selectedHeading).toBeFocused();

      if (captureScreenshots) {
        await page.screenshot({
          path: "/tmp/wallet-inline-focus-handoff.png",
        });
      }

      // Unmodified Tab moves freely without modal trapping (SC 2.1.2)
      await page.keyboard.press("Tab");
      const activeTag = await page.evaluate(
        () => document.activeElement?.tagName
      );
      expect(activeTag).not.toBe("BODY");

      await page.locator("#btn-close-selected-wallet").click();
      if (triggerId) {
        await expect(page.locator(`#${triggerId}`)).toBeFocused();
      }
    }
  });

  test("Clinical Canvas skip link, proposal selection, roving toolbar, Cmd/Ctrl+E shortcut, site activation Explain, and CanvasErrorAlert pass WCAG 2.2 AA", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_CANVAS_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    await gotoHashRoute(page, "/login");
    await expect(page.locator("#section-login")).toBeVisible();

    const STUDY_REF = "study:0192f3c4-5e6a-7b8c-9d0e-0000000000a1@2";
    const SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-0000000000b1@1";
    const PARTICIPANT_ID = "0192f3c4-5e6a-7b8c-9d0e-000000000001";
    const PARTICIPANT_REF = `participant:${PARTICIPANT_ID}`;
    const PROPOSAL_BASE = "proposal:0192f3c4-5e6a-7b8c-9d0e-0000000000e1";
    const PROPOSAL_REF = `${PROPOSAL_BASE}@1`;

    await page.evaluate(
      ({ studyRef, siteRef, participantId, participantRef, proposalRef }) => {
        (
          window as unknown as { __failQueriesOnce?: boolean }
        ).__failQueriesOnce = false;
        const origFetch = window.fetch.bind(window);
        const json = (payload: unknown, status = 200) =>
          new Response(JSON.stringify(payload), {
            status,
            headers: { "Content-Type": "application/json" },
          });

        window.fetch = async (input, init) => {
          const url =
            typeof input === "string"
              ? input
              : input instanceof URL
                ? input.href
                : input.url;
          const method = (init?.method || "GET").toUpperCase();

          if (method === "GET" && url.includes("/api/v1/kernel/search")) {
            return json([
              {
                ref: studyRef,
                kind: "study",
                code: "CADENCE-101",
                title: "Phase III Melanoma",
                detail: null,
                exact_code: true,
                rank: 1,
              },
            ]);
          }
          if (method === "GET" && url.includes("/api/v1/kernel/activity")) {
            return json({ entries: [], next: null });
          }
          if (
            method === "GET" &&
            url.includes("/api/v1/kernel/nodes/") &&
            url.includes("/children")
          ) {
            return json([
              {
                node: {
                  ref: siteRef,
                  kind: "study_site",
                  code: "SITE-101",
                  title: "Mayo Clinic Oncology",
                  status: "PENDING",
                  mask: null,
                },
                child_count: 1,
              },
            ]);
          }
          if (
            method === "GET" &&
            url.includes("/api/v1/kernel/nodes/") &&
            url.includes("/commands")
          ) {
            return json([
              {
                command: "proposal.reject",
                capability: "proposal.reject",
                decision: {
                  permitted: false,
                  reason:
                    "An active Data Manager role grant on CADENCE-101 is required.",
                  conditions: [
                    {
                      code: "grant.data_manager",
                      description:
                        "Active Data Manager role grant on CADENCE-101",
                      met: false,
                      evidence: [studyRef],
                    },
                  ],
                  explanation: {
                    claim:
                      "Proposal rejection requires an active Data Manager grant on CADENCE-101.",
                    steps: [
                      {
                        claim:
                          "Evaluated actor grants across study scope CADENCE-101.",
                        evidence: [studyRef],
                      },
                    ],
                    conditions: [
                      {
                        code: "grant.data_manager",
                        description:
                          "Active Data Manager role grant on CADENCE-101",
                        met: false,
                        evidence: [studyRef],
                      },
                    ],
                    next_actions: [
                      {
                        description:
                          "Request a Data Manager role grant on CADENCE-101.",
                        target: studyRef,
                      },
                    ],
                  },
                },
              },
            ]);
          }
          if (
            method === "GET" &&
            url.includes("/api/v1/kernel/authorization/can-i")
          ) {
            return json({
              permitted: false,
              reason:
                "Site activation requires an executed clinical site agreement in eTMF.",
              conditions: [
                {
                  code: "etmf.irb_approval",
                  description: "Approved IRB determination filed in eTMF",
                  met: true,
                  evidence: [siteRef],
                },
                {
                  code: "etmf.site_contract_executed",
                  description: "Executed clinical site agreement filed in eTMF",
                  met: false,
                  evidence: [siteRef],
                },
              ],
              explanation: {
                claim:
                  "SITE-101 cannot be activated until the executed clinical site agreement is filed.",
                steps: [
                  {
                    claim:
                      "Checked study_site.activate prerequisites for SITE-101.",
                    evidence: [siteRef],
                  },
                ],
                conditions: [
                  {
                    code: "etmf.irb_approval",
                    description: "Approved IRB determination filed in eTMF",
                    met: true,
                    evidence: [siteRef],
                  },
                  {
                    code: "etmf.site_contract_executed",
                    description:
                      "Executed clinical site agreement filed in eTMF",
                    met: false,
                    evidence: [siteRef],
                  },
                ],
                next_actions: [
                  {
                    description:
                      "Upload and approve the executed clinical site agreement for SITE-101.",
                    target: siteRef,
                  },
                ],
              },
            });
          }
          if (method === "GET" && url.includes("/api/v1/kernel/proposals/")) {
            return json({
              ref: proposalRef,
              study: studyRef,
              intent: "Query the out-of-range systolic pressure at visit 3",
              plan: {
                ref: "artifact_version:0192f3c4-5e6a-7b8c-9d0e-0000000000d1@1",
                title: "Data review plan",
                number: 1,
                media_type: "text/markdown",
                content_hash: "sha256:plan",
                mask: null,
              },
              affected: [{ ref: `${participantRef}@4`, mask: null }],
              simulated_impact: null,
              requested_capabilities: ["query.raise"],
              proposed_at: "2026-10-20T09:00:00Z",
              expires_at: "2026-10-27T09:00:00Z",
              proposed_by: "agent:data-reviewer",
              requested_by: "data-manager@sponsor.test",
              proposed_by_kind: "AutomationAgent",
              model_id: "claude-3-7-sonnet",
              confidence_score: 0.86,
              content_hash: "sha256:proposal",
              status: "Open",
              decided_at: null,
              decided_by: null,
              approval_attestation: null,
              decision_reason: null,
              is_escalated: false,
              escalated_to: null,
              escalation_reason: null,
              mask: null,
            });
          }
          if (method === "GET" && url.includes("/api/v1/kernel/proposals")) {
            return json([]);
          }
          if (method === "GET" && url.includes("/api/v1/kernel/nodes/")) {
            if (url.includes("proposal")) {
              return json({
                ref: proposalRef,
                kind: "proposal",
                title: "Query the out-of-range systolic pressure at visit 3",
                code: null,
                status: "Open",
                mask: null,
              });
            }
            if (url.includes("study_site")) {
              return json({
                ref: siteRef,
                kind: "study_site",
                title: "Mayo Clinic Oncology",
                code: "SITE-101",
                status: "PENDING",
                mask: null,
              });
            }
            return json({
              ref: `${participantRef}@4`,
              kind: "participant",
              title: "017-0001",
              code: "017-0001",
              status: "ACTIVE",
              mask: null,
            });
          }
          if (
            method === "GET" &&
            url.includes("/api/v1/execution/sites/SITE-101/compliance-status")
          ) {
            return json({
              study_id: "CADENCE-101",
              site_id: "SITE-101",
              is_complete: false,
              is_activated: false,
              checked_at: "2026-11-22T12:00:00Z",
              missing_documents: ["Executed Clinical Site Agreement"],
            });
          }
          if (method === "GET" && url.includes("/api/v1/execution/subjects")) {
            return json({
              items: [
                {
                  id: participantId,
                  subject_id: "017-0001",
                  study_id: "CADENCE-101",
                  site_id: "SITE-101",
                  status: "ACTIVE",
                  active_protocol_version: "3.0",
                  enrollment_index: 1,
                },
              ],
              total: 1,
              limit: 50,
              offset: 0,
            });
          }
          if (
            method === "GET" &&
            url.includes("/api/v1/execution/forms/queries")
          ) {
            const win = window as unknown as { __failQueriesOnce?: boolean };
            if (win.__failQueriesOnce) {
              win.__failQueriesOnce = false;
              return json(
                { detail: "Upstream Execution gateway read timeout (503)" },
                503
              );
            }
            return json([]);
          }

          return origFetch(input, init);
        };
      },
      {
        studyRef: STUDY_REF,
        siteRef: SITE_REF,
        participantId: PARTICIPANT_ID,
        participantRef: PARTICIPANT_REF,
        proposalRef: PROPOSAL_REF,
      }
    );

    // 1. Study skip link + ?proposal= inspector expansion + roving toolbar + Ctrl+E shortcut
    await gotoHashRoute(page, `/studies/CADENCE-101?proposal=${PROPOSAL_BASE}`);
    await expect(page.locator("#proposal-review")).toBeVisible();

    const skipWorkspace = page.locator("#skip-to-workspace");
    await skipWorkspace.focus();
    await expect(skipWorkspace).toBeVisible();
    await page.keyboard.press("Enter");
    await expect(page.locator("#canvas-workspace")).toBeFocused();

    const inspector = page.locator("#canvas-inspector");
    await expect(inspector).toHaveAttribute("data-collapsed", "false");
    await expect(page.locator("#canvas-inspector-title")).toHaveText(
      "Query the out-of-range systolic pressure at visit 3"
    );

    const verbDetails = page.locator("#verb-details");
    await verbDetails.focus();
    await expect(verbDetails).toHaveAttribute("tabindex", "0");
    await expect(page.locator("#verb-trace")).toHaveAttribute("tabindex", "-1");

    await page.keyboard.press("ArrowRight");
    await expect(page.locator("#verb-trace")).toBeFocused();
    await expect(page.locator("#verb-trace")).toHaveAttribute("tabindex", "0");
    await expect(verbDetails).toHaveAttribute("tabindex", "-1");

    await page.keyboard.press("End");
    await expect(page.locator("#verb-assist")).toBeFocused();
    await page.keyboard.press("Home");
    await expect(verbDetails).toBeFocused();

    // Typing Ctrl+E inside an editable textarea is ignored
    await page.locator("#proposal-reject-reason").focus();
    await page.keyboard.press("Control+e");
    await expect(page.locator("#kernel-explain")).toHaveCount(0);

    // Pressing Ctrl+E outside editable inputs opens #kernel-explain and moves focus to it
    await page.locator("#canvas-workspace").focus();
    await page.keyboard.press("Control+e");
    await expect(page.locator("#kernel-explain")).toBeVisible();
    await expect(page.locator("#kernel-explain")).toBeFocused();

    await expectNoWcagViolations(
      page,
      "Clinical Canvas proposal review, roving toolbar, and Explain"
    );
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/verify-canvas-proposal-toolbar.png",
      });
    }

    // 2. SiteActivationPanel persistent disabled button + inline Explain disclosure via kernelApi.canI
    await gotoHashRoute(
      page,
      "/studies/CADENCE-101?stage=start&section=site-activation"
    );
    await expect(page.locator("#site-activation-panel")).toBeVisible();
    await page.locator("#site-activation-site").fill("SITE-101");
    await page.locator("#btn-check-site-compliance").click();

    const activateBtn = page.locator("#btn-activate-site");
    await expect(activateBtn).toBeVisible();
    await expect(activateBtn).toBeDisabled();

    const explainBtn = page.locator("#btn-explain-site-activation");
    await expect(explainBtn).toBeVisible();
    await expect(explainBtn).toHaveAttribute("aria-expanded", "false");
    await explainBtn.click();
    await expect(explainBtn).toHaveAttribute("aria-expanded", "true");

    const siteExplain = page.locator("#site-activation-explanation");
    await expect(siteExplain).toBeVisible();
    await expect(siteExplain).toContainText(
      "SITE-101 cannot be activated until the executed clinical site agreement is filed."
    );
    await expectNoWcagViolations(
      page,
      "Clinical Canvas SiteActivationPanel disabled Explain disclosure"
    );
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/verify-site-activation-explain.png",
      });
    }

    // 3. CanvasErrorAlert Retry, Explain, and SupportDetails on QueriesSection
    await page.evaluate(() => {
      (window as unknown as { __failQueriesOnce?: boolean }).__failQueriesOnce =
        true;
    });
    await gotoHashRoute(
      page,
      `/studies/CADENCE-101?stage=data&section=queries&node=${PARTICIPANT_REF}`
    );

    const errorAlert = page.locator("#study-queries-error");
    await expect(errorAlert).toBeVisible();
    await errorAlert.locator("details.support-details summary").click();
    await expect(errorAlert).toContainText(
      "Upstream Execution gateway read timeout (503)"
    );
    await expectNoWcagViolations(
      page,
      "Clinical Canvas CanvasErrorAlert with SupportDetails"
    );
    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/verify-canvas-error-alert.png",
      });
    }

    await errorAlert.locator('[data-testid="canvas-error-explain"]').click();
    await expect(page.locator("#kernel-explain")).toBeVisible();

    await errorAlert.locator('[data-testid="canvas-error-retry"]').click();
    await expect(errorAlert).toBeHidden();
  });

  test("Live /etmf, BatchUploadDrawer, single-document upload modal, /training, and /ssu pass WCAG 2.2 AA and SC 2.5.3 Label in Name", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.emulateMedia({ reducedMotion: "reduce" });

    // 1. /etmf initial binder & toolbar buttons (Export Zip, Batch Ingestion, Upload Single Document)
    await gotoHashRoute(page, "/etmf");
    await expect(page.locator(".document-manager-layout")).toBeVisible();
    await expect(page.locator(".btn-export-full-binder")).toBeVisible();
    await expect(page.locator(".batch-upload-trigger-btn")).toBeVisible();
    await expectNoWcagViolations(page, "DocumentManagerView (/etmf)");

    // 2. BatchUploadDrawer dropzone & queued file state
    await page.locator(".batch-upload-trigger-btn").click();
    const batchDrawer = page.locator('.batch-drawer[role="dialog"]');
    await expect(batchDrawer).toBeVisible();

    const batchDropzone = batchDrawer.locator(".clinical-dropzone");
    await expect(batchDropzone).toBeVisible();
    await expect(batchDropzone.locator('[role="button"]')).toHaveCount(0);
    await expect(
      batchDropzone.locator(
        "button.clinical-dropzone-browse-btn[type='button']"
      )
    ).toBeVisible();

    await batchDropzone.locator('input[type="file"]').setInputFiles({
      name: "site-101-fda-1572.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("%PDF-1.4\n% Regulatory test document\n"),
    });
    await expect(batchDrawer.locator(".queue-section")).toBeVisible();
    await expectNoWcagViolations(
      page,
      "BatchUploadDrawer with queued file (/etmf)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-batch-dropzone-a11y.png" });
    }

    await batchDrawer.locator(".btn-close-batch-drawer").click();
    await expect(batchDrawer).toBeHidden();

    // 3. Single-document upload modal on /etmf with ClinicalDropzone & selected file
    const zoneWithDocs = page
      .locator(".zone-header")
      .filter({ has: page.locator(".zone-badge") })
      .first();
    await zoneWithDocs.click();

    const sectionWithDocs = page
      .locator(".section-header")
      .filter({ has: page.locator(".section-badge") })
      .first();
    await sectionWithDocs.click();

    const artifactNode = page.locator(".artifact-node").first();
    await expect(artifactNode).toBeVisible();
    await artifactNode.click();

    const uploadSingleBtn = page.locator(".upload-trigger-btn");
    await expect(uploadSingleBtn).toBeEnabled();
    await uploadSingleBtn.click();

    const uploadModal = page.locator(".upload-modal");
    await expect(
      page.locator('[role="dialog"][aria-label="Document Ingestion Modal"]')
    ).toBeVisible();
    await expect(uploadModal).toBeVisible();
    const modalDropzone = uploadModal.locator(".clinical-dropzone");
    await expect(modalDropzone.locator('[role="button"]')).toHaveCount(0);
    await expect(
      modalDropzone.locator(
        "button.clinical-dropzone-browse-btn[type='button']"
      )
    ).toBeVisible();

    await modalDropzone.locator('input[type="file"]').setInputFiles({
      name: "investigator-brochure-v3.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("%PDF-1.4\n% IB v3\n"),
    });

    const selectedDetails = uploadModal.locator(".selected-file-details");
    await expect(selectedDetails).toBeVisible();
    const isHiddenFromAt = await selectedDetails.evaluate(
      (el) => el.closest('[aria-hidden="true"]') !== null
    );
    expect(isHiddenFromAt).toBe(false);
    await expect(
      modalDropzone.locator(
        "button.clinical-dropzone-remove-btn[type='button']"
      )
    ).toBeVisible();
    await expectNoWcagViolations(
      page,
      "Regulated Document Upload Modal with selected file (/etmf)"
    );

    await page.keyboard.press("Escape");
    await expect(uploadModal).toBeHidden();

    // 4. /training default workspace state
    await gotoHashRoute(page, "/training");
    await expect(page.locator("#section-study-training")).toBeVisible();
    await expect(
      page.locator(
        "#section-study-training .header-actions > .btn.btn-secondary"
      )
    ).toBeEnabled();
    await expectNoWcagViolations(
      page,
      "StudyTrainingView default workspace (/training)"
    );

    // 5. /ssu dedicated Study Startup route
    await gotoHashRoute(page, "/ssu");
    await expect(page.locator(".ssu-greenlight-console")).toBeVisible();
    await expectNoWcagViolations(
      page,
      "SsuGreenlightConsole dedicated route (/ssu)"
    );
  });

  test("ConfirmationDialogHost (confirmation + validated input prompt) and ToastHost on /labs pass WCAG 2.2 AA with zero native dialogs", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    await gotoHashRoute(page, "/overview");
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
          url.includes("/api/v1/execution/lab-ranges") &&
          (!init?.method || init.method.toUpperCase() === "GET")
        ) {
          return new Response(
            JSON.stringify([
              {
                id: "rng-alt-01",
                study_id: "STUDY-101",
                test_code: "ALT",
                test_name: "Alanine Aminotransferase",
                loinc_code: "1742-6",
                source_type: "CENTRAL",
                sex: "ALL",
                age_min: 18,
                age_max: 85,
                low_normal: 7,
                high_normal: 56,
                unit: "U/L",
                effective_from: "2026-01-01",
              },
            ]),
            {
              status: 200,
              headers: { "Content-Type": "application/json" },
            }
          );
        }
        if (
          url.includes("/api/v1/execution/lab-ranges/rng-alt-01") &&
          init?.method?.toUpperCase() === "DELETE"
        ) {
          const headers = new Headers(init.headers);
          (window as unknown as Record<string, string>).__lastLabDeleteReason =
            headers.get("X-Change-Reason") ?? "";
          return new Response(JSON.stringify({ ok: true }), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          });
        }
        return origFetch(input, init);
      };
    });

    await gotoHashRoute(page, "/labs");
    const deactivateBtn = page
      .locator("button.btn-outline-danger", { hasText: "Deactivate" })
      .first();
    await expect(deactivateBtn).toBeVisible();

    // 1. Open ConfirmationDialogHost in confirm mode -> Escape restores focus to trigger
    await deactivateBtn.click();
    const alertDialog = page.locator('[role="alertdialog"]');
    await expect(alertDialog).toBeVisible();
    await expect(alertDialog.locator(".modal-title")).toHaveText(
      "Deactivate Reference Range"
    );
    await expectNoWcagViolations(
      page,
      "ConfirmationDialogHost (confirm mode on /labs)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/clinical-confirm-dialog.png" });
    }

    await page.keyboard.press("Escape");
    await expect(alertDialog).toBeHidden();
    await expect(deactivateBtn).toBeFocused();

    // 2. Re-open confirm -> advance directly into prompt mode -> validate empty input -> submit trimmed reason
    await deactivateBtn.click();
    await expect(alertDialog).toBeVisible();
    await alertDialog.locator(".btn-confirm").click();

    await expect(alertDialog).toBeVisible();
    await expect(alertDialog.locator(".modal-title")).toHaveText(
      "21 CFR Part 11 Deactivation Reason"
    );
    const promptInput = alertDialog.locator("#confirm-dialog-prompt-input");
    await expect(promptInput).toBeFocused();

    // Clear input and trigger accessible validation error
    await promptInput.fill("   ");
    await alertDialog.locator(".btn-confirm").click();
    const promptError = alertDialog.locator("#confirm-dialog-prompt-error");
    await expect(promptError).toBeVisible();
    await expect(promptError).toHaveAttribute("role", "alert");
    await expectNoWcagViolations(
      page,
      "ConfirmationDialogHost (prompt validation error state on /labs)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/clinical-prompt-dialog.png" });
    }

    // Enter padded reason and submit via Enter -> resolves trimmed value and pushes ToastHost notification
    await promptInput.fill("  Protocol amendment v2.1 range retirement  ");
    await promptInput.press("Enter");
    await expect(alertDialog).toBeHidden();

    const toastItem = page
      .locator(".toast-host .toast")
      .filter({ hasText: "Reference Range Deactivated" });
    await expect(toastItem).toBeVisible();

    const capturedChangeReason = await page.evaluate(
      () =>
        (window as unknown as Record<string, string>).__lastLabDeleteReason ??
        ""
    );
    expect(capturedChangeReason).toBe(
      "Protocol amendment v2.1 range retirement"
    );

    await expectNoWcagViolations(
      page,
      "ToastHost feedback notification (/labs)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/clinical-toast-host.png" });
    }
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

  test("WCAG 2.1 SC 1.4.10 320x640 reflow keeps scrollWidth <= 320 and card bounds unclipped across Plans, Unavailable, Login, and Forbidden", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 640 });

    async function expectPageWithin320(label: string) {
      const scrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth
      );
      expect(
        scrollWidth,
        `Expected documentElement.scrollWidth <= 320 on ${label}, got ${scrollWidth}`
      ).toBeLessThanOrEqual(320);
    }

    async function expectAllCardsWithin320(selector: string, label: string) {
      const items = page.locator(selector);
      const count = await items.count();
      expect(
        count,
        `Expected >= 1 card for ${selector} on ${label}`
      ).toBeGreaterThan(0);
      for (let i = 0; i < count; i++) {
        const box = await items.nth(i).boundingBox();
        expect(box, `Missing boundingBox for ${selector}[${i}]`).not.toBeNull();
        expect(
          box!.x,
          `Left bound clipped for ${selector}[${i}] on ${label}`
        ).toBeGreaterThanOrEqual(-0.5);
        expect(
          box!.x + box!.width,
          `Right bound overflows 320px for ${selector}[${i}] on ${label}`
        ).toBeLessThanOrEqual(320.5);
      }
    }

    // 1. PlansView.vue (.plans-tiers-grid, .scoping-grid, .guarantees-grid, .families-grid, .benchmark-grid)
    await gotoHashRoute(page, "/plans");
    await expect(page.locator(".plans-view")).toBeVisible();
    await expectPageWithin320("PlansView (/plans)");
    await expectAllCardsWithin320(".plans-tiers-grid > *", "PlansView tiers");
    await expectAllCardsWithin320(".scoping-grid > *", "PlansView scoping");
    await expectAllCardsWithin320(
      ".guarantees-grid > *",
      "PlansView guarantees"
    );
    await expectAllCardsWithin320(".families-grid > *", "PlansView families");
    await expectAllCardsWithin320(".benchmark-grid > *", "PlansView benchmark");
    await expectNoWcagViolations(page, "PlansView at 320x640");

    // 2. UnavailableView.vue
    await gotoHashRoute(page, "/unavailable?screen=safety");
    await expect(page.locator("#section-unavailable")).toBeVisible();
    await expectPageWithin320("UnavailableView (/unavailable?screen=safety)");
    await expectAllCardsWithin320(
      ".unavailable-card, .unavailable-info-card",
      "UnavailableView cards"
    );
    await expectNoWcagViolations(page, "UnavailableView at 320x640");

    // 3. ForbiddenView.vue
    await gotoHashRoute(page, "/forbidden");
    await expect(page.locator("#section-forbidden")).toBeVisible();
    await expectPageWithin320("ForbiddenView (/forbidden)");
    await expectNoWcagViolations(page, "ForbiddenView at 320x640");

    // 4. LoginView.vue (active session + signed-out prompt)
    await gotoHashRoute(page, "/login");
    await expect(page.locator("#section-login")).toBeVisible();
    await expectPageWithin320("LoginView (authenticated)");
    await expectNoWcagViolations(page, "LoginView (authenticated) at 320x640");

    const logoutBtn = page.locator("#section-login .btn-logout");
    if (await logoutBtn.isVisible().catch(() => false)) {
      await logoutBtn.click();
      await expect(page.locator(".btn-login-demo")).toBeVisible();
      await expectPageWithin320("LoginView (signed-out)");
      await expectNoWcagViolations(page, "LoginView (signed-out) at 320x640");
    }
  });

  test("WCAG 2.1 SC 1.4.10 320x640 reflow keeps scrollWidth <= 320 across StudyCanvasView, ParticipantsSection, ParticipantRecord SHA-256, and ProtocolVersionsSection", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 640 });

    async function expectPageWithin320(label: string) {
      const scrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth
      );
      expect(
        scrollWidth,
        `Expected documentElement.scrollWidth <= 320 on ${label}, got ${scrollWidth}`
      ).toBeLessThanOrEqual(320);
    }

    // 1. StudyCanvasView.vue (/studies directory + #create-study-file sr-only width <= 1px)
    await gotoHashRoute(page, "/studies");
    await expect(page.locator("#study-canvas")).toBeVisible();
    const createStudyFile = page.locator("#create-study-file");
    if ((await createStudyFile.count()) > 0) {
      const fileWidth = await createStudyFile.evaluate(
        (el) => el.getBoundingClientRect().width
      );
      expect(fileWidth).toBeLessThanOrEqual(1.5);
    }
    await expectPageWithin320("StudyCanvasView (/studies)");
    await expectNoWcagViolations(page, "StudyCanvasView (/studies) at 320x640");

    const demoStudyCode = "DEMO-01";

    // 2. ParticipantsSection.vue (#participants-site + .scrollable-table-wrapper)
    await gotoHashRoute(
      page,
      `/studies/${encodeURIComponent(demoStudyCode)}?stage=conduct&section=participants`
    );
    await expect(page.locator("#participants-section")).toBeVisible();
    const siteInput = page.locator("#participants-site");
    await expect(siteInput).toBeVisible();
    const siteBox = await siteInput.boundingBox();
    expect(siteBox).not.toBeNull();
    expect(siteBox!.x + siteBox!.width).toBeLessThanOrEqual(320.5);

    await expect(
      page.locator("#participants-section .scrollable-table-wrapper")
    ).toBeVisible();
    await expectPageWithin320("ParticipantsSection");
    await expectNoWcagViolations(page, "ParticipantsSection at 320x640");

    // 3. ParticipantRecord.vue (64-char casebookOutcome.sha256 wrapping)
    const firstParticipantBtn = page
      .locator(".participants-table tbody button.row-select")
      .first();
    await expect(firstParticipantBtn).toBeVisible();
    await firstParticipantBtn.click();
    const openRecordLink = page.locator("#inspector-open-record");
    await expect(openRecordLink).toBeVisible();
    await openRecordLink.click();

    await expect(page.locator("#participant-record")).toBeVisible();
    const exportCasebookBtn = page.locator("#btn-export-casebook");
    await expect(exportCasebookBtn).toBeVisible();
    await exportCasebookBtn.click();
    const digestSummary = page.locator(".casebook-digest-disclosure > summary");
    await expect(digestSummary).toBeVisible();
    await digestSummary.click();
    const shaCode = page.locator('[data-testid="participant-casebook-sha256"]');
    await expect(shaCode).toBeVisible();
    const shaBox = await shaCode.boundingBox();
    expect(shaBox).not.toBeNull();
    expect(shaBox!.x).toBeGreaterThanOrEqual(-0.5);
    expect(shaBox!.x + shaBox!.width).toBeLessThanOrEqual(320.5);
    await expectPageWithin320("ParticipantRecord (with SHA-256 digest)");
    await expectNoWcagViolations(page, "ParticipantRecord at 320x640");

    // 4. ProtocolVersionsSection.vue (.scrollable-table-wrapper)
    await gotoHashRoute(
      page,
      `/studies/${encodeURIComponent(demoStudyCode)}?stage=design&section=protocol-versions`
    );
    await expect(page.locator("#protocol-versions-section")).toBeVisible();
    await expect(
      page.locator("#protocol-versions-section .scrollable-table-wrapper")
    ).toBeVisible();
    await expectPageWithin320("ProtocolVersionsSection");
    await expectNoWcagViolations(page, "ProtocolVersionsSection at 320x640");
  });

  test("SignatureCaptureModal, TicketSignModal, and ReasonModal scroll action controls into 320x568 and 320x256 viewports and pass WCAG 2.2 AA", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";

    async function expectControlScrollableIntoViewport(
      selector: string,
      viewportWidth: number,
      viewportHeight: number,
      label: string
    ) {
      const control = page.locator(selector).first();
      await expect(control).toBeVisible();
      await control.scrollIntoViewIfNeeded();
      const box = await control.boundingBox();
      expect(
        box,
        `Missing boundingBox for ${selector} (${label})`
      ).not.toBeNull();
      expect(
        box!.x,
        `Left edge clipped for ${selector} (${label})`
      ).toBeGreaterThanOrEqual(0);
      expect(
        box!.x + box!.width,
        `Right edge overflows ${viewportWidth}px for ${selector} (${label})`
      ).toBeLessThanOrEqual(viewportWidth);
      expect(
        box!.y,
        `Top edge clipped for ${selector} (${label})`
      ).toBeGreaterThanOrEqual(0);
      expect(
        box!.y + box!.height,
        `Bottom edge overflows ${viewportHeight}px for ${selector} (${label})`
      ).toBeLessThanOrEqual(viewportHeight);
    }

    // 1. SignatureCaptureModal.vue (#btn-cancel-sig & #btn-confirm-sig)
    await page.setViewportSize({ width: 320, height: 568 });
    await gotoHashRoute(page, "/icf-builder");
    const openSigBtn = page.locator("#btn-open-sign-modal");
    await expect(openSigBtn).toBeVisible();
    await openSigBtn.click();

    const sigModal = page.locator("#signature-capture-modal");
    await expect(sigModal).toBeVisible();

    for (const viewportHeight of [568, 256]) {
      await page.setViewportSize({ width: 320, height: viewportHeight });
      await expectControlScrollableIntoViewport(
        "#btn-cancel-sig",
        320,
        viewportHeight,
        `SignatureCaptureModal at 320x${viewportHeight}`
      );
      await expectControlScrollableIntoViewport(
        "#btn-confirm-sig",
        320,
        viewportHeight,
        `SignatureCaptureModal at 320x${viewportHeight}`
      );
      await expectNoWcagViolations(
        page,
        `SignatureCaptureModal at 320x${viewportHeight}`
      );
      if (captureScreenshots && viewportHeight === 256) {
        await page.screenshot({ path: "/tmp/signature-modal-320x256.png" });
      }
    }

    await page.keyboard.press("Escape");
    await expect(sigModal).toBeHidden();

    // 2. TicketSignModal.vue (.btn-sign-action)
    await page.setViewportSize({ width: 320, height: 568 });
    await gotoHashRoute(page, "/tickets");
    await expect(page.locator("#section-tickets-hub")).toBeVisible();

    const inProgressCard = page
      .locator(".ticket-card")
      .filter({ hasText: "Missed Scheduled Blood Draw at Cycle 2 Day 1" })
      .first();
    await expect(inProgressCard).toBeVisible();
    await inProgressCard.click();

    const resolveBtn = page.locator("#btn-open-resolve");
    await expect(resolveBtn).toBeVisible();
    await resolveBtn.click();

    const openTicketSignBtn = page.locator("#btn-open-part11-sign");
    await expect(openTicketSignBtn).toBeVisible();
    await openTicketSignBtn.click();

    const ticketSignDialog = page.locator('.ticket-sign-modal[role="dialog"]');
    await expect(ticketSignDialog).toBeVisible();

    for (const viewportHeight of [568, 256]) {
      await page.setViewportSize({ width: 320, height: viewportHeight });
      await expectControlScrollableIntoViewport(
        ".ticket-sign-modal .btn-sign-action",
        320,
        viewportHeight,
        `TicketSignModal at 320x${viewportHeight}`
      );
      await expectNoWcagViolations(
        page,
        `TicketSignModal at 320x${viewportHeight}`
      );
      if (captureScreenshots && viewportHeight === 256) {
        await page.screenshot({ path: "/tmp/ticket-sign-modal-320x256.png" });
      }
    }

    await page.keyboard.press("Escape");
    await expect(ticketSignDialog).toBeHidden();

    // 3. ReasonModal.vue (#btn-save-change)
    await page.setViewportSize({ width: 320, height: 568 });
    await gotoHashRoute(page, "/econsent-authoring");
    await expect(page.locator("#section-econsent-authoring")).toBeVisible();

    const publishTemplateBtn = page
      .locator("#section-econsent-authoring .btn-publish")
      .first();
    if ((await publishTemplateBtn.count()) > 0) {
      await publishTemplateBtn.click();
    } else {
      await page.locator("#btn-create-template").click();
      await page.locator("#section-econsent-authoring .btn-save").click();
    }

    const reasonModal = page.locator("#reason-modal");
    await expect(reasonModal).toBeVisible();

    for (const viewportHeight of [568, 256]) {
      await page.setViewportSize({ width: 320, height: viewportHeight });
      await expectControlScrollableIntoViewport(
        "#btn-save-change",
        320,
        viewportHeight,
        `ReasonModal at 320x${viewportHeight}`
      );
      await expectNoWcagViolations(
        page,
        `ReasonModal at 320x${viewportHeight}`
      );
      if (captureScreenshots && viewportHeight === 256) {
        await page.screenshot({ path: "/tmp/reason-modal-320x256.png" });
      }
    }

    await page.keyboard.press("Escape");
    await expect(reasonModal).toBeHidden();
  });

  test("WCAG 2.1 SC 1.4.10 320x640 reflow keeps scrollWidth <= 320 across /etmf, /knowledge, and Study Canvas ExportsSection SHA-256", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 320, height: 640 });

    async function expectPageWithin320(label: string) {
      const scrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth
      );
      expect(
        scrollWidth,
        `Expected documentElement.scrollWidth <= 320 on ${label}, got ${scrollWidth}`
      ).toBeLessThanOrEqual(320);
    }

    async function expectElementWithin320(selector: string, label: string) {
      const el = page.locator(selector).first();
      await expect(el).toBeVisible();
      const box = await el.boundingBox();
      expect(
        box,
        `Missing boundingBox for ${selector} on ${label}`
      ).not.toBeNull();
      expect(
        box!.x,
        `Left bound clipped for ${selector} on ${label}`
      ).toBeGreaterThanOrEqual(0);
      expect(
        box!.x + box!.width,
        `Right bound overflows 320px for ${selector} on ${label}`
      ).toBeLessThanOrEqual(320);
      return box!;
    }

    // 1. /etmf (DocumentManagerView.vue)
    await gotoHashRoute(page, "/etmf");
    await expect(page.locator(".document-manager-layout")).toBeVisible();

    const sidebarBox = await expectElementWithin320(
      ".sidebar-binder-tree",
      "/etmf"
    );
    const contentBox = await expectElementWithin320(
      ".document-content-area",
      "/etmf"
    );
    expect(sidebarBox.y).toBeLessThan(contentBox.y);

    await expectElementWithin320("#btn-etmf-tab-qc-queue", "/etmf");
    await expectElementWithin320("#etmf-scope-bar", "/etmf");
    await expectPageWithin320("DocumentManagerView (/etmf)");
    await expectNoWcagViolations(
      page,
      "DocumentManagerView (/etmf) at 320x640"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-reflow-320x640.png" });
    }

    // 2. /knowledge (KnowledgeView.vue)
    await gotoHashRoute(page, "/knowledge");
    await expect(page.locator(".knowledge-layout")).toBeVisible();

    const firstArticleCard = page.locator(".article-card").first();
    await expect(firstArticleCard).toBeVisible();
    if (
      !(await page
        .locator(".breadcrumbs")
        .isVisible()
        .catch(() => false))
    ) {
      await firstArticleCard.click();
    }

    await expectElementWithin320(".knowledge-sidebar", "/knowledge");
    await expectElementWithin320(".article-list-panel", "/knowledge");
    await expectElementWithin320(".article-reader-pane", "/knowledge");
    await expectElementWithin320(".breadcrumbs", "/knowledge");
    await expectElementWithin320(".reader-badges", "/knowledge");
    await expectPageWithin320("KnowledgeView (/knowledge)");
    await expectNoWcagViolations(page, "KnowledgeView (/knowledge) at 320x640");
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/knowledge-reflow-320x640.png" });
    }

    // 3. ExportsSection.vue (/studies/DEMO-01?stage=submit&section=exports)
    await gotoHashRoute(page, "/studies/DEMO-01?stage=submit&section=exports");
    await expect(page.locator("#study-exports-section")).toBeVisible();

    const streamExportBtn = page.locator("#btn-stream-study-export");
    await expect(streamExportBtn).toBeVisible();
    await streamExportBtn.click();

    await expectElementWithin320(
      '[data-testid="study-exports-sha256"]',
      "ExportsSection (/studies/DEMO-01?stage=submit&section=exports)"
    );
    await expectPageWithin320(
      "ExportsSection (/studies/DEMO-01?stage=submit&section=exports)"
    );
    await expectNoWcagViolations(page, "ExportsSection at 320x640");
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/exports-sha256-320x640.png" });
    }
  });

  test("Operational eTMF hub (/etmf) passes WCAG 2.2 AA, ClinicalTabs keyboard navigation, TmfBinderTree Home/End & filter focus clamping, DocumentGrid (0,0) roving focus & button keys, and CraQcQueue focus fallback", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.emulateMedia({ reducedMotion: "reduce" });

    await gotoHashRoute(page, "/etmf");
    await expect(page.locator(".document-manager-layout")).toBeVisible();
    await expectNoWcagViolations(page, "DocumentManagerView initial (/etmf)");

    // 1. ClinicalTabs bar #1: DocumentManagerView workspace tabs
    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="eTMF workspace views"]',
      "DocumentManagerView workspace tabs (/etmf)"
    );

    // 2. TmfBinderTree: zero nested buttons inside role="treeitem", dynamic aria-selected, Home/End, and filter focus clamping
    const tree = page.locator('[role="tree"]');
    await expect(tree).toBeVisible();
    await expect(tree.locator('[role="treeitem"] button')).toHaveCount(0);
    await expect(tree.locator('[role="treeitem"][tabindex="0"]')).toHaveCount(
      1
    );

    const treeItems = tree.locator('[role="treeitem"]');
    const initialTreeCount = await treeItems.count();
    expect(initialTreeCount).toBeGreaterThanOrEqual(2);

    await treeItems.first().focus();
    await page.keyboard.press("End");
    await expect(treeItems.nth(initialTreeCount - 1)).toBeFocused();
    await expect(treeItems.nth(initialTreeCount - 1)).toHaveAttribute(
      "tabindex",
      "0"
    );
    await page.keyboard.press("Home");
    await expect(treeItems.first()).toBeFocused();
    await expect(treeItems.first()).toHaveAttribute("tabindex", "0");

    // Filter binder tree and verify roving tabindex="0" is preserved on visible items
    const binderFilterInput = page.locator("#tmf-binder-search-input");
    if ((await binderFilterInput.count()) > 0) {
      await binderFilterInput.fill("05");
      await expect(tree.locator('[role="treeitem"][tabindex="0"]')).toHaveCount(
        1
      );
      await binderFilterInput.fill("");
      await expect(tree.locator('[role="treeitem"][tabindex="0"]')).toHaveCount(
        1
      );
    }

    // Expand a zone -> section -> artifact with documents (only clicking collapsed containers) and verify aria-selected="true"
    const zoneWithDocs = page
      .locator(".zone-header")
      .filter({ has: page.locator(".zone-badge") })
      .first();
    if ((await zoneWithDocs.getAttribute("aria-expanded")) !== "true") {
      await zoneWithDocs.click();
    }

    const sectionWithDocs = page
      .locator(".section-header")
      .filter({ has: page.locator(".section-badge") })
      .first();
    if ((await sectionWithDocs.getAttribute("aria-expanded")) !== "true") {
      await sectionWithDocs.click();
    }

    const artifactWithDocs = page
      .locator(".artifact-node")
      .filter({ has: page.locator(".artifact-badge") })
      .first();
    await artifactWithDocs.click();
    await expect(artifactWithDocs).toHaveAttribute("aria-selected", "true");

    // 3. DocumentGrid: aria-colcount="8", 1 tabindex="0" cell after load, Enter on Inspect, Space on Preview
    const docGrid = page.locator('.documents-table[role="grid"]');
    await expect(docGrid).toBeVisible();
    await expect(docGrid).toHaveAttribute("aria-colcount", "8");
    await expect(
      docGrid.locator('[role="gridcell"][tabindex="0"]')
    ).toHaveCount(1);
    await expectNoWcagViolations(
      page,
      "DocumentGrid populated with artifact documents (/etmf)"
    );

    const inspectBtn = docGrid.locator(".btn-inspect-doc").first();
    await inspectBtn.focus();
    await page.keyboard.press("Enter");
    const drawerDialog = page.locator('.metadata-drawer[role="dialog"]');
    await expect(drawerDialog).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(drawerDialog).toBeHidden();

    const previewBtn = docGrid.locator(".btn-preview-doc").first();
    await previewBtn.focus();
    await page.keyboard.press(" ");
    const pdfDialog = page.locator('.pdf-preview-modal[role="dialog"]');
    await expect(pdfDialog).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(pdfDialog).toBeHidden();

    // 4. CraQcQueue: stage a document if queue is empty, adjudicate via keyboard/modal, verify focus stays off BODY
    await page.evaluate(async () => {
      await fetch("/cadence-clinical/api/v1/etmf/intelligence/stage-qc", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          study_id: "CADENCE-101",
          site_id: "SITE-101",
          filename: "site-101-fda-1572-staged.pdf",
          artifact_code: "05.02.01",
          artifact_type: "FDA Form 1572",
        }),
      });
    });

    await page.locator("#btn-etmf-tab-qc-queue").click();
    await expect(page.locator(".qc-queue-container")).toBeVisible();
    await page.locator("#btn-refresh-qc-queue").click();

    const inspectQcBtn = page.locator(".btn-inspect-qc").first();
    await expect(inspectQcBtn).toBeVisible();
    await expectNoWcagViolations(
      page,
      "CraQcQueue with staged technical QC item (/etmf)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-hub-a11y.png" });
    }

    await inspectQcBtn.focus();
    await inspectQcBtn.click();
    const qcModal = page.locator(
      '[role="dialog"][aria-labelledby="qc-inspect-modal-title"]'
    );
    await expect(qcModal).toBeVisible();
    await expectNoWcagViolations(
      page,
      "CraQcQueue single-item review modal (/etmf)"
    );

    await qcModal
      .locator("#review-reason-input")
      .fill("Verified regulatory taxonomy classification against source PDF");
    await qcModal.locator(".btn-accept-qc").click();
    await expect(qcModal).toBeHidden();

    const activeAfterQc = await page.evaluate(() => ({
      tag: document.activeElement?.tagName ?? null,
      id: document.activeElement?.id ?? null,
    }));
    expect(
      activeAfterQc.tag,
      "Completing CraQcQueue review must not drop focus to BODY"
    ).not.toBe("BODY");
    expect(activeAfterQc.id).toBe("btn-refresh-qc-queue");
  });

  test("Tickets hub (/tickets) passes WCAG 2.2 AA, native button cards, labeled filters, ClinicalTabs keyboard navigation, and Part 11 sign-off focus retention", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.emulateMedia({ reducedMotion: "reduce" });

    await gotoHashRoute(page, "/tickets");
    await expect(page.locator("#section-tickets-hub")).toBeVisible();
    await expect(page.locator("button.ticket-card").first()).toBeVisible();
    await expectNoWcagViolations(page, "TicketsView Kanban board (/tickets)");

    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/tickets-hub-a11y.png" });
    }

    // Verify interactive KRI stat cards and Kanban ticket cards are native <button type="button">
    const statCardCount = await page
      .locator("#section-tickets-hub button.stat-card[type='button']")
      .count();
    expect(statCardCount).toBe(3);
    const nonButtonTicketCards = await page
      .locator("#section-tickets-hub .ticket-card:not(button)")
      .count();
    expect(nonButtonTicketCards).toBe(0);

    // ClinicalTabs bar #2: TicketsView Kanban / Table view switcher
    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="Tickets workspace view mode"]',
      "TicketsView view mode tabs (/tickets)"
    );

    // Switch to Table view to audit WCAG 2.2 AA on list table, then return to Kanban
    const tableTab = page
      .locator(
        '[role="tablist"][aria-label="Tickets workspace view mode"] [role="tab"]'
      )
      .nth(1);
    await tableTab.click();
    await expectNoWcagViolations(page, "TicketsView List table (/tickets)");
    await page
      .locator(
        '[role="tablist"][aria-label="Tickets workspace view mode"] [role="tab"]'
      )
      .first()
      .click();

    // Activate a Kanban ticket card via keyboard Enter -> opens TicketDetailDrawer
    const resolvedOrFirstCard = page
      .locator("button.ticket-card")
      .filter({ hasText: /RESOLVED/i })
      .first();
    const targetCard =
      (await resolvedOrFirstCard.count()) > 0
        ? resolvedOrFirstCard
        : page.locator("button.ticket-card").first();

    await targetCard.focus();
    await page.keyboard.press("Enter");

    const drawer = page.locator(
      '.drawer-panel[role="dialog"][aria-labelledby="drawer-ticket-title"]'
    );
    await expect(drawer).toBeVisible();
    await expectNoWcagViolations(page, "TicketDetailDrawer open (/tickets)");

    // ClinicalTabs bar #3: TicketDetailDrawer tabs
    await expectClinicalTabsKeyboardNavigation(
      page,
      '.drawer-panel[aria-labelledby="drawer-ticket-title"] [role="tablist"][aria-label="Ticket detail sections"]',
      "TicketDetailDrawer section tabs (/tickets)"
    );

    // Ensure ticket is in RESOLVED state so Part 11 eSign button is available
    const signTrigger = drawer.locator("#btn-open-part11-sign");
    if ((await signTrigger.count()) === 0) {
      const startWorkBtn = drawer
        .locator("button", { hasText: /Start Work/i })
        .first();
      if (await startWorkBtn.isVisible().catch(() => false)) {
        await drawer
          .locator("#transition-reason-input")
          .fill("Initiating clinical triage review");
        await startWorkBtn.click();
      }
      const openResolveBtn = drawer.locator("#btn-open-resolve");
      if (await openResolveBtn.isVisible().catch(() => false)) {
        await openResolveBtn.click();
        await drawer
          .locator("#resolve-rca-summary")
          .fill("Verified protocol workflow alignment and updated site SOP.");
        await drawer
          .locator("#resolve-change-reason")
          .fill("Root-cause corrective action verified");
        await drawer.locator("#btn-confirm-resolve").click();
      }
    }

    await expect(signTrigger).toBeVisible();
    await signTrigger.focus();
    await signTrigger.click();

    const signModal = page.locator('.ticket-sign-modal[role="dialog"]');
    await expect(signModal).toBeVisible();
    await expectNoWcagViolations(
      page,
      "TicketSignModal 21 CFR Part 11 dialog (/tickets)"
    );

    await signModal
      .locator("#signature-reason")
      .fill("Authoritative clinical verification of issue closure");
    await signModal.locator("#signature-password").fill("DemoPass!2026");
    await signModal.locator(".btn-sign-action").click();

    await expect(signModal).toBeHidden();
    await expect(signTrigger).toHaveCount(0);

    // Verify focus stayed inside the parent TicketDetailDrawer instead of dropping to BODY
    const activeAfterSign = await page.evaluate(() => {
      const active = document.activeElement;
      return {
        tag: active?.tagName ?? null,
        id: active?.id ?? null,
        insideDrawer: Boolean(
          active?.closest(
            '.drawer-panel[aria-labelledby="drawer-ticket-title"]'
          )
        ),
      };
    });
    expect(
      activeAfterSign.tag,
      "Completing TicketSignModal sign-off must not drop focus to BODY"
    ).not.toBe("BODY");
    expect(activeAfterSign.insideDrawer).toBe(true);
  });

  test("Knowledge (/knowledge), CTMS (/ctms), SSU (/ssu), Training (/training), and Trial Onboarding (/onboarding/trial) pass WCAG 2.2 AA, 24px chip targets, native buttons, and ClinicalTabs keyboard navigation", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.emulateMedia({ reducedMotion: "reduce" });

    // 1. /knowledge: labeled search, 24px .tag-chip targets, native <button> SOP & stat cards, and ClinicalTabs bar #4
    await gotoHashRoute(page, "/knowledge");
    await expect(page.locator("#section-knowledge-hub")).toBeVisible();
    await expect(page.locator("button.article-card").first()).toBeVisible();
    await expectNoWcagViolations(
      page,
      "KnowledgeView SOP repository (/knowledge)"
    );

    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/knowledge-hub-a11y.png" });
    }

    await expect(
      page.locator("#section-knowledge-hub .article-card:not(button)")
    ).toHaveCount(0);
    await expect(
      page.locator("#section-knowledge-hub .stat-card:not(button)")
    ).toHaveCount(0);

    const tagChips = page.locator(".tag-chips-scroll .tag-chip");
    const chipCount = await tagChips.count();
    expect(chipCount).toBeGreaterThan(0);
    for (let i = 0; i < Math.min(chipCount, 6); i++) {
      const box = await tagChips.nth(i).boundingBox();
      expect(box).not.toBeNull();
      expect(
        box!.height,
        `Expected .tag-chip[${i}] height >= 24px (WCAG 2.2 SC 2.5.8)`
      ).toBeGreaterThanOrEqual(24);
      expect(
        box!.width,
        `Expected .tag-chip[${i}] width >= 24px (WCAG 2.2 SC 2.5.8)`
      ).toBeGreaterThanOrEqual(24);
    }

    // Activate first SOP card with keyboard Enter and test SOP reader ClinicalTabs (#4)
    const firstArticleBtn = page.locator("button.article-card").first();
    await firstArticleBtn.focus();
    await page.keyboard.press("Enter");

    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="SOP document inspection tabs"]',
      "KnowledgeView SOP reader tabs (/knowledge)"
    );
    await expectNoWcagViolations(
      page,
      "KnowledgeView SOP reader panel (/knowledge)"
    );

    // 2. /ctms: ClinicalTabs bar #5 (CtmsView workspace tabs)
    await gotoHashRoute(page, "/ctms");
    await expect(page.locator("#section-ctms")).toBeVisible();
    await expectClinicalTabsKeyboardNavigation(
      page,
      '#section-ctms [role="tablist"]',
      "CtmsView workspace tabs (/ctms)"
    );

    // 3. /ssu: ClinicalTabs bar #6 (SsuGreenlightConsole view mode tabs)
    await gotoHashRoute(page, "/ssu");
    await expect(page.locator(".ssu-greenlight-console")).toBeVisible();
    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="Study startup view mode"]',
      "SsuGreenlightConsole view mode tabs (/ssu)"
    );
    await expectNoWcagViolations(
      page,
      "SsuGreenlightConsole after keyboard tab navigation (/ssu)"
    );

    // 4. /training: native <button> .module-item controls and ClinicalTabs bar #7 (ProtocolTrainingConsole)
    await gotoHashRoute(page, "/training");
    await expect(page.locator("#section-study-training")).toBeVisible();
    await expect(
      page
        .locator("#section-study-training button.module-item[type='button']")
        .first()
    ).toBeVisible();
    await expect(
      page.locator("#section-study-training .module-item:not(button)")
    ).toHaveCount(0);

    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="Protocol training workspace sections"]',
      "ProtocolTrainingConsole tabs (/training)"
    );
    await expectNoWcagViolations(
      page,
      "ProtocolTrainingConsole after keyboard tab navigation (/training)"
    );

    // 5. /onboarding/trial: TrialSetupView WCAG 2.2 AA gate
    await gotoHashRoute(page, "/onboarding/trial");
    await expect(page.locator("#section-trial-setup")).toBeVisible();
    await expect(page.locator("#input-organization-name")).toBeVisible();
    await expect(page.locator("#select-residency")).toBeVisible();
    await expect(page.locator("#select-trial-tier")).toBeVisible();
    await expectNoWcagViolations(page, "TrialSetupView (/onboarding/trial)");

    // 6. StudyCanvasView embedded Start-stage ClinicalTabs (documents and training)
    await gotoHashRoute(page, "/studies/DEMO-01?stage=start&section=documents");
    await expect(page.locator(".document-manager-layout")).toBeVisible();
    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="eTMF workspace views"]',
      "StudyCanvasView documents tabs"
    );

    await gotoHashRoute(page, "/studies/DEMO-01?stage=start&section=training");
    await expect(page.locator("#section-study-training")).toBeVisible();
    await expectClinicalTabsKeyboardNavigation(
      page,
      '[role="tablist"][aria-label="Protocol training workspace sections"]',
      "StudyCanvasView training tabs"
    );
  });

  test("ClinicalTabs overflow containment at 320x640 and 768x1024 keeps 0px horizontal overflow on /ctms and /ssu, preserves dark-theme contrast, and keeps #ticket-drawer-tab-attachments visible on ArrowRight", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    for (const vp of [
      { width: 320, height: 640 },
      { width: 768, height: 1024 },
    ]) {
      await page.setViewportSize(vp);

      // 1. /ctms: 0px horizontal overflow, >= 44px tab triggers, light & dark contrast
      await gotoHashRoute(page, "/ctms");
      await expect(page.locator("#section-ctms")).toBeVisible();
      const ctmsScrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth
      );
      expect(
        ctmsScrollWidth,
        `Expected 0px horizontal overflow on /ctms at ${vp.width}x${vp.height}, got scrollWidth=${ctmsScrollWidth}`
      ).toBeLessThanOrEqual(vp.width);

      await expectClinicalTabsKeyboardNavigation(
        page,
        '#section-ctms [role="tablist"]',
        `CtmsView tabs at ${vp.width}x${vp.height}`
      );
      if (captureScreenshots && vp.width === 320) {
        await page.screenshot({ path: "/tmp/ctms-tabs-320x640.png" });
      }

      // Verify dark-theme contrast on /ctms as well
      await page.evaluate(() =>
        document.documentElement.setAttribute("data-theme", "dark")
      );
      await expectClinicalTabsKeyboardNavigation(
        page,
        '#section-ctms [role="tablist"]',
        `CtmsView tabs dark theme at ${vp.width}x${vp.height}`
      );
      await page.evaluate(() =>
        document.documentElement.setAttribute("data-theme", "light")
      );

      // 2. /ssu: 0px horizontal overflow, >= 44px tab triggers, light & dark contrast
      await gotoHashRoute(page, "/ssu");
      await expect(page.locator(".ssu-greenlight-console")).toBeVisible();
      const ssuScrollWidth = await page.evaluate(
        () => document.documentElement.scrollWidth
      );
      expect(
        ssuScrollWidth,
        `Expected 0px horizontal overflow on /ssu at ${vp.width}x${vp.height}, got scrollWidth=${ssuScrollWidth}`
      ).toBeLessThanOrEqual(vp.width);

      await expectClinicalTabsKeyboardNavigation(
        page,
        '[role="tablist"][aria-label="Study startup view mode"]',
        `SsuGreenlightConsole tabs at ${vp.width}x${vp.height}`
      );
      if (captureScreenshots && vp.width === 320) {
        await page.screenshot({ path: "/tmp/ssu-tabs-320x640.png" });
      }

      // 3. /tickets -> TicketDetailDrawer: ArrowRight keeps #ticket-drawer-tab-attachments fully visible
      await gotoHashRoute(page, "/tickets");
      await expect(page.locator("button.ticket-card").first()).toBeVisible();
      await page.locator("button.ticket-card").first().click();

      const drawerTablist = page.locator(
        '.drawer-panel[aria-labelledby="drawer-ticket-title"] [role="tablist"][aria-label="Ticket detail sections"]'
      );
      await expect(drawerTablist).toBeVisible();

      const overviewTab = drawerTablist.locator("#ticket-drawer-tab-overview");
      const attachmentsTab = drawerTablist.locator(
        "#ticket-drawer-tab-attachments"
      );
      await overviewTab.focus();
      await page.keyboard.press("Home");
      await expect(overviewTab).toHaveAttribute("aria-selected", "true");

      // Step through tabs with ArrowRight until reaching #ticket-drawer-tab-attachments (index 2)
      await page.keyboard.press("ArrowRight");
      await page.keyboard.press("ArrowRight");

      await expect(attachmentsTab).toBeFocused();
      await expect(attachmentsTab).toHaveAttribute("aria-selected", "true");

      const attachmentBounds = await attachmentsTab.evaluate((el) => {
        const list = el.closest('[role="tablist"]')!;
        const tabRect = el.getBoundingClientRect();
        const listRect = list.getBoundingClientRect();
        return {
          tabLeft: tabRect.left,
          tabRight: tabRect.right,
          tabHeight: tabRect.height,
          listLeft: listRect.left,
          listRight: listRect.right,
          pageScrollWidth: document.documentElement.scrollWidth,
        };
      });

      expect(attachmentBounds.tabHeight).toBeGreaterThanOrEqual(44);
      expect(
        attachmentBounds.tabLeft,
        `#ticket-drawer-tab-attachments clipped on left at ${vp.width}x${vp.height}`
      ).toBeGreaterThanOrEqual(attachmentBounds.listLeft - 1);
      expect(
        attachmentBounds.tabRight,
        `#ticket-drawer-tab-attachments clipped on right at ${vp.width}x${vp.height}`
      ).toBeLessThanOrEqual(attachmentBounds.listRight + 1);
      expect(attachmentBounds.pageScrollWidth).toBeLessThanOrEqual(vp.width);

      if (captureScreenshots && vp.width === 320) {
        await page.screenshot({
          path: "/tmp/ticket-drawer-attachments-tab-320x640.png",
        });
      }

      await page.keyboard.press("Escape");
    }
  });

  test("Dual collapsible Clinical Canvas rails, single-row compact shell header, contained clinical tables, and store-driven inspector focus/scroll pass layout and WCAG 2.2 AA gates across light and dark themes", async ({
    page,
  }) => {
    test.setTimeout(90000);
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    async function expectNoWcagLightAndDark(label: string) {
      await page.evaluate(() => {
        document.documentElement.removeAttribute("data-theme");
      });
      await expectNoWcagViolations(page, `${label} (light)`);
      await page.evaluate(() => {
        document.documentElement.setAttribute("data-theme", "dark");
      });
      await expectNoWcagViolations(page, `${label} (dark)`);
      await page.evaluate(() => {
        document.documentElement.removeAttribute("data-theme");
      });
    }

    // Boot demo session and seed a superseded eCRF signature + multi-column study locks
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(
      page,
      "/studies/DEMO-01?stage=conduct&section=participants"
    );
    await expect(page.locator("#study-canvas")).toBeVisible();
    await expect(page.locator("#participants-section")).toBeVisible();

    await page.evaluate(async () => {
      const formId = "00000000-0000-0000-0000-000000002001";
      const obsRef = "observation:00000000-0000-0000-0000-000000000001";
      await fetch(
        `/cadence-clinical/api/v1/execution/forms/${formId}/signatures`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Cadence-Reason-For-Change":
              "Investigator baseline sign-off prior to source amendment",
          },
          body: JSON.stringify({ meaning: "approved" }),
        }
      );
      const obsResp = await fetch(
        `/cadence-clinical/api/v1/kernel/observations/${encodeURIComponent(obsRef)}`
      );
      const obsJson = await obsResp.json();
      await fetch(
        `/cadence-clinical/api/v1/kernel/observations/${encodeURIComponent(obsRef)}/corrections`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Cadence-Reason-For-Change":
              "Source chart verification correction superseding prior signature",
          },
          body: JSON.stringify({
            expected_version: obsJson.version ?? 1,
            value: {
              type: "quantity",
              value: "126",
              unit: "mm[Hg]",
              comparator: null,
            },
          }),
        }
      );

      const win = window as unknown as {
        __origFetchForLayoutSpec?: typeof window.fetch;
        __mockExpiredEntitlements?: boolean;
        fetch: typeof window.fetch;
      };
      if (!win.__origFetchForLayoutSpec) {
        const baseFetch = window.fetch.bind(window);
        win.__origFetchForLayoutSpec = baseFetch;
        win.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
          const url =
            typeof input === "string"
              ? input
              : input instanceof URL
                ? input.href
                : input.url;
          const method = (
            init?.method || (input instanceof Request ? input.method : "GET")
          ).toUpperCase();

          if (
            method === "GET" &&
            win.__mockExpiredEntitlements &&
            url.includes("/api/v1/kernel/entitlements")
          ) {
            return new Response(
              JSON.stringify({
                tenant_ref: "tenant:00000000-0000-0000-0000-000000000010",
                tenant_id: "00000000-0000-0000-0000-000000000010",
                subscription_ref:
                  "subscription:00000000-0000-0000-0000-000000000011",
                tier: "Complete",
                status: "Expired",
                started_at: "2026-05-01T00:00:00Z",
                trial_ends_at: "2026-06-01T00:00:00Z",
                is_read_only: true,
                product_families: ["edc", "ctms", "etmf", "rtsm"],
                baseline_capabilities: [
                  "audit_trail",
                  "electronic_signatures",
                  "authorization",
                  "record_export",
                ],
              }),
              {
                status: 200,
                headers: { "Content-Type": "application/json" },
              }
            );
          }

          if (
            method === "GET" &&
            /\/api\/v1\/kernel\/studies\/[^/]+\/locks(?:\?|$)/.test(url)
          ) {
            const now = "2026-08-18T14:30:00Z";
            return new Response(
              JSON.stringify({
                study_ref: "study:00000000-0000-0000-0000-000000000001",
                study_code: "DEMO-01",
                valid_at: now,
                known_at: now,
                locks: [
                  {
                    ref: "assertion:01960000-0000-7000-8000-000000000501",
                    study_ref: "study:00000000-0000-0000-0000-000000000001",
                    scope_ref:
                      "enrollment:00000000-0000-0000-0000-000000001001",
                    scope_kind: "enrollment",
                    subject_code: "SUBJ-1001",
                    kind: "Frozen",
                    attestation_ref:
                      "attestation:01960000-0000-7000-8000-000000000099",
                    attestation_meaning: "Data Management Freeze Sign-off",
                    locked_at: now,
                    locked_by: "usr-dm-lead-01",
                    reason_for_change:
                      "Interim database freeze pending medical monitor safety adjudication",
                    released: false,
                    release_ref: null,
                    released_at: null,
                    released_by: null,
                    release_reason: null,
                    masked: false,
                    mask: null,
                  },
                  {
                    ref: "assertion:01960000-0000-7000-8000-000000000502",
                    study_ref: "study:00000000-0000-0000-0000-000000000001",
                    scope_ref: "site:00000000-0000-0000-0000-000000000202",
                    scope_kind: "site",
                    subject_code: null,
                    kind: "Locked",
                    attestation_ref: null,
                    attestation_meaning: null,
                    locked_at: now,
                    locked_by: null,
                    reason_for_change: null,
                    released: false,
                    release_ref: null,
                    released_at: null,
                    released_by: null,
                    release_reason: null,
                    masked: true,
                    mask: {
                      reason: "Unblinded site lock metadata restricted",
                      policy: "blinded-monitor-mask",
                    },
                  },
                ],
              }),
              {
                status: 200,
                headers: { "Content-Type": "application/json" },
              }
            );
          }

          return baseFetch(input, init);
        };
      }
    });

    // 1. 1024x768 open form: auto-collapsed left navigator, workspace >= 640px, card > 600px, sticky bars <= 108px, and superseded signed-values scroll containment
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(
      page,
      "/studies/DEMO-01?stage=conduct&section=participants&participant=SUBJ-1001&form=00000000-0000-0000-0000-000000002001"
    );
    await expect(page.locator("#canvas-form-workspace")).toBeVisible();
    await expect(page.locator("#ecrf-form-instance")).toBeVisible();
    await expect(
      page.locator('[data-testid="ecrf-signed-values-scroll"]')
    ).toBeVisible();

    const navAside = page.locator("#canvas-navigator");
    const navToggle = page.locator("#btn-toggle-navigator");
    const inspectorToggle = page.locator("#btn-toggle-inspector");

    await expect(navAside).toHaveAttribute("data-collapsed", "true");
    await expect(navToggle).toHaveAttribute("aria-expanded", "false");
    await expect(navToggle).toHaveAttribute(
      "aria-controls",
      "canvas-navigator-body"
    );
    await expectVisibleBoxesAtLeast24px(
      page,
      "#btn-toggle-navigator, #btn-toggle-inspector",
      "StudyCanvasView rail toggles at 1024x768"
    );

    const navFocusMetrics = await focusWithKeyboardAndMeasureDualRing(
      page,
      "#btn-toggle-navigator"
    );
    expect(navFocusMetrics.matchesFocusVisible).toBe(true);
    expect(navFocusMetrics.parentContrast).toBeGreaterThanOrEqual(3);

    const openFormMetrics = await page.evaluate(() => {
      const headerRect = document
        .querySelector(".app-header")!
        .getBoundingClientRect();
      const topbarRect = document
        .querySelector(".topbar")!
        .getBoundingClientRect();
      const workspaceRect = document
        .getElementById("canvas-workspace")!
        .getBoundingClientRect();
      const cardRect = document
        .getElementById("ecrf-form-instance")!
        .getBoundingClientRect();
      return {
        stickyHeight: headerRect.height + topbarRect.height,
        workspaceWidth: workspaceRect.width,
        cardWidth: cardRect.width,
        scrollWidth: document.documentElement.scrollWidth,
      };
    });
    expect(openFormMetrics.stickyHeight).toBeLessThanOrEqual(108);
    expect(openFormMetrics.workspaceWidth).toBeGreaterThanOrEqual(640);
    expect(openFormMetrics.cardWidth).toBeGreaterThan(600);
    expect(openFormMetrics.scrollWidth).toBeLessThanOrEqual(1024);

    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/verify-1024x768-open-form-dual-rails.png",
      });
    }

    // Verify manual navigator expand/collapse override via keyboard
    await navToggle.focus();
    await page.keyboard.press("Enter");
    await expect(navAside).toHaveAttribute("data-collapsed", "false");
    await expect(navToggle).toHaveAttribute("aria-expanded", "true");
    await page.keyboard.press("Enter");
    await expect(navAside).toHaveAttribute("data-collapsed", "true");
    await expect(navToggle).toHaveAttribute("aria-expanded", "false");

    await expectNoWcagLightAndDark(
      "Open eCRF form on StudyCanvasView at 1024x768"
    );

    // Verify signed-values table containment across 320x640, 768x1024, 1024x768, 1280x800, and 1440x900
    for (const vp of [
      { width: 320, height: 640 },
      { width: 768, height: 1024 },
      { width: 1024, height: 768 },
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
    ]) {
      await page.setViewportSize(vp);
      const signedBounds = await page.evaluate((maxW) => {
        const ws = document
          .getElementById("canvas-workspace")!
          .getBoundingClientRect();
        const region = document
          .querySelector('[data-testid="ecrf-signed-values-scroll"]')!
          .getBoundingClientRect();
        const culprits = Array.from(document.querySelectorAll("*"))
          .map((el) => {
            const r = el.getBoundingClientRect();
            return {
              sel: `${el.tagName.toLowerCase()}${el.id ? "#" + el.id : ""}${el.className && typeof el.className === "string" ? "." + el.className.trim().split(/\s+/).join(".") : ""}`,
              right: Math.round(r.right),
              width: Math.round(r.width),
            };
          })
          .filter((item) => item.right > maxW + 1)
          .slice(0, 8);
        return {
          wsLeft: ws.x,
          wsRight: ws.x + ws.width,
          regionLeft: region.x,
          regionRight: region.x + region.width,
          scrollWidth: document.documentElement.scrollWidth,
          culprits,
        };
      }, vp.width);
      expect(signedBounds.regionLeft).toBeGreaterThanOrEqual(
        signedBounds.wsLeft - 1
      );
      expect(signedBounds.regionRight).toBeLessThanOrEqual(
        signedBounds.wsRight + 1
      );
      expect(
        signedBounds.scrollWidth,
        `Overflow at ${vp.width}px: ${JSON.stringify(signedBounds.culprits)}`
      ).toBeLessThanOrEqual(vp.width);
    }

    // 2. Single-row .app-header, compact secondary menu disclosure (<= 1280px vs 1440px), sticky offsets, and expired trial badge
    for (const vp of [
      { width: 1024, height: 768, compactMenu: true },
      { width: 1280, height: 800, compactMenu: true },
      { width: 1440, height: 900, compactMenu: false },
    ]) {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await gotoHashRoute(
        page,
        "/studies/DEMO-01?stage=conduct&section=participants"
      );
      await expect(page.locator("#participants-section")).toBeVisible();
      await page.evaluate(
        () =>
          new Promise<void>((resolve) =>
            requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
          )
      );

      const headerMetrics = await page.evaluate(() => {
        const header = document.querySelector(".app-header")!;
        const topbar = document.querySelector(".topbar")!;
        const nav = document.getElementById("canvas-navigator")!;
        const inspector = document.getElementById("canvas-inspector")!;
        const headerRect = header.getBoundingClientRect();
        const topbarRect = topbar.getBoundingClientRect();
        const topbarTop = parseFloat(window.getComputedStyle(topbar).top);
        const navTop = parseFloat(window.getComputedStyle(nav).top);
        const inspectorTop = parseFloat(window.getComputedStyle(inspector).top);
        return {
          headerHeight: headerRect.height,
          combinedStickyHeight: headerRect.height + topbarRect.height,
          topbarTopDiff: Math.abs(topbarTop - headerRect.height),
          navTopDiff: Math.abs(
            navTop - (headerRect.height + topbarRect.height)
          ),
          inspectorTopDiff: Math.abs(
            inspectorTop - (headerRect.height + topbarRect.height)
          ),
          logoutIdCount: document.querySelectorAll('[id="btn-logout"]').length,
        };
      });
      expect(
        headerMetrics.headerHeight,
        `Expected single-row .app-header at ${vp.width}x${vp.height}`
      ).toBeLessThanOrEqual(56);
      expect(headerMetrics.combinedStickyHeight).toBeLessThanOrEqual(108);
      expect(headerMetrics.topbarTopDiff).toBeLessThanOrEqual(2);
      expect(headerMetrics.navTopDiff).toBeLessThanOrEqual(2);
      expect(headerMetrics.inspectorTopDiff).toBeLessThanOrEqual(2);
      expect(headerMetrics.logoutIdCount).toBe(1);

      const menuBtn = page.locator("#btn-header-actions-menu");
      if (vp.compactMenu) {
        await expect(menuBtn).toBeVisible();
        await expect(page.locator("#btn-logout")).toBeHidden();
        await menuBtn.click();
        await expect(menuBtn).toHaveAttribute("aria-expanded", "true");
        await expect(page.locator("#btn-logout")).toBeVisible();
        await expectNoWcagLightAndDark(
          `AppShell open compact menu at ${vp.width}x${vp.height}`
        );
        await page.keyboard.press("Escape");
        await expect(menuBtn).toHaveAttribute("aria-expanded", "false");
        await expect(menuBtn).toBeFocused();
      } else {
        await expect(menuBtn).toBeHidden();
        await expect(page.locator("#btn-logout")).toBeVisible();
        await expect(
          page.locator("#btn-contextual-help-trigger")
        ).toBeVisible();
      }
    }

    // Verify expired trial badge truncation on single-row header + expired banner at 1024x768
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.evaluate(async () => {
      const win = window as unknown as { __mockExpiredEntitlements?: boolean };
      win.__mockExpiredEntitlements = true;
      const pinia = (
        document.querySelector("#app") as unknown as {
          __vue_app__?: {
            config?: {
              globalProperties?: {
                $pinia?: {
                  _s?: Map<
                    string,
                    {
                      loadEntitlements?: (opts?: {
                        force?: boolean;
                      }) => Promise<unknown>;
                    }
                  >;
                };
              };
            };
          };
        }
      )?.__vue_app__?.config?.globalProperties?.$pinia;
      await pinia?._s?.get("auth")?.loadEntitlements?.({ force: true });
    });
    await expect(page.locator("#shell-plan-badge.is-expired")).toBeVisible();
    await expect(
      page.locator("#shell-expired-subscription-banner")
    ).toBeVisible();
    const expiredHeaderHeight = await page
      .locator(".app-header")
      .evaluate((el) => el.getBoundingClientRect().height);
    expect(expiredHeaderHeight).toBeLessThanOrEqual(56);
    await expectNoWcagLightAndDark(
      "AppShell with expired trial badge and banner at 1024x768"
    );
    await page.evaluate(async () => {
      const win = window as unknown as { __mockExpiredEntitlements?: boolean };
      win.__mockExpiredEntitlements = false;
      const pinia = (
        document.querySelector("#app") as unknown as {
          __vue_app__?: {
            config?: {
              globalProperties?: {
                $pinia?: {
                  _s?: Map<
                    string,
                    {
                      loadEntitlements?: (opts?: {
                        force?: boolean;
                      }) => Promise<unknown>;
                    }
                  >;
                };
              };
            };
          };
        }
      )?.__vue_app__?.config?.globalProperties?.$pinia;
      await pinia?._s?.get("auth")?.loadEntitlements?.({ force: true });
    });

    // 3. Study locks 8-column table containment across 320x640, 768x1024, 1024x768, 1280x800, and 1440x900
    await gotoHashRoute(page, "/studies/DEMO-01?stage=closeout&section=locks");
    await expect(page.locator("#study-locks-section")).toBeVisible();
    await expect(
      page.locator('[data-testid="study-locks-table-scroll"]')
    ).toBeVisible();
    await expect(
      page.locator('[data-testid="study-lock-masked"]').first()
    ).toBeVisible();

    for (const vp of [
      { width: 320, height: 640 },
      { width: 768, height: 1024 },
      { width: 1024, height: 768 },
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
    ]) {
      await page.setViewportSize(vp);
      const lockBounds = await page.evaluate(() => {
        const ws = document
          .getElementById("canvas-workspace")!
          .getBoundingClientRect();
        const region = document
          .querySelector('[data-testid="study-locks-table-scroll"]')!
          .getBoundingClientRect();
        return {
          wsLeft: ws.x,
          wsRight: ws.x + ws.width,
          regionLeft: region.x,
          regionRight: region.x + region.width,
          scrollWidth: document.documentElement.scrollWidth,
        };
      });
      expect(lockBounds.regionLeft).toBeGreaterThanOrEqual(
        lockBounds.wsLeft - 1
      );
      expect(lockBounds.regionRight).toBeLessThanOrEqual(
        lockBounds.wsRight + 1
      );
      expect(lockBounds.scrollWidth).toBeLessThanOrEqual(vp.width);

      if (vp.width === 1024) {
        const scrollFocusMetrics = await focusWithKeyboardAndMeasureDualRing(
          page,
          '[data-testid="study-locks-table-scroll"]'
        );
        expect(scrollFocusMetrics.matchesFocusVisible).toBe(true);
        expect(scrollFocusMetrics.parentContrast).toBeGreaterThanOrEqual(3);
        await expectNoWcagLightAndDark("Study locks table at 1024x768");
        if (captureScreenshots) {
          await page.screenshot({
            path: "/tmp/verify-1024x768-locks-scroll-contained.png",
          });
        }
      }
    }

    // 4. 768x1024 stacked layout: selecting & re-selecting a participant from workspace row and command palette expands inspector and scrolls below sticky topbar
    await page.setViewportSize({ width: 768, height: 1024 });
    await gotoHashRoute(
      page,
      "/studies/DEMO-01?stage=conduct&section=participants"
    );
    await expect(page.locator("#participants-section")).toBeVisible();

    const firstRowBtn = page
      .locator(".participants-table tbody button.row-select")
      .first();
    await expect(firstRowBtn).toBeVisible();
    const selectedSubjectCode = (await firstRowBtn.innerText()).trim();

    async function expectInspectorExpandedBelowStickyTopbar(
      contextLabel: string
    ) {
      await expect(page.locator("#canvas-inspector")).toHaveAttribute(
        "data-collapsed",
        "false"
      );
      const pos = await page.evaluate(() => {
        const topbarRect = document
          .querySelector(".topbar")!
          .getBoundingClientRect();
        const inspectorRect = document
          .getElementById("canvas-inspector")!
          .getBoundingClientRect();
        return {
          topbarBottom: topbarRect.y + topbarRect.height,
          inspectorTop: inspectorRect.y,
        };
      });
      expect(
        pos.inspectorTop,
        `${contextLabel}: expected #canvas-inspector top (${pos.inspectorTop}) >= sticky .topbar bottom (${pos.topbarBottom})`
      ).toBeGreaterThanOrEqual(pos.topbarBottom - 2);
      expect(
        pos.inspectorTop,
        `${contextLabel}: expected #canvas-inspector visible in 768x1024 viewport`
      ).toBeLessThan(980);
    }

    // Initial selection from workspace row
    await firstRowBtn.click();
    await expectInspectorExpandedBelowStickyTopbar(
      "Initial participant row click at 768x1024"
    );

    // Collapse inspector, scroll back to top, and re-select the ALREADY-SELECTED participant row
    await inspectorToggle.click();
    await expect(page.locator("#canvas-inspector")).toHaveAttribute(
      "data-collapsed",
      "true"
    );
    await page.evaluate(() => window.scrollTo(0, 0));
    await firstRowBtn.click();
    await expectInspectorExpandedBelowStickyTopbar(
      "Re-selecting same participant row at 768x1024"
    );

    // Collapse inspector, scroll back to top, and re-select the ALREADY-SELECTED participant from global command palette
    await inspectorToggle.click();
    await expect(page.locator("#canvas-inspector")).toHaveAttribute(
      "data-collapsed",
      "true"
    );
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.locator("#btn-open-palette").click();
    const paletteInput = page.locator("#canvas-palette-input");
    await expect(paletteInput).toBeVisible();
    await paletteInput.fill(selectedSubjectCode);
    await expect(
      page
        .locator("#canvas-palette-list [role='option']")
        .filter({ hasText: selectedSubjectCode })
        .first()
    ).toBeVisible();
    await page.keyboard.press("Enter");
    await expectInspectorExpandedBelowStickyTopbar(
      "Re-selecting participant from command palette at 768x1024"
    );

    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/verify-768x1024-palette-inspector-scroll.png",
      });
    }
    await expectNoWcagLightAndDark(
      "Stacked StudyCanvasView inspector focus/scroll at 768x1024"
    );
  });

  test("Dark theme (data-theme='dark') and system-auto dark mode pass WCAG 2.2 AA contrast, >= 3:1 control border contrast, and neutral-ink separation across clinician workspaces", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });

    if (captureScreenshots) {
      await page.emulateMedia({
        reducedMotion: "reduce",
        colorScheme: "light",
      });
      await gotoHashRoute(page, "/studies/CADENCE-101");
      await expect(page.locator(".canvas").first()).toBeVisible();
      await page.screenshot({ path: "/tmp/web-light-semantic-tokens.png" });
    }

    await page.emulateMedia({ reducedMotion: "reduce", colorScheme: "dark" });

    // 1. Explicit dark theme (data-theme="dark") across clinician workspaces
    await page.addInitScript(() => {
      document.documentElement.setAttribute("data-theme", "dark");
    });

    const darkRoutes: Array<{
      path: string;
      readySelector: string;
      label: string;
    }> = [
      {
        path: "/overview",
        readySelector: "main",
        label: "Overview workspace in dark mode (/overview)",
      },
      {
        path: "/studies",
        readySelector: "main",
        label: "Studies workspace in dark mode (/studies)",
      },
      {
        path: "/tickets",
        readySelector: "main",
        label: "Tickets workspace in dark mode (/tickets)",
      },
      {
        path: "/knowledge",
        readySelector: "#section-knowledge-hub",
        label: "Knowledge Hub in dark mode (/knowledge)",
      },
      {
        path: "/etmf",
        readySelector: ".document-manager-layout",
        label: "eTMF Document Manager in dark mode (/etmf)",
      },
      {
        path: "/onboarding/trial",
        readySelector: "#section-trial-setup",
        label: "Trial Onboarding in dark mode (/onboarding/trial)",
      },
    ];

    for (const route of darkRoutes) {
      await gotoHashRoute(page, route.path);
      await page.evaluate(() => {
        document.documentElement.setAttribute("data-theme", "dark");
      });
      await expect(page.locator(route.readySelector).first()).toBeVisible();
      await expectNoWcagViolations(page, route.label);
    }

    // Verify neutral ink (--text-main -> rgb(248, 250, 252)) on .app-shell vs brand blue (#38bdf8 -> rgb(56, 189, 248))
    const shellInkCheck = await page.evaluate(() => {
      const shell = document.querySelector(".app-shell");
      const rootStyle = window.getComputedStyle(document.documentElement);
      return {
        shellColor: shell ? window.getComputedStyle(shell).color : "",
        textMainToken: rootStyle.getPropertyValue("--text-main").trim(),
        primaryToken: rootStyle.getPropertyValue("--primary").trim(),
      };
    });
    expect(shellInkCheck.shellColor).toBe("rgb(248, 250, 252)");
    expect(shellInkCheck.textMainToken.toLowerCase()).toBe("#f8fafc");
    expect(shellInkCheck.primaryToken.toLowerCase()).toBe("#38bdf8");

    // Verify >= 3:1 control border contrast on TrialSetupView inputs in dark mode
    const darkInputBorderRatio = await getBorderContrastRatio(
      page,
      "#input-organization-name"
    );
    expect(darkInputBorderRatio).toBeGreaterThanOrEqual(3);

    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/web-dark-semantic-tokens.png" });
    }

    // 2. System-auto dark theme (data-theme="auto" + prefers-color-scheme: dark)
    await gotoHashRoute(page, "/knowledge");
    await page.evaluate(() => {
      document.documentElement.setAttribute("data-theme", "auto");
    });
    await expect(page.locator("#section-knowledge-hub")).toBeVisible();
    const autoSurface = await page.evaluate(() =>
      window
        .getComputedStyle(document.documentElement)
        .getPropertyValue("--semantic-color-surface")
        .trim()
        .toLowerCase()
    );
    expect(autoSurface).toBe("#0f172a");
    await expectNoWcagViolations(
      page,
      "Knowledge Hub in system-auto dark mode (/knowledge)"
    );
  });

  test("ADR-2663: synced sticky chrome offsets, short-viewport header release, nested fieldset grid stacking, and zero horizontal overflow down to 320px and 200% zoom", async ({
    page,
  }) => {
    test.setTimeout(120_000);
    const captureScreenshots = process.env.CAPTURE_ADR2657_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    const SEEDED_OBSERVATION_REF =
      "observation:00000000-0000-0000-0000-000000000001@1"; // deid-ignore
    const LIVE_WEB_ROUTES = [
      "/overview",
      "/plans",
      "/onboarding/trial",
      "/login",
      "/forbidden",
      "/unavailable",
      "/studies",
      "/studies/DEMO-01",
      "/ecrf",
      `/observations/${SEEDED_OBSERVATION_REF}`,
      "/ctms",
      "/ssu",
      "/etmf",
      "/training",
      "/tickets",
      "/knowledge",
    ];

    const waitForRouteMain = async (routePath: string) => {
      await gotoHashRoute(page, routePath);
      await expect(
        page.locator("main").first(),
        `Expected <main> on ${routePath}`
      ).toBeVisible();
      await page.evaluate(
        () =>
          new Promise<void>((resolve) =>
            requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
          )
      );
    };

    // 1. /ecrf at 768x1024 and 320x640: all 7 multi-span field pairs inside form#form-VS_DEMO.responsive-grid stack full-width, and #query-flag-vssbp never overlaps #vsdpb
    for (const vp of [
      { width: 768, height: 1024 },
      { width: 320, height: 640 },
    ]) {
      await page.setViewportSize(vp);
      await waitForRouteMain("/ecrf");
      const vsForm = page.locator("form#form-VS_DEMO.responsive-grid");
      await expect(vsForm).toBeVisible();

      const formMetrics = await vsForm.evaluate((form) => {
        const formRect = form.getBoundingClientRect();
        const wrappers = Array.from(
          form.querySelectorAll(":scope > fieldset > div")
        ).filter((el) => {
          const style = window.getComputedStyle(el);
          return (
            style.display !== "none" &&
            el.getBoundingClientRect().height > 0 &&
            el.getBoundingClientRect().width > 0
          );
        });
        const rects = wrappers.map((el) => {
          const r = el.getBoundingClientRect();
          return {
            top: r.top,
            bottom: r.bottom,
            left: r.left,
            right: r.right,
            width: r.width,
            inlineSpan: el.getAttribute("style") || "",
          };
        });
        const multiSpanCount = rects.filter(
          (r) =>
            r.inlineSpan.includes("span 6") ||
            r.inlineSpan.includes("span 4") ||
            r.inlineSpan.includes("span 8") ||
            !r.inlineSpan.includes("span 12")
        ).length;

        const queryFlag = document.getElementById("query-flag-vssbp");
        const vsdpb = document.getElementById("vsdpb");
        const vssbp = document.getElementById("vssbp");
        const qRect = queryFlag?.getBoundingClientRect() ?? null;
        const dRect = vsdpb?.getBoundingClientRect() ?? null;
        const sRect = vssbp?.getBoundingClientRect() ?? null;

        const overlaps = (
          a: DOMRect | null,
          b: DOMRect | null
        ): boolean | null => {
          if (!a || !b) return null;
          return !(
            a.right <= b.left + 0.5 ||
            b.right <= a.left + 0.5 ||
            a.bottom <= b.top + 0.5 ||
            b.bottom <= a.top + 0.5
          );
        };

        return {
          formWidth: formRect.width,
          wrapperCount: rects.length,
          multiSpanCount,
          rects,
          hasQueryFlag: Boolean(qRect),
          hasVsdpb: Boolean(dRect),
          overlapsVsdpb: overlaps(qRect, dRect),
          overlapsVssbp: overlaps(qRect, sRect),
        };
      });

      // 11 multi-span fields (one 3-column row = 3 pairs + four 2-column rows = 4 pairs => 7 desktop pairs)
      expect(
        formMetrics.multiSpanCount,
        `Expected all 11 multi-span fields (7 multi-span pairs) in form#form-VS_DEMO at ${vp.width}x${vp.height}`
      ).toBe(11);

      for (let i = 0; i < formMetrics.rects.length; i++) {
        const cur = formMetrics.rects[i];
        expect(
          cur.width,
          `Field wrapper #${i} should span full form width at ${vp.width}x${vp.height}`
        ).toBeGreaterThanOrEqual(formMetrics.formWidth - 2);
        if (i > 0) {
          const prev = formMetrics.rects[i - 1];
          expect(
            cur.top,
            `Field wrapper #${i} must stack below wrapper #${i - 1} at ${vp.width}x${vp.height}`
          ).toBeGreaterThanOrEqual(prev.bottom - 1);
        }
      }

      expect(formMetrics.hasQueryFlag).toBe(true);
      expect(formMetrics.hasVsdpb).toBe(true);
      expect(
        formMetrics.overlapsVsdpb,
        `#query-flag-vssbp must never overlap #vsdpb at ${vp.width}x${vp.height}`
      ).toBe(false);
      expect(
        formMetrics.overlapsVssbp,
        `#query-flag-vssbp must never overlap #vssbp at ${vp.width}x${vp.height}`
      ).toBe(false);

      if (captureScreenshots) {
        await vsForm.scrollIntoViewIfNeeded();
        await page.screenshot({
          path: `/tmp/adr2657-ecrf-${vp.width}x${vp.height}.png`,
        });
      }
    }

    // 2. Short viewports (320x256 and 640x400): .app-header and .topbar release sticky positioning and scroll out of view
    for (const vp of [
      { width: 320, height: 256 },
      { width: 640, height: 400 },
    ]) {
      await page.setViewportSize(vp);
      await waitForRouteMain("/studies/DEMO-01");
      await expect(page.locator(".app-header")).toBeVisible();
      await expect(page.locator(".topbar")).toBeVisible();

      const shortMetrics = await page.evaluate(() => {
        const header = document.querySelector(".app-header");
        const topbar = document.querySelector(".topbar");
        const headerPos = header
          ? window.getComputedStyle(header).position
          : "";
        const topbarPos = topbar
          ? window.getComputedStyle(topbar).position
          : "";
        const stickyVar = document.documentElement.style.getPropertyValue(
          "--sticky-chrome-offset"
        );
        window.scrollTo(0, 400);
        const scrolledY = window.scrollY;
        const headerBottomAfterScroll = header
          ? header.getBoundingClientRect().bottom
          : 0;
        const topbarTopAfterScroll = topbar
          ? topbar.getBoundingClientRect().top
          : 0;
        return {
          headerPos,
          topbarPos,
          stickyVar,
          scrolledY,
          headerBottomAfterScroll,
          topbarTopAfterScroll,
        };
      });

      expect(["static", "relative"]).toContain(shortMetrics.headerPos);
      expect(shortMetrics.topbarPos).toBe("static");
      expect(shortMetrics.stickyVar).toBe("12px");
      if (shortMetrics.scrolledY > 80) {
        expect(shortMetrics.headerBottomAfterScroll).toBeLessThanOrEqual(4);
        expect(shortMetrics.topbarTopAfterScroll).toBeLessThan(20);
      }

      if (captureScreenshots && vp.width === 320) {
        await page.screenshot({
          path: "/tmp/adr2657-study-canvas-320x256-scrolled.png",
        });
      }
      await page.evaluate(() => window.scrollTo(0, 0));
    }

    // 3. Zero horizontal overflow at 320x640, 768x1024, and 900x768 across all 16 live routes (including /overview, /studies/DEMO-01, /ecrf, /ctms, /ssu, /training, /tickets)
    for (const vp of [
      { width: 320, height: 640 },
      { width: 768, height: 1024 },
      { width: 900, height: 768 },
    ]) {
      await page.setViewportSize(vp);
      for (const routePath of LIVE_WEB_ROUTES) {
        await waitForRouteMain(routePath);
        const overflow = await page.evaluate(() => {
          const doc = document.documentElement;
          const main = document.querySelector("main");
          const header = document.querySelector(".app-header");
          return {
            docScrollWidth: doc.scrollWidth,
            docClientWidth: doc.clientWidth,
            mainScrollWidth: main ? main.scrollWidth : 0,
            mainClientWidth: main ? main.clientWidth : 0,
            headerHeight: header ? header.getBoundingClientRect().height : 0,
          };
        });

        expect(
          overflow.docScrollWidth,
          `Document horizontal overflow on ${routePath} at ${vp.width}x${vp.height}`
        ).toBeLessThanOrEqual(overflow.docClientWidth);
        expect(
          overflow.mainScrollWidth,
          `<main> horizontal overflow on ${routePath} at ${vp.width}x${vp.height}`
        ).toBeLessThanOrEqual(overflow.mainClientWidth);

        if (vp.width >= 768 && overflow.headerHeight > 0) {
          expect(
            overflow.headerHeight,
            `.app-header height <= 52px on ${routePath} at ${vp.width}x${vp.height}`
          ).toBeLessThanOrEqual(52.5);
        }

        if (
          captureScreenshots &&
          vp.width === 320 &&
          routePath === "/training"
        ) {
          await page.screenshot({
            path: "/tmp/adr2657-training-320x640.png",
          });
        }
        if (
          captureScreenshots &&
          vp.width === 900 &&
          routePath === "/studies/DEMO-01"
        ) {
          await page.screenshot({
            path: "/tmp/adr2657-study-canvas-900x768.png",
          });
        }
      }
    }

    // 4. Across all 16 live web routes at 1280x800, 1366x768, 640x400, and 320x256:
    //    --sticky-chrome-offset stays synced on :root, .app-header <= 52px at >= 768px, and Tab / Shift+Tab / programmatic focus keep focused elements visible below sticky chrome
    for (const vp of [
      { width: 1280, height: 800 },
      { width: 1366, height: 768 },
      { width: 640, height: 400 },
      { width: 320, height: 256 },
    ]) {
      await page.setViewportSize(vp);
      for (const routePath of LIVE_WEB_ROUTES) {
        await waitForRouteMain(routePath);

        const headerState = await page.evaluate(() => {
          const header = document.querySelector(".app-header");
          const topbar = document.querySelector(".topbar");
          const headerRect = header ? header.getBoundingClientRect() : null;
          const headerPos = header
            ? window.getComputedStyle(header).position
            : "none";
          const topbarRect = topbar ? topbar.getBoundingClientRect() : null;
          const topbarPos = topbar
            ? window.getComputedStyle(topbar).position
            : "none";
          const rootOffsetPx = parseFloat(
            window
              .getComputedStyle(document.documentElement)
              .getPropertyValue("--sticky-chrome-offset") || "0"
          );
          const scrollPaddingTopPx = parseFloat(
            window.getComputedStyle(document.documentElement)
              .scrollPaddingTop || "0"
          );
          let stuckStickyHeight = 0;
          if (headerRect && headerPos === "sticky") {
            stuckStickyHeight += headerRect.height;
          }
          if (topbarRect && topbarPos === "sticky") {
            stuckStickyHeight += topbarRect.height;
          }
          return {
            hasHeader: Boolean(headerRect && headerRect.height > 0),
            headerHeight: headerRect ? headerRect.height : 0,
            stuckStickyHeight,
            rootOffsetPx,
            scrollPaddingTopPx,
          };
        });

        if (vp.width >= 768 && vp.height >= 500 && headerState.hasHeader) {
          expect(
            headerState.headerHeight,
            `.app-header height <= 52px on ${routePath} at ${vp.width}x${vp.height}`
          ).toBeLessThanOrEqual(52.5);
          expect(
            headerState.rootOffsetPx,
            `--sticky-chrome-offset synced >= stuckStickyHeight on ${routePath} at ${vp.width}x${vp.height}`
          ).toBeGreaterThanOrEqual(Math.floor(headerState.stuckStickyHeight));
          expect(headerState.scrollPaddingTopPx).toBeCloseTo(
            headerState.rootOffsetPx,
            0
          );
        }

        // Scroll to the bottom of the document, then programmatically focus the first interactive element inside <main>
        // (which is above the scrolled viewport / behind sticky chrome) and step with Tab + Shift+Tab
        const hasMainTarget = await page.evaluate(() => {
          (document.activeElement as HTMLElement | null)?.blur?.();
          window.scrollTo(0, document.documentElement.scrollHeight);
          const main = document.querySelector("main");
          if (!main) return false;
          const candidates = Array.from(
            main.querySelectorAll<HTMLElement>(
              'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
            )
          ).filter((el) => {
            const style = window.getComputedStyle(el);
            const rect = el.getBoundingClientRect();
            return (
              style.display !== "none" &&
              style.visibility !== "hidden" &&
              !el.classList.contains("sr-only") &&
              rect.width > 0 &&
              rect.height > 0
            );
          });
          if (candidates.length === 0) return false;
          candidates[0].focus();
          return true;
        });

        if (hasMainTarget) {
          const checkActiveVisibleBelowChrome = async (stepLabel: string) => {
            const activeMetrics = await page.evaluate(() => {
              const active = document.activeElement as HTMLElement | null;
              if (
                !active ||
                active === document.body ||
                active.classList.contains("sr-only")
              ) {
                return null;
              }
              const inStickyChrome = Boolean(
                active.closest(".app-header, .topbar")
              );
              if (inStickyChrome) return null;

              let stickyBottom = 0;
              for (const sel of [".app-header", ".topbar"]) {
                const el = document.querySelector(sel);
                if (el && window.getComputedStyle(el).position === "sticky") {
                  stickyBottom = Math.max(
                    stickyBottom,
                    el.getBoundingClientRect().bottom
                  );
                }
              }
              const rect = active.getBoundingClientRect();
              return {
                tag: active.tagName,
                id: active.id,
                top: rect.top,
                bottom: rect.bottom,
                stickyBottom,
                innerHeight: window.innerHeight,
              };
            });

            if (activeMetrics) {
              expect(
                activeMetrics.top,
                `${stepLabel} on ${routePath} at ${vp.width}x${vp.height} (${activeMetrics.tag}#${activeMetrics.id}) must sit below sticky chrome (${activeMetrics.stickyBottom}px)`
              ).toBeGreaterThanOrEqual(activeMetrics.stickyBottom - 1.5);
            }
          };

          await checkActiveVisibleBelowChrome("Programmatic focus");
          await page.keyboard.press("Tab");
          await checkActiveVisibleBelowChrome("Tab focus");
          await page.keyboard.press("Shift+Tab");
          await checkActiveVisibleBelowChrome("Shift+Tab focus");
        }
      }
    }

    // 5. 200% font scaling (320px width with 32px root font-size): .app-header and <main> wrap cleanly with zero horizontal page overflow across all 16 live web routes
    await page.setViewportSize({ width: 320, height: 640 });
    await page.evaluate(() => {
      document.documentElement.style.fontSize = "32px";
    });
    const zoomFailures: Array<{
      routePath: string;
      docScrollWidth: number;
      docClientWidth: number;
      mainScrollWidth: number;
      mainClientWidth: number;
      headerScrollWidth: number;
      headerClientWidth: number;
      culprits: string[];
    }> = [];
    try {
      for (const routePath of LIVE_WEB_ROUTES) {
        await waitForRouteMain(routePath);
        await page.evaluate(() => {
          document.documentElement.style.fontSize = "32px";
        });
        const zoomMetrics = await page.evaluate(() => {
          const doc = document.documentElement;
          const main = document.querySelector("main");
          const header = document.querySelector(".app-header");
          const limit = doc.clientWidth;
          const culprits: string[] = [];
          for (const el of Array.from(
            (main ?? document).querySelectorAll("*")
          )) {
            if (
              el.closest(
                ".scrollable-table-wrapper, .ecrf-table-scroll, .matrix-table-card, .observation-table-scroll, .casebook-matrix-scroll, [role='tablist']"
              )
            ) {
              continue;
            }
            const r = el.getBoundingClientRect();
            if (r.width > 0 && r.height > 0 && r.right > limit + 0.05) {
              const cls =
                typeof el.className === "string" && el.className.trim()
                  ? `.${el.className.trim().split(/\s+/).slice(0, 2).join(".")}`
                  : "";
              const id = el.id ? `#${el.id}` : "";
              const testId = el.getAttribute("data-testid")
                ? `[data-testid="${el.getAttribute("data-testid")}"]`
                : "";
              const parentCls =
                el.parentElement &&
                typeof el.parentElement.className === "string" &&
                el.parentElement.className.trim()
                  ? `${el.parentElement.tagName.toLowerCase()}.${el.parentElement.className.trim().split(/\s+/)[0]}>`
                  : "";
              culprits.push(
                `${parentCls}${el.tagName.toLowerCase()}${id}${cls}${testId}(right=${Math.round(r.right)},w=${Math.round(r.width)})`
              );
              if (culprits.length >= 6) break;
            }
          }
          return {
            docScrollWidth: doc.scrollWidth,
            docClientWidth: doc.clientWidth,
            mainScrollWidth: main ? main.scrollWidth : 0,
            mainClientWidth: main ? main.clientWidth : 0,
            headerScrollWidth: header ? header.scrollWidth : 0,
            headerClientWidth: header ? header.clientWidth : 0,
            culprits,
          };
        });

        if (
          zoomMetrics.docScrollWidth > zoomMetrics.docClientWidth ||
          zoomMetrics.mainScrollWidth > zoomMetrics.mainClientWidth ||
          (zoomMetrics.headerClientWidth > 0 &&
            zoomMetrics.headerScrollWidth > zoomMetrics.headerClientWidth)
        ) {
          zoomFailures.push({ routePath, ...zoomMetrics });
        }

        if (captureScreenshots && routePath === "/overview") {
          await page.screenshot({
            path: "/tmp/adr2657-overview-320px-200pct-zoom.png",
          });
        }
      }
      expect(
        zoomFailures,
        `200% font-scaling (320px @ 32px) overflows: ${JSON.stringify(zoomFailures, null, 2)}`
      ).toEqual([]);
    } finally {
      await page.evaluate(() => {
        document.documentElement.style.fontSize = "";
      });
    }
  });

  test("preserves keyboard focus across ContextualHelpDrawer, BatchUploadDrawer/ClinicalDropzone, and Clinical Canvas swaps without BODY drops or inert background locks (ADR-2676)", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_SCREENSHOTS === "1";
    await page.addInitScript(() => {
      let underlyingFetch = window.fetch.bind(window);
      const w = window as unknown as {
        __failNextTickets?: boolean;
        __mockTicketsList?: boolean;
        __failNextLocks?: boolean;
      };
      const demoTicket = {
        id: "ticket_demo_01",
        ticket_number: "TKT-1042",
        title: "Protocol version v2.0 alignment question",
        description: "Verify visit window mapping for Screening epoch.",
        status: "OPEN",
        priority: "MEDIUM",
        category: "DATA_QUERY",
        gxp_severity: "MINOR",
        study_id: "DEMO-01",
        created_at: "2026-04-01T10:00:00Z",
        updated_at: "2026-04-01T10:05:00Z",
        comments: [],
      };
      const wrappedFetch: typeof window.fetch = async (input, init) => {
        const url =
          typeof input === "string"
            ? input
            : input instanceof URL
              ? input.href
              : input.url;
        if (w.__mockTicketsList && url.includes("/api/v1/tickets")) {
          if (url.includes("/ticket_demo_01")) {
            return new Response(JSON.stringify(demoTicket), {
              status: 200,
              headers: { "Content-Type": "application/json" },
            });
          }
          if (w.__failNextTickets) {
            w.__failNextTickets = false;
            return new Response(
              JSON.stringify({
                detail: "Support queue temporarily unavailable",
              }),
              { status: 503, headers: { "Content-Type": "application/json" } }
            );
          }
          return new Response(JSON.stringify([demoTicket]), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          });
        }
        if (
          w.__failNextLocks &&
          url.includes("/locks") &&
          !url.includes("/readiness")
        ) {
          w.__failNextLocks = false;
          return new Response(
            JSON.stringify({ detail: "Temporary lock service timeout" }),
            { status: 503, headers: { "Content-Type": "application/json" } }
          );
        }
        return underlyingFetch(input, init);
      };
      Object.defineProperty(window, "fetch", {
        configurable: true,
        enumerable: true,
        get() {
          return wrappedFetch;
        },
        set(nextFn) {
          if (typeof nextFn === "function" && nextFn !== wrappedFetch) {
            underlyingFetch = nextFn.bind(window);
          }
        },
      });
    });

    await page.setViewportSize({ width: 1280, height: 800 });
    await gotoHashRoute(page, "/tickets");
    await expect(page.locator("#section-tickets-hub")).toBeVisible();

    // 1. ContextualHelpDrawer: #btn-retry-drawer-tickets recovery + ticket detail <-> list swap inside open dialog
    await page.evaluate(() => {
      const w = window as unknown as {
        __failNextTickets?: boolean;
        __mockTicketsList?: boolean;
      };
      w.__mockTicketsList = true;
      w.__failNextTickets = true;
    });

    const compactActionsBtn = page.locator("#btn-header-actions-menu");
    if (await compactActionsBtn.isVisible().catch(() => false)) {
      await compactActionsBtn.click();
      await expect(
        page.locator("#header-secondary-menu.is-open")
      ).toBeVisible();
    }
    await page.locator("#btn-contextual-help-trigger").click();
    const helpDrawer = page.locator("#contextual-help-drawer");
    await expect(helpDrawer).toBeVisible();

    await helpDrawer.locator("#tab-contextual-tickets").click();
    const retryTicketsBtn = helpDrawer.locator("#btn-retry-drawer-tickets");
    await expect(retryTicketsBtn).toBeVisible();
    await retryTicketsBtn.focus();
    await page.keyboard.press("Enter");

    const firstTicketCard = helpDrawer.locator(".drawer-ticket-card").first();
    await expect(firstTicketCard).toBeVisible();
    await expect(firstTicketCard).toBeFocused();
    await page.keyboard.press("Enter");

    const backBtn = helpDrawer.locator("#btn-back-to-ticket-list");
    await expect(backBtn).toBeVisible();
    await expect(backBtn).toBeFocused();

    await page.keyboard.press("Enter");
    await expect(firstTicketCard).toBeVisible();
    await expect(firstTicketCard).toBeFocused();

    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/contextual-help-drawer-focus-handoff.png",
      });
    }
    await page.keyboard.press("Escape");
    await expect(helpDrawer).toBeHidden();

    // 2. BatchUploadDrawer + ClinicalDropzone queue removal and Clear Queue inside open dialog
    await gotoHashRoute(page, "/documents");
    const batchTrigger = page
      .locator(".btn-batch-upload, .batch-upload-trigger-btn")
      .first();
    await expect(batchTrigger).toBeVisible();
    await batchTrigger.click();

    const batchDrawer = page.locator(".batch-drawer");
    await expect(batchDrawer).toBeVisible();
    await batchDrawer.locator('input[type="file"]').setInputFiles([
      {
        name: "protocol-amendment-1.pdf",
        mimeType: "application/pdf",
        buffer: Buffer.from("%PDF-1.4 demo 1"),
      },
      {
        name: "protocol-amendment-2.pdf",
        mimeType: "application/pdf",
        buffer: Buffer.from("%PDF-1.4 demo 2"),
      },
    ]);

    const removeButtons = batchDrawer.locator(".btn-remove-item");
    await expect(removeButtons).toHaveCount(2);
    await removeButtons.first().focus();
    await page.keyboard.press("Enter");
    await expect(removeButtons).toHaveCount(1);
    await expect(removeButtons.first()).toBeFocused();

    const clearQueueBtn = batchDrawer.getByRole("button", {
      name: /^Clear Queue$/i,
    });
    await clearQueueBtn.focus();
    await page.keyboard.press("Enter");
    await expect(removeButtons).toHaveCount(0);
    await expect(
      batchDrawer.locator(".clinical-dropzone-browse-btn")
    ).toBeFocused();

    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/batch-upload-drawer-focus-handoff.png",
      });
    }
    await page.keyboard.press("Escape");
    await expect(batchDrawer).toBeHidden();

    // 3. Clinical Canvas inline heading target & CanvasErrorAlert retry handoff (without inert background)
    await gotoHashRoute(
      page,
      "/studies/DEMO-01?stage=design&section=protocol-versions"
    );
    const versionsSection = page.locator("#protocol-versions-section");
    await expect(versionsSection).toBeVisible();
    await expect(
      versionsSection.locator("#protocol-versions-title")
    ).toHaveAttribute("tabindex", "-1");
    await expect(page.locator("#main-content")).not.toHaveAttribute("inert");

    await page.evaluate(() => {
      (window as unknown as { __failNextLocks?: boolean }).__failNextLocks =
        true;
    });

    await gotoHashRoute(page, "/studies/DEMO-01?stage=closeout&section=locks");
    const locksSection = page.locator("#study-locks-section");
    const retryLocksBtn = locksSection.locator("#btn-retry-locks-list");
    await expect(retryLocksBtn).toBeVisible();
    await retryLocksBtn.focus();
    await page.keyboard.press("Enter");
    await expect(
      locksSection
        .locator('#study-locks-empty, [data-testid="study-locks-table-scroll"]')
        .first()
    ).toBeVisible();

    const activeInsideLocks = await page.evaluate(() => {
      const active = document.activeElement;
      const section = document.getElementById("study-locks-section");
      return Boolean(
        active &&
        active !== document.body &&
        section &&
        section.contains(active)
      );
    });
    expect(activeInsideLocks).toBe(true);
    await expect(page.locator("#main-content")).not.toHaveAttribute("inert");

    if (captureScreenshots) {
      await page.screenshot({
        path: "/tmp/study-locks-retry-focus-handoff.png",
      });
    }
  });

  test("ADR-2679: passes WCAG 2.2 Level A & AA axe-core across all 40 web routes (including /auditor and /icf-builder) and verifies modal <form> Enter submit & retry focus", async ({
    page,
  }) => {
    test.setTimeout(180_000);
    const captureScreenshots = process.env.CAPTURE_ADR2679_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1280, height: 800 });

    const SEEDED_OBSERVATION_REF =
      "observation:00000000-0000-0000-0000-000000000001@1"; // deid-ignore

    const ALL_WEB_ROUTES = [
      "/overview",
      "/plans",
      "/onboarding/trial",
      "/login",
      "/forbidden",
      "/unavailable",
      "/studies",
      "/studies/DEMO-01",
      "/mdr",
      "/digitization",
      "/icf-builder",
      "/econsent-authoring",
      "/amendment-diff",
      `/observations/${SEEDED_OBSERVATION_REF}`,
      "/ecrf",
      "/imaging",
      "/devices",
      "/device-investigation",
      "/adjudication",
      "/cec",
      "/ctms",
      "/ssu",
      "/audit",
      "/auditor",
      "/rules",
      "/coding",
      "/data-lock",
      "/lakehouse",
      "/sensors",
      "/labs",
      "/dct",
      "/safety",
      "/exports",
      "/etmf",
      "/quality",
      "/notifications",
      "/tickets",
      "/documents",
      "/knowledge",
      "/training",
      "/participant-wallet",
      "/lay-summaries",
    ];
    expect(ALL_WEB_ROUTES.length).toBeGreaterThanOrEqual(40);

    const routeViolations: Array<{
      routePath: string;
      violations: Array<{
        id: string;
        impact?: string | null;
        nodes: Array<{ target: unknown; failureSummary?: string }>;
      }>;
    }> = [];

    for (const routePath of ALL_WEB_ROUTES) {
      await gotoHashRoute(page, routePath);
      await expect(
        page.locator("main").first(),
        `Expected <main> on ${routePath}`
      ).toBeVisible();
      await page.evaluate(
        () =>
          new Promise<void>((resolve) =>
            requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
          )
      );

      if (routePath === "/auditor") {
        await expect(page.locator(".auditor-workspace")).toBeVisible();
        await expect(page.locator(".audit-trail-viewer")).toBeVisible();
        if (captureScreenshots) {
          await page.screenshot({ path: "/tmp/adr2679-auditor-a11y.png" });
        }
      }

      if (routePath === "/icf-builder") {
        const editorCanvas = page.locator(".editor-canvas").first();
        if ((await editorCanvas.count()) > 0) {
          await expect(editorCanvas).toHaveAttribute("aria-multiline", "true");
          const labelledBy = await editorCanvas.getAttribute("aria-labelledby");
          expect(labelledBy).toBeTruthy();
        }
        if (captureScreenshots) {
          await page.screenshot({ path: "/tmp/adr2679-icf-builder-a11y.png" });
        }
      }

      const results = await new AxeBuilder({ page })
        .exclude(".watermark-overlay-container")
        .exclude(".watermark-overlay")
        .options({ rules: { "target-size": { enabled: true } } })
        .withTags(WCAG_AA_TAGS)
        .analyze();

      if (results.violations.length > 0) {
        routeViolations.push({
          routePath,
          violations: results.violations.map((v) => ({
            id: v.id,
            impact: v.impact,
            nodes: v.nodes.map((n) => ({
              target: n.target,
              failureSummary: n.failureSummary,
            })),
          })),
        });
      }
    }

    expect(
      routeViolations,
      `WCAG 2.2 AA violations across all 40+ routes: ${JSON.stringify(routeViolations, null, 2)}`
    ).toEqual([]);

    // Verify regulated SignatureCaptureModal <form> Enter submission, non-stealing ModalErrorAlert, and Retry focus
    await gotoHashRoute(page, "/icf-builder");
    const openSigBtn = page.locator("#btn-open-sign-modal");
    await expect(openSigBtn).toBeVisible();
    await openSigBtn.click();

    const sigModal = page.locator("#signature-capture-modal");
    await expect(sigModal).toBeVisible();
    const sigUsername = sigModal.locator("#sig-username");
    await sigUsername.fill("");
    await sigUsername.focus();
    await page.keyboard.press("Enter");

    const modalAlert = sigModal.locator('[role="alert"]').first();
    await expect(modalAlert).toBeVisible();
    // ModalErrorAlert must not steal focus on mount; Retry focuses the invalid #sig-username input
    const retryBtn = sigModal.locator('[data-testid="modal-error-retry-btn"]');
    await expect(retryBtn).toBeVisible();
    await expectNoWcagViolations(
      page,
      "SignatureCaptureModal error state on /icf-builder"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/adr2679-signature-modal-retry.png" });
    }
    await retryBtn.click();
    await expect(sigUsername).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(sigModal).toBeHidden();
  });
});
