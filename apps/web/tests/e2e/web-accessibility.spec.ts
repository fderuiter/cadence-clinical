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
});
