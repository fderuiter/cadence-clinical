import { test as setup, expect } from "@playwright/test";

const authFile = "playwright/.auth/user.json";

setup("authenticate as admin", async ({ page }) => {
  // Navigate to login page under the /cadence-clinical/ base path
  await page.goto("/cadence-clinical/login");

  // Inject authentication state directly into localStorage to simulate OIDC session
  await page.evaluate(() => {
    window.localStorage.setItem(
      "cadence_auth",
      JSON.stringify({
        isAuthenticated: true,
        accessToken: "mock-access-token-xyz-123",
        idToken: "mock-id-token-abc",
        refreshToken: "mock-refresh-token-pqr",
        user: {
          username: "admin@cadence.clinical",
          email: "admin@cadence.clinical",
          firstName: "Admin",
          lastName: "Cadence",
          id: "admin-id-123",
        },
        rawRoles: [
          "Sponsor Admin",
          "Sponsor Designer",
          "CRA",
          "Data Manager",
          "Site Investigator",
          "Auditor",
        ],
        isDemoMode: true,
      })
    );
  });

  // Navigate to main workspace page
  await page.goto("/cadence-clinical/");
  await page.waitForLoadState("networkidle");

  // Ensure that we are not on the login page anymore and are authenticated
  await expect(page).not.toHaveURL(/.*login/);

  // Save storage state for all project tests
  await page.context().storageState({ path: authFile });
});
