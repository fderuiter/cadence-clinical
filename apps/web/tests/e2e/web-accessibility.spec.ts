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
  let nativeDialogs: string[] = [];

  test.beforeEach(({ page }) => {
    nativeDialogs = [];
    page.on("dialog", async (dialog) => {
      nativeDialogs.push(`${dialog.type()}: ${dialog.message()}`);
      await dialog.dismiss();
    });
  });

  test.afterEach(() => {
    expect(
      nativeDialogs,
      "Expected zero native blocking browser dialogs (alert/confirm/prompt)"
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
    await expect(kanbanBoard).toHaveAttribute("role", "region");

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
});
