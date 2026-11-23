import { expect, test } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

const WCAG_AA_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

async function completeInitialPinSetupIfOpen(page, pin = "246810") {
  const pinSetupModal = page.locator("#portal-pin-setup-modal");
  const pinUnlockModal = page.locator("#portal-pin-unlock-modal");
  await Promise.race([
    pinSetupModal.waitFor({ state: "visible", timeout: 4000 }),
    pinUnlockModal.waitFor({ state: "visible", timeout: 4000 }),
  ]).catch(() => {});

  if (await pinSetupModal.isVisible().catch(() => false)) {
    await page.fill("#setup-pin", pin);
    await page.fill("#confirm-setup-pin", pin);
    await page.click("#btn-pin-setup-confirm");
    await expect(pinSetupModal).toBeHidden();
  } else if (await pinUnlockModal.isVisible().catch(() => false)) {
    await page.fill("#unlock-pin", pin);
    await page.click("#btn-pin-unlock-confirm");
    await expect(pinUnlockModal).toBeHidden();
  }
}

test.describe("Production Build Screen Gating & Zero-404 Guarantee", () => {
  test.use({
    baseURL: "http://127.0.0.1:5174/subject-portal/",
    serviceWorkers: "block",
  });

  test("routes unauthenticated visitors on / and unbacked screens to /unavailable with Sign In and zero interop or econsent requests", async ({
    page,
  }, testInfo) => {
    const unmountedRequests = [];
    page.on("request", (req) => {
      const url = req.url();
      if (
        url.includes("/api/v1/interop/") ||
        url.includes("/api/v1/econsent/")
      ) {
        unmountedRequests.push(url);
      }
    });

    await page.goto("consent");
    await expect(page.locator("#view-unavailable")).toBeVisible();
    await expect(page.locator("#btn-unavailable-signin")).toBeVisible();
    await expect(page.locator('.nav-tabs [role="tab"]')).toHaveCount(0);
    expect(page.url()).toContain("/unavailable");
    expect(page.url()).not.toContain("/tasks");
    expect(unmountedRequests).toEqual([]);

    const axeResults = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(axeResults.violations).toEqual([]);

    await page.screenshot({
      path: testInfo.outputPath("subject-portal-unavailable.png"),
    });
  });

  test("routes authenticated participants on / to /wallet, hides unbacked tabs, and makes zero interop or econsent requests", async ({
    page,
  }, testInfo) => {
    const unmountedRequests = [];
    await page.route("**/api/**", async (route) => {
      const url = route.request().url();
      if (
        url.includes("/api/v1/interop/") ||
        url.includes("/api/v1/econsent/")
      ) {
        unmountedRequests.push(url);
        await route.fulfill({
          status: 404,
          contentType: "application/json",
          body: JSON.stringify({ error: "Not mounted" }),
        });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          wallet: {
            id: "wal_prod_001",
            studyId: "CADENCE-101",
            siteId: "SITE-101",
            currency: "USD",
            currentBalance: 120.0,
            lifetimeStipends: 240.0,
            lifetimeReimbursements: 0,
            lifetimeDisbursements: 120.0,
            taxableYtd: 240.0,
            is1099ThresholdExceeded: false,
            w9Status: "NOT_REQUIRED",
            achAccountMask: "ACH-***4821",
          },
          recentTransactions: [],
          expenseClaims: [],
        }),
      });
    });

    await page.addInitScript(() => {
      sessionStorage.setItem("mock_user_id", "subject.site101");
      sessionStorage.setItem("mock_token", "mock_jwt_token_prod_101"); // pragma: allowlist secret
    });

    await page.goto("");
    await completeInitialPinSetupIfOpen(page);

    await expect(page.locator("#view-wallet")).toBeVisible();
    await expect(page.locator("#btn-tab-btn-wallet")).toBeVisible();
    await expect(page.locator("#tab-btn-tasks")).toHaveCount(0);
    await expect(page.locator("#tab-btn-compliance")).toHaveCount(0);
    await expect(page.locator("#tab-btn-inbox")).toHaveCount(0);
    await expect(page.locator("#tab-btn-consent")).toHaveCount(0);
    await expect(page.locator("#tab-btn-lay-summaries")).toHaveCount(0);
    expect(page.url()).toContain("/wallet");
    expect(unmountedRequests).toEqual([]);

    const walletAxe = await new AxeBuilder({ page })
      .withTags(WCAG_AA_TAGS)
      .analyze();
    expect(walletAxe.violations).toEqual([]);

    await page.screenshot({
      path: testInfo.outputPath("subject-portal-gated-wallet.png"),
    });

    // Direct navigation to unbacked /consent shows UnavailableView with Return to Participant Wallet
    await page.goto("consent");
    await completeInitialPinSetupIfOpen(page);
    await expect(page.locator("#view-unavailable")).toBeVisible();
    await expect(page.locator("#btn-unavailable-return-tasks")).toContainText(
      "Return to Participant Wallet"
    );
    await page.click("#btn-unavailable-return-tasks");
    await expect(page.locator("#view-wallet")).toBeVisible();
    expect(unmountedRequests).toEqual([]);
  });
});
