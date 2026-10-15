import { test, expect } from "@playwright/test";

/**
 * The browser half of the browser-to-database check (ADR-2620). KernelObservationHostPostgresTests seeds an
 * observation, runs this spec against it, and then reads the correction and its reason back from PostgreSQL.
 * Run only by that test, which sets OBSERVATION_REF and starts the signing proxy the dev server forwards /api to.
 */
const observationRef = process.env.OBSERVATION_REF ?? "";
const reasonDetail = process.env.CORRECTION_DETAIL ?? "source reads 128";

test("corrects an observation with a reason and shows it in the kernel audit trail", async ({
  page,
}) => {
  test.skip(!observationRef, "Run by KernelObservationHostPostgresTests");

  await page.goto(
    `tests/browser/observation.html?observation=${encodeURIComponent(observationRef)}`
  );
  await expect(page.getByTestId("observation-value")).toHaveText("182");
  await expect(page.getByTestId("observation-version")).toHaveText("1");

  await page.locator("#observation-new-value").fill("128");
  await page.locator("#observation-save").click();
  await page
    .locator("#observation-change-reason-select")
    .selectOption("Typographical Error");
  await page.locator("#observation-change-reason-text").fill(reasonDetail);
  await page.locator("#observation-btn-save-change").click();

  await expect(page.getByTestId("observation-outcome")).toContainText(
    "Correction saved"
  );
  await expect(page.getByTestId("observation-value")).toHaveText("128");
  await expect(page.getByTestId("observation-version")).toHaveText("2");
  await expect(page.getByTestId("observation-audit")).toContainText(
    `Typographical Error: ${reasonDetail}`
  );
});
