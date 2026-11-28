import { test, expect, type Page, type Locator } from "@playwright/test";

interface TargetViewport {
  width: number;
  height: number;
  isBottomSheet: boolean;
}

const COMPACT_VIEWPORTS: readonly TargetViewport[] = [
  { width: 320, height: 640, isBottomSheet: true },
  { width: 390, height: 844, isBottomSheet: true },
  { width: 768, height: 1024, isBottomSheet: false },
  { width: 1024, height: 768, isBottomSheet: false },
  { width: 1024, height: 416, isBottomSheet: false },
];

const NARROW_REFLOW_VIEWPORTS = [
  { width: 320, height: 640 },
  { width: 390, height: 844 },
] as const;

async function gotoHashRoute(page: Page, routePath: string) {
  await page.goto(`#${routePath}`);
}

async function expectFocusInsideDrawer(
  page: Page,
  drawerPanel: Locator,
  label: string
) {
  await expect
    .poll(
      async () => {
        return await drawerPanel.evaluate((panelEl) => {
          const active = document.activeElement;
          return Boolean(active && panelEl.contains(active));
        });
      },
      {
        message: `${label}: expected document.activeElement to move inside drawer`,
        timeout: 5000,
      }
    )
    .toBe(true);
}

async function expectTriggerFocused(trigger: Locator, label: string) {
  await expect
    .poll(
      async () => {
        return await trigger.evaluate((el) => document.activeElement === el);
      },
      {
        message: `${label}: expected focus to restore to trigger control`,
        timeout: 5000,
      }
    )
    .toBe(true);
}

async function expectZeroHorizontalOverflow(
  page: Page,
  containerSelector: string,
  label: string
) {
  const metrics = await page.evaluate((sel) => {
    const docEl = document.documentElement;
    const mainEl =
      (document.querySelector(sel) as HTMLElement | null) ||
      (document.querySelector("main") as HTMLElement | null);
    return {
      docScrollWidth: docEl.scrollWidth,
      docClientWidth: docEl.clientWidth,
      mainExists: Boolean(mainEl),
      mainScrollWidth: mainEl ? mainEl.scrollWidth : 0,
      mainClientWidth: mainEl ? mainEl.clientWidth : 0,
    };
  }, containerSelector);

  expect(
    metrics.docScrollWidth,
    `${label}: documentElement.scrollWidth (${metrics.docScrollWidth}) must equal clientWidth (${metrics.docClientWidth})`
  ).toBe(metrics.docClientWidth);
  expect(
    metrics.mainExists,
    `${label}: main container (${containerSelector}) must exist`
  ).toBe(true);
  expect(
    metrics.mainScrollWidth,
    `${label}: container (${containerSelector}) scrollWidth (${metrics.mainScrollWidth}) must equal clientWidth (${metrics.mainClientWidth})`
  ).toBe(metrics.mainClientWidth);
}

test.describe("ClinicalSplitWorkspace & Adaptive Kanban Multi-Viewport Reflow", () => {
  test("Opens and focuses compact drawers/bottom-sheets and restores trigger focus across all 9 clinical workspaces and 5 compact viewports", async ({
    page,
  }) => {
    test.setTimeout(180000);
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    const workspaces: Array<{
      name: string;
      prepare: (page: Page) => Promise<void>;
      getTrigger: (page: Page) => Locator;
    }> = [
      {
        name: "KnowledgeView (/knowledge)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/knowledge");
          await expect(p.locator("button.article-card").first()).toBeVisible();
        },
        getTrigger: (p) => p.locator("button.article-card").first(),
      },
      {
        name: "ProtocolTrainingConsole (/training)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/training");
          await expect(
            p.locator("#section-study-training button.module-item").first()
          ).toBeVisible();
        },
        getTrigger: (p) =>
          p.locator("#section-study-training button.module-item").first(),
      },
      {
        name: "DocumentManagerView + TmfBinderTree (/etmf)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/etmf");
          const artifactTrigger = p.locator("button.artifact-node").first();
          if (!(await artifactTrigger.isVisible().catch(() => false))) {
            const zoneHeader = p.locator("button.zone-header").first();
            await expect(zoneHeader).toBeVisible();
            if ((await zoneHeader.getAttribute("aria-expanded")) !== "true") {
              await zoneHeader.click();
            }
            const sectionHeader = p.locator("button.section-header").first();
            await expect(sectionHeader).toBeVisible();
            if (
              (await sectionHeader.getAttribute("aria-expanded")) !== "true"
            ) {
              await sectionHeader.click();
            }
          }
          await expect(artifactTrigger).toBeVisible();
        },
        getTrigger: (p) => p.locator("button.artifact-node").first(),
      },
      {
        name: "ImagingView (/imaging)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/imaging");
          await expect(p.locator("button.series-item").first()).toBeVisible();
        },
        getTrigger: (p) => p.locator("button.series-item").first(),
      },
      {
        name: "CecAdjudicationConsole (/adjudication)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/adjudication");
          await expect(p.locator("button.dossier-card").first()).toBeVisible();
        },
        getTrigger: (p) => p.locator("button.dossier-card").first(),
      },
      {
        name: "LaySummaryPortal (/lay-summaries)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/lay-summaries");
          await expect(
            p.locator("button.summary-item-card").first()
          ).toBeVisible();
        },
        getTrigger: (p) => p.locator("button.summary-item-card").first(),
      },
      {
        name: "CrfAuthoringCanvas (/mdr canvas)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/mdr");
          const canvasTab = p.locator("#btn-tab-canvas-top");
          await expect(canvasTab).toBeVisible();
          await canvasTab.click();
          await expect(p.locator(".canvas-field-widget").first()).toBeVisible();
        },
        getTrigger: (p) => p.locator(".canvas-field-widget").first(),
      },
      {
        name: "ConsentAuthoringView (/econsent-authoring)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/econsent-authoring");
          await expect(p.locator("#btn-create-template")).toBeVisible();
        },
        getTrigger: (p) => p.locator("#btn-create-template"),
      },
      {
        name: "RulesView (/rules)",
        prepare: async (p) => {
          await gotoHashRoute(p, "/rules");
          await expect(p.locator(".btn-create-rule")).toBeVisible();
        },
        getTrigger: (p) => p.locator(".btn-create-rule"),
      },
    ];

    for (const workspace of workspaces) {
      await page.setViewportSize({ width: 768, height: 1024 });
      await workspace.prepare(page);

      for (const vp of COMPACT_VIEWPORTS) {
        await page.setViewportSize({ width: vp.width, height: vp.height });
        const ctx = `${workspace.name} at ${vp.width}x${vp.height}`;

        const drawerPanel = page
          .locator(".clinical-split-drawer-panel[role='dialog']")
          .first();

        // Ensure closed before triggering selection
        if (await drawerPanel.isVisible().catch(() => false)) {
          await page.keyboard.press("Escape");
          await expect(drawerPanel).toBeHidden();
        }

        const trigger = workspace.getTrigger(page);
        await trigger.scrollIntoViewIfNeeded();
        await trigger.focus();
        await trigger.click();

        await expect(drawerPanel, `${ctx}: drawer opens`).toBeVisible();
        await expect(drawerPanel).toHaveAttribute("aria-modal", "true");

        const box = await drawerPanel.boundingBox();
        expect(box).not.toBeNull();
        if (vp.isBottomSheet) {
          await expect(drawerPanel).toHaveClass(/drawer-mobile-bottom-sheet/);
          expect(box!.height).toBeLessThanOrEqual(
            Math.ceil(vp.height * 0.9) + 1.5
          );
          expect(box!.y).toBeGreaterThanOrEqual(
            Math.floor(vp.height * 0.1) - 2
          );
        } else {
          expect(box!.y).toBeLessThanOrEqual(1.5);
          expect(Math.abs(box!.height - vp.height)).toBeLessThanOrEqual(2);
        }

        await expectFocusInsideDrawer(page, drawerPanel, ctx);

        if (
          captureScreenshots &&
          workspace.name.startsWith("KnowledgeView") &&
          vp.width === 390 &&
          vp.height === 844
        ) {
          await page.screenshot({
            path: "/tmp/knowledge-mobile-sheet-390x844.png",
          });
        }
        if (
          captureScreenshots &&
          workspace.name.startsWith("ImagingView") &&
          vp.width === 768 &&
          vp.height === 1024
        ) {
          await page.screenshot({
            path: "/tmp/imaging-tablet-drawer-768x1024.png",
          });
        }

        // Dismiss with Escape and verify trigger focus restoration
        await page.keyboard.press("Escape");
        await expect(
          drawerPanel,
          `${ctx}: drawer closes on Escape`
        ).toBeHidden();
        await expectTriggerFocused(trigger, ctx);
      }

      // Verify >= 1280px renders inline without compact drawer
      await page.setViewportSize({ width: 1440, height: 900 });
      await expect(page.locator(".clinical-split-drawer-panel")).toHaveCount(0);
    }
  });

  test("Preserves keepMounted state and LIFO Escape with nested ReasonModal in stateful editors, enforces native button selection semantics, and eliminates 320x640 / 390x844 horizontal overflow", async ({
    page,
  }) => {
    test.setTimeout(120000);
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    // 1. Native <button type="button"> + aria-current="true" semantics
    await page.setViewportSize({ width: 1440, height: 900 });

    await gotoHashRoute(page, "/imaging");
    await expect(
      page.locator("button.series-item[type='button']").first()
    ).toBeVisible();
    await expect(page.locator(".series-item:not(button)")).toHaveCount(0);
    await expect(
      page.locator("button.series-item[aria-current='true']")
    ).toHaveCount(1);

    await gotoHashRoute(page, "/adjudication");
    await expect(
      page.locator("button.dossier-card[type='button']").first()
    ).toBeVisible();
    await expect(page.locator(".dossier-card:not(button)")).toHaveCount(0);
    await page.locator("button.dossier-card").first().click();
    await expect(
      page.locator("button.dossier-card[aria-current='true']")
    ).toHaveCount(1);

    await gotoHashRoute(page, "/lay-summaries");
    await expect(
      page.locator("button.summary-item-card[type='button']").first()
    ).toBeVisible();
    await expect(page.locator(".summary-item-card:not(button)")).toHaveCount(0);
    await expect(
      page.locator("button.summary-item-card[aria-current='true']")
    ).toHaveCount(1);

    // 2. keepMounted + LIFO Escape with nested ReasonModal in ConsentAuthoringView (/econsent-authoring)
    await page.setViewportSize({ width: 768, height: 1024 });
    await gotoHashRoute(page, "/econsent-authoring");
    const createTplBtn = page.locator("#btn-create-template");
    await expect(createTplBtn).toBeVisible();
    await createTplBtn.click();

    const consentDrawer = page.locator(
      ".clinical-split-drawer-panel[role='dialog']"
    );
    await expect(consentDrawer).toBeVisible();

    await page.locator("#input-study-id").fill("STUDY-KEEP-01");
    await page
      .locator("#input-template-name")
      .fill("KeepMounted Consent Draft");
    await page.locator("#input-protocol-version").fill("v4.2");

    // Open nested 21 CFR Part 11 ReasonModal inside the open drawer
    await consentDrawer.locator(".btn-save").click();
    const reasonDialog = page.locator(".clinical-modal-dialog[role='dialog']");
    await expect(reasonDialog).toBeVisible();

    // First Escape closes ONLY ReasonModal while keeping ConsentAuthoringView drawer open & state preserved
    await page.keyboard.press("Escape");
    await expect(reasonDialog).toBeHidden();
    await expect(consentDrawer).toBeVisible();
    await expect(page.locator("#input-template-name")).toHaveValue(
      "KeepMounted Consent Draft"
    );

    // Second Escape closes ConsentAuthoringView drawer and restores focus to trigger
    await page.keyboard.press("Escape");
    await expect(consentDrawer).toBeHidden();
    await expectTriggerFocused(
      createTplBtn,
      "ConsentAuthoringView after LIFO Escape"
    );

    // 3. Zero horizontal overflow (scrollWidth === clientWidth) at 320x640 and 390x844
    const overflowTargets: Array<{
      name: string;
      containerSelector: string;
      navigate: (p: Page) => Promise<void>;
    }> = [
      {
        name: "ImagingView (/imaging)",
        containerSelector: "#section-imaging-viewer",
        navigate: async (p) => {
          await gotoHashRoute(p, "/imaging");
          await expect(p.locator("#section-imaging-viewer")).toBeVisible();
        },
      },
      {
        name: "CrfAuthoringCanvas (/mdr canvas)",
        containerSelector: "#section-mdr",
        navigate: async (p) => {
          await gotoHashRoute(p, "/mdr");
          await p.locator("#btn-tab-canvas-top").click();
          await expect(p.locator(".authoring-canvas-layout")).toBeVisible();
        },
      },
      {
        name: "CecAdjudicationConsole (/adjudication)",
        containerSelector: ".cec-adjudication-console",
        navigate: async (p) => {
          await gotoHashRoute(p, "/adjudication");
          await expect(p.locator(".cec-adjudication-console")).toBeVisible();
        },
      },
      {
        name: "LaySummaryPortal (/lay-summaries)",
        containerSelector: "#section-lay-summaries",
        navigate: async (p) => {
          await gotoHashRoute(p, "/lay-summaries");
          await expect(p.locator("#section-lay-summaries")).toBeVisible();
        },
      },
      {
        name: "TicketsView (/tickets)",
        containerSelector: "#section-tickets",
        navigate: async (p) => {
          await gotoHashRoute(p, "/tickets");
          await expect(p.locator(".kanban-board")).toBeVisible();
        },
      },
    ];

    for (const target of overflowTargets) {
      for (const vp of NARROW_REFLOW_VIEWPORTS) {
        await page.setViewportSize({ width: vp.width, height: vp.height });
        await target.navigate(page);
        await expectZeroHorizontalOverflow(
          page,
          target.containerSelector,
          `${target.name} at ${vp.width}x${vp.height}`
        );
      }
    }

    // 4. TicketsView adaptive Kanban stage tabs below 1280px & >= 160px .column-cards height at 1024x416
    for (const vp of COMPACT_VIEWPORTS) {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await gotoHashRoute(page, "/tickets");

      const stageTabs = page.locator(
        '[role="tablist"][aria-label="Kanban workflow stages"]'
      );
      await expect(stageTabs).toBeVisible();
      await expect(page.locator(".kanban-column")).toHaveCount(1);

      const boardOverflow = await page
        .locator(".kanban-board")
        .evaluate((el) => ({
          scrollWidth: el.scrollWidth,
          clientWidth: el.clientWidth,
        }));
      expect(
        boardOverflow.scrollWidth,
        `TicketsView .kanban-board zero horizontal overflow at ${vp.width}x${vp.height}`
      ).toBe(boardOverflow.clientWidth);

      if (vp.width === 390 && vp.height === 844) {
        // Switch stage via ClinicalTabs and verify displayed column updates
        await page.locator("#tickets-kanban-stage-tab-progress").click();
        await expect(page.locator(".column-title")).toHaveText("Investigation");
        await page.locator("#tickets-kanban-stage-tab-open").click();
        await expect(page.locator(".column-title")).toHaveText("Open / Triage");
        if (captureScreenshots) {
          await page.locator(".kanban-column").first().scrollIntoViewIfNeeded();
          await stageTabs.scrollIntoViewIfNeeded();
          await page.screenshot({
            path: "/tmp/tickets-adaptive-kanban-390x844.png",
          });
        }
      }

      if (vp.width === 1024 && vp.height === 416) {
        const cardsLocator = page.locator(".column-cards").first();
        await cardsLocator.scrollIntoViewIfNeeded();
        const cardsBox = await cardsLocator.boundingBox();
        expect(cardsBox).not.toBeNull();
        expect(
          cardsBox!.height,
          `TicketsView .column-cards height (${cardsBox!.height}px) >= 160px at 1024x416`
        ).toBeGreaterThanOrEqual(160);
        if (captureScreenshots) {
          await page.evaluate(() => {
            const tabs = document.querySelector(".kanban-stage-tabs");
            tabs?.scrollIntoView({ block: "start" });
          });
          await page.screenshot({
            path: "/tmp/tickets-adaptive-kanban-1024x416.png",
          });
        }
      }
    }
  });
});
