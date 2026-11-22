import * as fs from "node:fs";
import * as path from "node:path";
import { fileURLToPath } from "node:url";
import { test, expect, type Page } from "@playwright/test";
import {
  createWcag22AxeBuilder,
  formatAccessibilityViolations,
  getControlBorderContrast,
  runWcag22Audit,
} from "ui/playwright";
import {
  ALWAYS_AVAILABLE,
  AVAILABLE_SCREENS,
  DEMO_AVAILABLE_SCREENS,
} from "../../src/router/availability";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const REPO_ROOT = path.resolve(__dirname, "../../../..");
const ROUTER_INDEX_PATH = path.resolve(__dirname, "../../src/router/index.js");
const DEMO_BASELINE_RELATIVE_PATH =
  "ArchitectureBaselines/demo-wcag-violations.baseline.txt";
const DEMO_BASELINE_PATH = path.join(REPO_ROOT, DEMO_BASELINE_RELATIVE_PATH);

interface ParsedRouteDefinition {
  name: string;
  path: string;
  isRedirect: boolean;
}

function loadRouterDefinitions(): ParsedRouteDefinition[] {
  const source = fs.readFileSync(ROUTER_INDEX_PATH, "utf8");
  const routesStart = source.indexOf("export const routes = [");
  const routesEnd = source.indexOf("\n];", routesStart);
  if (routesStart < 0 || routesEnd < 0) {
    throw new Error(
      `Unable to locate 'export const routes = [...]' in ${ROUTER_INDEX_PATH}`
    );
  }

  const routesBlock = source.slice(routesStart, routesEnd + 3);
  const entryPattern =
    /\{\s*(?:\/\/[^\n]*\s*)*path:\s*"([^"]+)",\s*name:\s*"([^"]+)",\s*(redirect|component):/g;
  const parsed: ParsedRouteDefinition[] = [];
  for (const match of routesBlock.matchAll(entryPattern)) {
    parsed.push({
      path: match[1],
      name: match[2],
      isRedirect: match[3] === "redirect",
    });
  }
  return parsed;
}

const ALL_ROUTER_ROUTES = loadRouterDefinitions();
const REDIRECT_ROUTES = ALL_ROUTER_ROUTES.filter((r) => r.isRedirect);
const COMPONENT_ROUTES = ALL_ROUTER_ROUTES.filter((r) => !r.isRedirect);

const LIVE_ROUTES = COMPONENT_ROUTES.filter(
  (r) => AVAILABLE_SCREENS.has(r.name) || ALWAYS_AVAILABLE.has(r.name)
);

const DEMO_ONLY_ROUTES = COMPONENT_ROUTES.filter(
  (r) =>
    DEMO_AVAILABLE_SCREENS.has(r.name) &&
    !AVAILABLE_SCREENS.has(r.name) &&
    !ALWAYS_AVAILABLE.has(r.name)
);

const ROUTE_NAVIGATION_FIXTURES: Record<
  string,
  {
    hashPath: string;
    expectedHashPrefixes?: string[];
    readySelector?: string;
  }
> = {
  overview: { hashPath: "/overview", readySelector: "#overview-hub-root" },
  plans: { hashPath: "/plans", readySelector: ".plans-view" },
  "trial-onboarding": {
    hashPath: "/onboarding/trial",
    readySelector: "#section-trial-setup",
  },
  login: { hashPath: "/login", readySelector: "#section-login" },
  forbidden: { hashPath: "/forbidden", readySelector: "#section-forbidden" },
  unavailable: {
    hashPath: "/unavailable?screen=mdr",
    readySelector: "#section-unavailable",
  },
  study: { hashPath: "/studies/DEMO-01", readySelector: "#study-canvas" },
  observation: {
    hashPath:
      "/observations/observation%3A00000000-0000-0000-0000-000000000001", // deid-ignore
    expectedHashPrefixes: ["#/observations/", "#/studies/"],
    readySelector: '[data-testid="observation-concept"]',
  },
  ecrf: { hashPath: "/ecrf", readySelector: "main" },
  ctms: { hashPath: "/ctms", readySelector: "#section-ctms" },
  ssu: { hashPath: "/ssu", readySelector: "#section-ctms" },
  etmf: { hashPath: "/etmf", readySelector: ".document-manager-layout" },
  training: { hashPath: "/training", readySelector: "#section-study-training" },
};

function resolveConcreteRouteTarget(route: ParsedRouteDefinition): {
  hashPath: string;
  expectedHashPrefixes: string[];
  readySelector?: string;
} {
  const fixture = ROUTE_NAVIGATION_FIXTURES[route.name];
  if (fixture) {
    const basePath = fixture.hashPath.split("?")[0];
    return {
      hashPath: fixture.hashPath,
      expectedHashPrefixes: fixture.expectedHashPrefixes ?? [`#${basePath}`],
      readySelector: fixture.readySelector,
    };
  }

  const resolvedPath = route.path.replace(/\/:[^/]+\?/g, "");
  if (resolvedPath.includes(":")) {
    throw new Error(
      `Route '${route.name}' (${route.path}) requires an explicit parameter fixture in ROUTE_NAVIGATION_FIXTURES.`
    );
  }
  return {
    hashPath: resolvedPath,
    expectedHashPrefixes: [`#${resolvedPath}`],
  };
}

async function gotoHashRoute(page: Page, routePath: string) {
  await page.goto(`#${routePath}`);
}

async function setDocumentTheme(page: Page, theme: "light" | "dark") {
  await page.evaluate((activeTheme) => {
    document.documentElement.setAttribute("data-theme", activeTheme);
  }, theme);
}

async function waitForRouteSettled(
  page: Page,
  target: {
    hashPath: string;
    expectedHashPrefixes: string[];
    readySelector?: string;
  }
) {
  await gotoHashRoute(page, target.hashPath);
  if (target.readySelector) {
    await expect(page.locator(target.readySelector).first()).toBeVisible();
  } else {
    await expect(
      page.locator("#app main, #study-canvas").first()
    ).toBeVisible();
  }
  await page.waitForLoadState("networkidle").catch(() => {});
  await page.waitForTimeout(120);

  const currentHash = await page.evaluate(() => window.location.hash);
  expect(
    target.expectedHashPrefixes.some((prefix) =>
      currentHash.startsWith(prefix)
    ),
    `Expected hash to match ${target.expectedHashPrefixes.join(" or ")} when auditing ${target.hashPath}, but redirected to ${currentHash}`
  ).toBe(true);
}

async function expectNoWcagViolations(
  page: Page,
  contextLabel: string,
  options: { checkControlBorders?: boolean; include?: string | string[] } = {}
) {
  const { violations, borderViolations } = await runWcag22Audit(page, options);
  const summary = formatAccessibilityViolations(violations, borderViolations);

  expect(
    violations,
    `WCAG 2.2 AA violations on ${contextLabel}:\n${summary}`
  ).toEqual([]);
  expect(
    borderViolations,
    `SC 1.4.11 control border contrast violations on ${contextLabel}:\n${summary}`
  ).toEqual([]);
}

function normalizeTargetSelector(target: unknown): string {
  if (!Array.isArray(target)) {
    return String(target ?? "").trim();
  }
  return target
    .map((segment) =>
      Array.isArray(segment) ? segment.join(" ") : String(segment)
    )
    .join(" >> ")
    .replace(/\s+/g, " ")
    .trim();
}

function readBaselineEntries(filePath: string): string[] {
  if (!fs.existsSync(filePath)) {
    return [];
  }
  return fs
    .readFileSync(filePath, "utf8")
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));
}

function diffMultiset(before: string[], after: string[]): string[] {
  const counts = new Map<string, number>();
  for (const entry of before) {
    counts.set(entry, (counts.get(entry) ?? 0) + 1);
  }
  const added: string[] = [];
  for (const entry of after) {
    const remaining = counts.get(entry) ?? 0;
    if (remaining > 0) {
      counts.set(entry, remaining - 1);
    } else {
      added.push(entry);
    }
  }
  return added.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

test.describe("Clinical Web Application WCAG 2.2 Level AA Contrast & Control Gates", () => {
  test("dynamically classifies all router definitions from availability.js and index.js with zero uncategorized routes", () => {
    expect(AVAILABLE_SCREENS.size).toBe(7);
    expect(ALWAYS_AVAILABLE.size).toBe(7);
    expect(REDIRECT_ROUTES.map((r) => r.name)).toEqual(["landing"]);
    expect(LIVE_ROUTES.length).toBe(13);
    expect(DEMO_ONLY_ROUTES.length).toBe(28);
    expect(
      LIVE_ROUTES.length + DEMO_ONLY_ROUTES.length + REDIRECT_ROUTES.length
    ).toBe(ALL_ROUTER_ROUTES.length);

    expect(LIVE_ROUTES.map((r) => r.name)).toEqual(
      expect.arrayContaining([
        "overview",
        "plans",
        "trial-onboarding",
        "login",
        "forbidden",
        "unavailable",
        "study",
        "observation",
        "ecrf",
        "ctms",
        "ssu",
        "etmf",
        "training",
      ])
    );
  });

  test("enforces zero WCAG 2.2 AA and SC 1.4.11 violations across all 13 live routes in light and dark themes", async ({
    page,
  }) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width: 1440, height: 900 });

    // Prime workspace session on /overview before iterating all 13 live routes
    await gotoHashRoute(page, "/overview");
    await expect(page.locator("#overview-hub-root")).toBeVisible();

    for (const theme of ["light", "dark"] as const) {
      for (const route of LIVE_ROUTES) {
        const target = resolveConcreteRouteTarget(route);
        await waitForRouteSettled(page, target);
        await setDocumentTheme(page, theme);
        await expectNoWcagViolations(
          page,
          `Live route '${route.name}' (${target.hashPath}) [${theme}]`
        );
      }
    }

    // Verify root '/' landing redirect resolves to an available live route with 0 violations
    await setDocumentTheme(page, "light");
    await gotoHashRoute(page, "/");
    await expect(
      page.locator("#app main, #study-canvas").first()
    ).toBeVisible();
    await expectNoWcagViolations(page, "Landing redirect ('/') [light]");
  });

  test("verifies /etmf H44 filter labels, G208 label-in-name buttons, and C42 >= 24x24px compact tree export targets", async ({
    page,
  }) => {
    const captureScreenshots = process.env.CAPTURE_OVERLAY_SCREENSHOTS === "1";
    await page.setViewportSize({ width: 1440, height: 900 });
    await gotoHashRoute(page, "/etmf");
    await expect(page.locator(".document-manager-layout")).toBeVisible();

    // 1. SC 3.3.2 / SC 4.1.2 (Technique H44): programmatic <label for> on search input and zone filter select
    const labelDiagnostics = await page.evaluate(() => {
      const searchInput = document.querySelector(
        ".search-input"
      ) as HTMLInputElement | null;
      const zoneSelect = document.querySelector(
        ".zone-filter-select"
      ) as HTMLSelectElement | null;
      return {
        searchId: searchInput?.id ?? "",
        searchLabels: Array.from(searchInput?.labels ?? []).map((l) =>
          l.textContent?.trim()
        ),
        zoneId: zoneSelect?.id ?? "",
        zoneLabels: Array.from(zoneSelect?.labels ?? []).map((l) =>
          l.textContent?.trim()
        ),
      };
    });
    expect(labelDiagnostics.searchId).toBe("tmf-binder-search-input");
    expect(labelDiagnostics.searchLabels).toEqual([
      "Search TMF binder zones, sections, or artifacts",
    ]);
    expect(labelDiagnostics.zoneId).toBe("tmf-zone-filter-select");
    expect(labelDiagnostics.zoneLabels).toEqual([
      "Filter TMF binder by DIA zone",
    ]);

    // 2. SC 2.5.3 (Technique G208): button accessible names contain visible text labels without mismatched aria-label overrides
    const uploadSingleBtn = page.locator(".upload-trigger-btn");
    const batchIngestBtn = page.locator(".batch-upload-trigger-btn");
    const exportBinderBtn = page.locator(".btn-export-full-binder");
    await expect(uploadSingleBtn).toBeVisible();
    await expect(batchIngestBtn).toBeVisible();
    await expect(exportBinderBtn).toBeVisible();
    await expect(uploadSingleBtn).not.toHaveAttribute("aria-label");
    await expect(batchIngestBtn).not.toHaveAttribute("aria-label");
    await expect(exportBinderBtn).not.toHaveAttribute("aria-label");
    await expect(uploadSingleBtn).toContainText("Upload Single Document");
    await expect(batchIngestBtn).toContainText("Batch Ingestion");
    await expect(exportBinderBtn).toContainText("Export Complete Study Binder");

    // 3. SC 2.5.8 (Technique C42) + UX compact tree row guardrail: expand zone & section and measure .btn-node-export
    const firstZone = page.locator(".zone-header").first();
    await expect(firstZone).toBeVisible();
    await firstZone.click();
    const firstSection = page.locator(".section-header").first();
    await expect(firstSection).toBeVisible();
    await firstSection.click();

    const exportButtons = page.locator(".btn-node-export");
    const exportCount = await exportButtons.count();
    expect(exportCount).toBeGreaterThan(0);

    for (let i = 0; i < exportCount; i++) {
      const btn = exportButtons.nth(i);
      if (!(await btn.isVisible())) continue;
      const box = await btn.boundingBox();
      expect(box).not.toBeNull();
      expect(box!.width).toBeGreaterThanOrEqual(24);
      expect(box!.height).toBeGreaterThanOrEqual(24);
    }

    // UX guardrail: tree row height remains compact for dense eTMF folders
    const zoneRowBox = await firstZone.boundingBox();
    expect(zoneRowBox).not.toBeNull();
    expect(zoneRowBox!.height).toBeLessThanOrEqual(42);

    await setDocumentTheme(page, "light");
    await expectNoWcagViolations(
      page,
      "/etmf expanded TMF binder tree (light)"
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-wcag22-light.png" });
    }

    await setDocumentTheme(page, "dark");
    await expectNoWcagViolations(page, "/etmf expanded TMF binder tree (dark)");
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/etmf-wcag22-dark.png" });
    }
    await setDocumentTheme(page, "light");
  });

  test("scans all 28 pre-release demo routes against shrink-only ArchitectureBaselines/demo-wcag-violations.baseline.txt", async ({
    page,
  }) => {
    test.setTimeout(240_000);
    await page.setViewportSize({ width: 1440, height: 900 });

    await gotoHashRoute(page, "/overview");
    await expect(page.locator("#overview-hub-root")).toBeVisible();
    await setDocumentTheme(page, "light");

    const actualEntries: string[] = [];

    for (const route of DEMO_ONLY_ROUTES) {
      const target = resolveConcreteRouteTarget(route);
      await waitForRouteSettled(page, target);
      const results = await createWcag22AxeBuilder(page).analyze();
      for (const violation of results.violations) {
        for (const node of violation.nodes) {
          actualEntries.push(
            `${route.name}|${violation.id}|${normalizeTargetSelector(node.target)}`
          );
        }
      }
    }

    actualEntries.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));

    if (process.env.CADENCE_UPDATE_BASELINE === "1") {
      const header = [
        "# Pre-release demo routes WCAG 2.2 Level A & AA violation ratchet baseline.",
        "# Format: <routeName>|<axeRuleId>|<targetSelector> (sorted ordinally; enforced by web-accessibility.spec.ts and BaselineRatchetService).",
        "# Live routes (AVAILABLE_SCREENS + ALWAYS_AVAILABLE) enforce zero violations and may never appear in this file.",
        "# Regenerate after fixing demo defects: CADENCE_UPDATE_BASELINE=1 pnpm --filter web test:e2e",
      ];
      fs.mkdirSync(path.dirname(DEMO_BASELINE_PATH), { recursive: true });
      fs.writeFileSync(
        DEMO_BASELINE_PATH,
        [...header, ...actualEntries].join("\n") + "\n",
        "utf8"
      );
      return;
    }

    const baselineEntries = readBaselineEntries(DEMO_BASELINE_PATH);

    // Ensure no live route ever appears in the demo baseline
    const leakedLiveEntries = baselineEntries.filter((entry) => {
      const routeName = entry.split("|")[0];
      return (
        AVAILABLE_SCREENS.has(routeName) || ALWAYS_AVAILABLE.has(routeName)
      );
    });
    expect(
      leakedLiveEntries,
      `Live routes must never appear in ${DEMO_BASELINE_RELATIVE_PATH}`
    ).toEqual([]);

    const newViolations = diffMultiset(baselineEntries, actualEntries);
    const staleEntries = diffMultiset(actualEntries, baselineEntries);

    expect(
      newViolations,
      [
        `Detected ${newViolations.length} NEW WCAG 2.2 AA violation(s) across pre-release demo routes not recorded in ${DEMO_BASELINE_RELATIVE_PATH}:`,
        ...newViolations.map((entry) => `  + ${entry}`),
        "Fix the contrast, keyboard, label, or target-size regression instead of growing the baseline.",
      ].join("\n")
    ).toEqual([]);

    expect(
      staleEntries,
      [
        `${staleEntries.length} baseline entr(y/ies) in ${DEMO_BASELINE_RELATIVE_PATH} no longer occur — shrink the baseline:`,
        ...staleEntries.map((entry) => `  - ${entry}`),
        "Run: CADENCE_UPDATE_BASELINE=1 pnpm --filter web test:e2e",
      ].join("\n")
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

    const taskLaunchBorderRatio = await getControlBorderContrast(
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
      const inputBorderRatio = await getControlBorderContrast(
        page,
        ".clinical-input input, .clinical-input select, .clinical-input textarea"
      );
      expect(inputBorderRatio).toBeGreaterThanOrEqual(3);
    }

    const radioGrid = page.locator(".clinical-radio-grid");
    if ((await radioGrid.count()) > 0) {
      const radioBorderRatio = await getControlBorderContrast(
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
      "DataLockView ClinicalModal (/data-lock)",
      { checkControlBorders: false }
    );
    if (captureScreenshots) {
      await page.screenshot({ path: "/tmp/datalock-clinical-modal.png" });
    }

    await page.keyboard.press("Escape");
    await expect(dataLockModal).toBeHidden();
  });
});
