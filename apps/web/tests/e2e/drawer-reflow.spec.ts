import { test, expect, type Page, type Locator } from "@playwright/test";

interface TargetViewport {
  width: number;
  height: number;
  minBodyHeight: number;
  isShortViewport: boolean;
  isMobileBottomSheet: boolean;
}

const TARGET_VIEWPORTS: readonly TargetViewport[] = [
  {
    width: 320,
    height: 640,
    minBodyHeight: 120,
    isShortViewport: false,
    isMobileBottomSheet: true,
  },
  {
    width: 390,
    height: 844,
    minBodyHeight: 120,
    isShortViewport: false,
    isMobileBottomSheet: true,
  },
  {
    width: 768,
    height: 1024,
    minBodyHeight: 160,
    isShortViewport: false,
    isMobileBottomSheet: false,
  },
  {
    width: 1024,
    height: 768,
    minBodyHeight: 160,
    isShortViewport: false,
    isMobileBottomSheet: false,
  },
  {
    width: 1024,
    height: 416,
    minBodyHeight: 120,
    isShortViewport: true,
    isMobileBottomSheet: false,
  },
];

async function gotoHashRoute(page: Page, routePath: string) {
  await page.goto(`#${routePath}`);
}

async function expectElementScrollableIntoViewport(
  locator: Locator,
  viewportWidth: number,
  viewportHeight: number,
  label: string
) {
  await expect(locator, `${label}: element visible`).toBeVisible();
  await locator.scrollIntoViewIfNeeded();
  const box = await locator.boundingBox();
  expect(box, `${label}: bounding box present`).not.toBeNull();
  expect(
    box!.y,
    `${label}: top (${box!.y}) >= 0 inside ${viewportWidth}x${viewportHeight}`
  ).toBeGreaterThanOrEqual(-1);
  expect(
    box!.y + box!.height,
    `${label}: bottom (${box!.y + box!.height}) <= ${viewportHeight}`
  ).toBeLessThanOrEqual(viewportHeight + 1.5);
  expect(
    box!.x,
    `${label}: left (${box!.x}) >= 0 inside ${viewportWidth}x${viewportHeight}`
  ).toBeGreaterThanOrEqual(-1);
  expect(
    box!.x + box!.width,
    `${label}: right (${box!.x + box!.width}) <= ${viewportWidth}`
  ).toBeLessThanOrEqual(viewportWidth + 1.5);
}

async function verifyDrawerAcrossTargetViewports(
  page: Page,
  panelSelector: string,
  drawerName: string
) {
  const panel = page.locator(panelSelector).first();
  await expect(panel, `${drawerName}: drawer panel visible`).toBeVisible();
  await expect(panel).toHaveClass(/clinical-drawer-panel/);
  await expect(panel).toHaveAttribute("aria-modal", "true");

  // Verify parent .clinical-drawer-backdrop is position: fixed (not static)
  const backdropPosition = await panel.evaluate((el) => {
    const backdrop = el.closest(".clinical-drawer-backdrop");
    return backdrop ? window.getComputedStyle(backdrop).position : null;
  });
  expect(
    backdropPosition,
    `${drawerName}: .clinical-drawer-backdrop must mount as position: fixed`
  ).toBe("fixed");

  for (const vp of TARGET_VIEWPORTS) {
    await page.setViewportSize({ width: vp.width, height: vp.height });

    const metrics = await panel.evaluate((panelEl) => {
      const htmlEl = panelEl as HTMLElement;
      const bodyEl = htmlEl.querySelector(
        ":scope > .clinical-drawer-body"
      ) as HTMLElement | null;
      const headerEl = htmlEl.firstElementChild as HTMLElement | null;
      const footerEl = htmlEl.querySelector(
        ":scope > .clinical-drawer-footer"
      ) as HTMLElement | null;

      htmlEl.scrollTop = 0;
      if (bodyEl) bodyEl.scrollTop = 0;

      const panelRect = htmlEl.getBoundingClientRect();
      const bodyRect = bodyEl?.getBoundingClientRect() ?? null;
      const headerRect =
        headerEl && headerEl !== bodyEl && headerEl !== footerEl
          ? headerEl.getBoundingClientRect()
          : null;

      const visibleTop = bodyRect
        ? Math.max(bodyRect.top, panelRect.top, 0)
        : 0;
      const visibleBottom = bodyRect
        ? Math.min(bodyRect.bottom, panelRect.bottom, window.innerHeight)
        : 0;
      const visibleBodyHeight = Math.max(0, visibleBottom - visibleTop);

      const textControls = Array.from(
        htmlEl.querySelectorAll(
          'input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]):not([type="range"]):not([type="file"]), select, textarea'
        )
      ) as HTMLElement[];

      const undersizedFontControls = textControls
        .filter((ctrl) => ctrl.getClientRects().length > 0)
        .map((ctrl) => ({
          tag: ctrl.tagName,
          id: ctrl.id || ctrl.className,
          fontSize: parseFloat(window.getComputedStyle(ctrl).fontSize || "0"),
        }))
        .filter((entry) => entry.fontSize < 16);

      return {
        panelHeight: panelRect.height,
        panelScrollWidth: htmlEl.scrollWidth,
        panelClientWidth: htmlEl.clientWidth,
        bodyExists: Boolean(bodyEl),
        bodyScrollWidth: bodyEl ? bodyEl.scrollWidth : 0,
        bodyClientWidth: bodyEl ? bodyEl.clientWidth : 0,
        visibleBodyHeight,
        headerHeight: headerRect ? headerRect.height : 0,
        hasFooterButtons: Boolean(
          footerEl && footerEl.querySelector("button:not([disabled]), button")
        ),
        undersizedFontControls,
      };
    });

    const ctx = `${drawerName} at ${vp.width}x${vp.height}`;
    expect(metrics.bodyExists, `${ctx}: .clinical-drawer-body present`).toBe(
      true
    );
    expect(
      metrics.visibleBodyHeight,
      `${ctx}: visible .clinical-drawer-body height (${metrics.visibleBodyHeight.toFixed(1)}px) >= ${vp.minBodyHeight}px`
    ).toBeGreaterThanOrEqual(vp.minBodyHeight);

    if (vp.isShortViewport) {
      expect(
        metrics.headerHeight,
        `${ctx}: short-viewport header height (${metrics.headerHeight.toFixed(1)}px) <= 52px`
      ).toBeLessThanOrEqual(52.5);
    }

    if (vp.isMobileBottomSheet) {
      expect(
        metrics.panelHeight,
        `${ctx}: mobile bottom sheet height (${metrics.panelHeight.toFixed(1)}px) <= 90dvh`
      ).toBeLessThanOrEqual(Math.ceil(vp.height * 0.9) + 1.5);
    }

    if (vp.width === 320) {
      expect(
        metrics.panelScrollWidth,
        `${ctx}: panel scrollWidth (${metrics.panelScrollWidth}) <= clientWidth (${metrics.panelClientWidth}) under WCAG 2.2 SC 1.4.10`
      ).toBeLessThanOrEqual(metrics.panelClientWidth + 1);
      expect(
        metrics.bodyScrollWidth,
        `${ctx}: body scrollWidth (${metrics.bodyScrollWidth}) <= clientWidth (${metrics.bodyClientWidth}) under WCAG 2.2 SC 1.4.10`
      ).toBeLessThanOrEqual(metrics.bodyClientWidth + 1);
    }

    expect(
      metrics.undersizedFontControls,
      `${ctx}: touch <input>, <select>, and <textarea> controls must have >= 16px font-size`
    ).toEqual([]);

    if (metrics.hasFooterButtons) {
      const footerButtons = panel.locator(
        ":scope > .clinical-drawer-footer button"
      );
      const count = await footerButtons.count();
      for (let i = 0; i < count; i++) {
        const btn = footerButtons.nth(i);
        if (await btn.isVisible()) {
          await expectElementScrollableIntoViewport(
            btn,
            vp.width,
            vp.height,
            `${ctx} footer button[${i}]`
          );
        }
      }
    }
  }
}

test.describe("Bounded ClinicalDrawer Viewport Reflow & Scrollable Body Verification (ADR-2659)", () => {
  test("Verifies visible .clinical-drawer-body height, 320px zero horizontal overflow, 16px touch inputs, and reachable footer controls across all 13 web drawers and 5 target viewports", async ({
    page,
  }) => {
    test.setTimeout(120000);
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    // 1 & 2. ContextualHelpDrawer & OnboardingChecklistDrawer (/overview)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/overview");
    const headerActionsToggle = page.locator("#btn-header-actions-menu");
    if (await headerActionsToggle.isVisible()) {
      await headerActionsToggle.click();
    }
    await expect(page.locator("#btn-contextual-help-trigger")).toBeVisible();

    await page.locator("#btn-contextual-help-trigger").click();
    await verifyDrawerAcrossTargetViewports(
      page,
      "#contextual-help-drawer",
      "ContextualHelpDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator("#contextual-help-drawer")).toBeHidden();

    await page.setViewportSize({ width: 1024, height: 768 });
    if (await headerActionsToggle.isVisible()) {
      await headerActionsToggle.click();
    }
    await expect(
      page.locator("#btn-onboarding-checklist-trigger")
    ).toBeVisible();
    await page.locator("#btn-onboarding-checklist-trigger").click();
    await verifyDrawerAcrossTargetViewports(
      page,
      "#onboarding-checklist-drawer",
      "OnboardingChecklistDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator("#onboarding-checklist-drawer")).toBeHidden();

    // 3 & 4. SynonymImportDrawer & UpversioningImpactDrawer (/coding)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/coding");
    await expect(page.locator(".synonym-import-btn")).toBeVisible();

    await page.locator(".synonym-import-btn").click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".synonym-drawer-panel",
      "SynonymImportDrawer"
    );
    if (captureScreenshots) {
      await page.setViewportSize({ width: 768, height: 1024 });
      await page.screenshot({
        path: "/tmp/synonym-import-drawer-768x1024.png",
      });
    }
    await page.keyboard.press("Escape");
    await expect(page.locator(".synonym-drawer-panel")).toBeHidden();

    await page.setViewportSize({ width: 1024, height: 768 });
    await page
      .locator("button", { hasText: /Up-Versioning Impact/i })
      .first()
      .click();
    await verifyDrawerAcrossTargetViewports(
      page,
      '.drawer-panel[aria-labelledby="upversioning-impact-title"]',
      "UpversioningImpactDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(
      page.locator('.drawer-panel[aria-labelledby="upversioning-impact-title"]')
    ).toBeHidden();

    // 5. UsdmConceptStagingDrawer (/digitization)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/digitization");
    await expect(page.locator("#btn-stage-biomedical-concepts")).toBeVisible();
    await page.locator("#btn-stage-biomedical-concepts").click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".usdm-concept-staging-drawer",
      "UsdmConceptStagingDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".usdm-concept-staging-drawer")).toBeHidden();

    // 6 & 7. ReadabilityDrawer & QuizTemplateDrawer (/icf-builder)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/icf-builder");
    await expect(page.locator(".readability-btn").first()).toBeVisible();
    await page.locator(".readability-btn").first().click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".readability-drawer.open",
      "ReadabilityDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".readability-drawer.open")).toHaveCount(0);

    await page.setViewportSize({ width: 1024, height: 768 });
    await page
      .locator(".outline-item", { hasText: /Comprehension Quiz/i })
      .click();
    await expect(page.locator(".btn-import-template")).toBeVisible();
    await page.locator(".btn-import-template").click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".quiz-template-drawer",
      "QuizTemplateDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".quiz-template-drawer")).toBeHidden();

    // 8 & 9. BatchUploadDrawer & ExportJobDrawer (/etmf)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/etmf");
    const batchUploadTrigger = page
      .getByRole("button", { name: /Batch Ingestion|Batch Upload/i })
      .first();
    await expect(batchUploadTrigger).toBeVisible();
    await batchUploadTrigger.click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".batch-drawer",
      "BatchUploadDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".batch-drawer")).toBeHidden();

    await page.setViewportSize({ width: 1024, height: 768 });
    await page
      .getByRole("button", { name: /Export Complete Study Binder/i })
      .first()
      .click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".export-drawer",
      "ExportJobDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".export-drawer")).toBeHidden();

    // 10. ShareDrawer (/documents)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/documents");
    const shareBtn = page.locator(".btn-share-action").first();
    await expect(shareBtn).toBeVisible();
    await shareBtn.click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".share-drawer",
      "ShareDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".share-drawer")).toBeHidden();

    // 11. TicketDetailDrawer (/tickets) — also expand .resolve-panel inside .clinical-drawer-body
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/tickets");
    const ticketCard = page.locator("button.ticket-card").first();
    await expect(ticketCard).toBeVisible();
    await ticketCard.click();
    const resolveToggle = page.locator("#btn-open-resolve");
    if (await resolveToggle.isVisible().catch(() => false)) {
      await resolveToggle.click();
    }
    await verifyDrawerAcrossTargetViewports(
      page,
      '.drawer-panel[aria-labelledby="drawer-ticket-title"]',
      "TicketDetailDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(
      page.locator('.drawer-panel[aria-labelledby="drawer-ticket-title"]')
    ).toBeHidden();

    // 12. EhrReconciliationDrawer (/ecrf) — including batch bar & nested ClinicalModal LIFO Escape
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(page, "/ecrf");
    const openEhrBtn = page
      .locator("button", { hasText: /EHR.*Reconciliation/i })
      .first();
    await expect(openEhrBtn).toBeVisible();
    await openEhrBtn.click();
    const ehrDrawer = page.locator(".ehr-reconciliation-drawer");
    await expect(ehrDrawer).toBeVisible();
    await expect(
      ehrDrawer.locator(".reconciliation-card").first()
    ).toBeVisible();

    const selectHighConfBtn = ehrDrawer.locator(".btn-select-high-confidence");
    if (await selectHighConfBtn.isVisible().catch(() => false)) {
      await selectHighConfBtn.click();
    }

    await verifyDrawerAcrossTargetViewports(
      page,
      ".ehr-reconciliation-drawer",
      "EhrReconciliationDrawer"
    );

    if (captureScreenshots) {
      await page.setViewportSize({ width: 390, height: 844 });
      await page.screenshot({
        path: "/tmp/ehr-reconciliation-drawer-390x844.png",
      });
    }

    // Verify nested ClinicalModal (Reject with Reason) opens cleanly and LIFO Escape keeps drawer open
    await page.setViewportSize({ width: 390, height: 844 });
    const rejectBtn = ehrDrawer.locator(".btn-reject").first();
    if (await rejectBtn.isVisible().catch(() => false)) {
      await rejectBtn.scrollIntoViewIfNeeded();
      await rejectBtn.click();
      const rejectDialog = page.locator(
        '[role="dialog"][aria-labelledby="reject-modal-title"]'
      );
      await expect(rejectDialog).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(rejectDialog).toBeHidden();
      await expect(ehrDrawer).toBeVisible();
    }

    await page.keyboard.press("Escape");
    await expect(ehrDrawer).toBeHidden();

    // 13. BulkIpKitReconciliationDrawer (/studies/CADENCE-101?stage=conduct&section=supply&siteId=SITE-101)
    await page.setViewportSize({ width: 1024, height: 768 });
    await gotoHashRoute(
      page,
      "/studies/CADENCE-101?stage=conduct&section=supply&siteId=SITE-101"
    );
    const openKitBtn = page.locator("#btn-open-kit-reconciliation");
    if (!(await openKitBtn.isVisible().catch(() => false))) {
      const supplyScopeForm = page.getByRole("form", {
        name: "Investigational supply scope",
      });
      await supplyScopeForm.getByLabel("Site").fill("SITE-101");
      await supplyScopeForm
        .getByRole("button", { name: /Load supply status/i })
        .click();
    }
    await expect(openKitBtn).toBeVisible();
    await openKitBtn.click();
    await verifyDrawerAcrossTargetViewports(
      page,
      ".bulk-ip-reconciliation-drawer",
      "BulkIpKitReconciliationDrawer"
    );
    await page.keyboard.press("Escape");
    await expect(page.locator(".bulk-ip-reconciliation-drawer")).toBeHidden();
  });

  test("BulkIpKitReconciliationDrawer barcode scanning at 1024x416 and 320x640 keeps signature inputs and .btn-submit-batch scrollable into view with zero horizontal overflow", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.emulateMedia({ reducedMotion: "reduce" });

    await page.setViewportSize({ width: 1024, height: 416 });
    await gotoHashRoute(
      page,
      "/studies/CADENCE-101?stage=conduct&section=supply"
    );
    await page.locator("#btn-open-kit-reconciliation").click();

    const drawer = page.locator(".bulk-ip-reconciliation-drawer");
    await expect(drawer).toBeVisible();

    const scannerInput = drawer.locator("#barcode-scanner-input");
    await expect(scannerInput).toBeVisible();

    // Verify #barcode-scanner-input lives inside .clinical-drawer-body
    const scannerInsideScrollBody = await scannerInput.evaluate((el) =>
      Boolean(el.closest(".clinical-drawer-body"))
    );
    expect(scannerInsideScrollBody).toBe(true);

    // Scan barcode at 1024x416 (landscape tablet with open keyboard)
    await scannerInput.fill("KIT-1001|LOT-2026A");
    await scannerInput.press("Enter");
    await expect(drawer.locator("tbody tr").first()).toBeVisible();

    for (const vp of [
      { width: 1024, height: 416 },
      { width: 320, height: 640 },
    ]) {
      await page.setViewportSize(vp);
      const label = `BulkIpKitReconciliationDrawer scanned kit at ${vp.width}x${vp.height}`;

      // Scan a second barcode at 320x640 to prove live scanning works in narrow mobile bottom sheet too
      if (vp.width === 320) {
        await expectElementScrollableIntoViewport(
          scannerInput,
          vp.width,
          vp.height,
          `${label} #barcode-scanner-input`
        );
        await scannerInput.fill("KIT-1002|LOT-2026B");
        await scannerInput.press("Enter");
        await expect(drawer.locator("tbody tr")).toHaveCount(2);

        // Verify stacked kit record cards at <= 480px
        const rowDisplay = await drawer
          .locator("tbody tr")
          .first()
          .evaluate((tr) => window.getComputedStyle(tr).display);
        expect(["block", "flex", "grid"]).toContain(rowDisplay);
      }

      if (captureScreenshots && vp.width === 1024) {
        await page.screenshot({
          path: "/tmp/bulk-ip-kit-drawer-1024x416.png",
        });
      }
      if (captureScreenshots && vp.width === 320) {
        await page.screenshot({
          path: "/tmp/bulk-ip-kit-drawer-320x640.png",
        });
      }

      await expectElementScrollableIntoViewport(
        drawer.locator("#pharmacist-signature-id"),
        vp.width,
        vp.height,
        `${label} #pharmacist-signature-id`
      );
      await expectElementScrollableIntoViewport(
        drawer.locator("#reason-for-change-input"),
        vp.width,
        vp.height,
        `${label} #reason-for-change-input`
      );
      await expectElementScrollableIntoViewport(
        drawer.locator(".btn-submit-batch"),
        vp.width,
        vp.height,
        `${label} .btn-submit-batch`
      );

      // Verify zero horizontal overflow across panel and scroll body
      const overflowCheck = await drawer.evaluate((panelEl) => {
        const htmlEl = panelEl as HTMLElement;
        const bodyEl = htmlEl.querySelector(
          ":scope > .clinical-drawer-body"
        ) as HTMLElement;
        return {
          panelScrollWidth: htmlEl.scrollWidth,
          panelClientWidth: htmlEl.clientWidth,
          bodyScrollWidth: bodyEl.scrollWidth,
          bodyClientWidth: bodyEl.clientWidth,
        };
      });
      expect(overflowCheck.panelScrollWidth).toBeLessThanOrEqual(
        overflowCheck.panelClientWidth + 1
      );
      expect(overflowCheck.bodyScrollWidth).toBeLessThanOrEqual(
        overflowCheck.bodyClientWidth + 1
      );
    }
  });
});
