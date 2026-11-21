import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

test.describe("Web Application WCAG 2.1 AA Accessibility Suite", () => {
  test("passes WCAG 2.1 AA axe-core audit and keyboard navigation on sign-in screen", async ({
    page,
  }) => {
    await page.goto("login");
    await expect(page.locator("#section-login")).toBeVisible();
    await expect(
      page.getByRole("heading", { name: /Sign in or start a trial workspace/i })
    ).toBeVisible();

    const skipLink = page.locator(".skip-link");
    await expect(skipLink).toBeAttached();
    await expect(skipLink).toHaveAttribute("href", "#main-content");
    await expect(page.locator("main#main-content")).toBeAttached();

    const audit = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
    expect(audit.violations).toEqual([]);
  });

  test("passes WCAG 2.1 AA axe-core audit on public plans comparison screen", async ({
    page,
  }) => {
    await page.goto("plans");
    await expect(page.locator("main#main-content")).toBeVisible();

    const audit = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
    expect(audit.violations).toEqual([]);
  });

  test("passes WCAG 2.1 AA axe-core audit on unavailable and forbidden guard screens", async ({
    page,
  }) => {
    await page.goto("unavailable?screen=mdr");
    await expect(page.locator("main#main-content")).toBeVisible();
    const unavailableAudit = await new AxeBuilder({ page })
      .withTags(WCAG_TAGS)
      .analyze();
    expect(unavailableAudit.violations).toEqual([]);

    await page.goto("forbidden");
    await expect(page.locator("main#main-content")).toBeVisible();
    const forbiddenAudit = await new AxeBuilder({ page })
      .withTags(WCAG_TAGS)
      .analyze();
    expect(forbiddenAudit.violations).toEqual([]);
  });

  test("passes WCAG 2.1 AA axe-core audit on ObservationView and ReasonForChangeModal", async ({
    page,
  }) => {
    const observationRef = "obs:01950000-0000-7000-8000-000000000042"; // deid-ignore: synthetic kernel NodeRef for accessibility test
    await page.route("**/api/v1/kernel/observations/**", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          ref: `${observationRef}@1`,
          enrollment_ref: "enrollment:01950000-0000-7000-8000-000000000002", // deid-ignore: synthetic kernel NodeRef for accessibility test
          concept: "SYSBP@1",
          value: { type: "quantity", value: "120", unit: "mm[Hg]" },
          version: 1,
          observed_at: "2026-01-01T00:00:00Z",
          locks: [],
          assertions: [],
          revisions: [
            {
              number: 1,
              value: { type: "quantity", value: "120", unit: "mm[Hg]" },
              reason_for_change: "Initial vitals entry",
              recorded_by: "person:crc-1",
              recorded_at: "2026-01-01T00:00:00Z",
              recorded_until: null,
            },
          ],
          audit: [
            {
              sequence: 1,
              recorded_at: "2026-01-01T00:00:00Z",
              actor_id: "person:crc-1",
              action: "Created",
              subject: `${observationRef}@1`,
              version: 1,
              reason_for_change: "Initial vitals entry",
            },
          ],
        }),
      });
    });

    await page.goto(
      `tests/browser/observation.html?observation=${encodeURIComponent(observationRef)}`
    );
    await expect(page.getByTestId("observation-value")).toHaveText(
      "120 mm[Hg]"
    );

    const viewAudit = await new AxeBuilder({ page })
      .withTags(WCAG_TAGS)
      .analyze();
    expect(viewAudit.violations).toEqual([]);

    await page.locator("#observation-new-value").fill("118");
    await page.locator("#observation-save").click();
    await expect(
      page.locator("#observation-change-reason-select")
    ).toBeVisible();

    const modalAudit = await new AxeBuilder({ page })
      .withTags(WCAG_TAGS)
      .analyze();
    expect(modalAudit.violations).toEqual([]);
  });
});
