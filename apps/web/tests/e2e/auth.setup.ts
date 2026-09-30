import { test as setup, expect } from "@playwright/test";
import { signInWithKeycloak } from "./helpers/keycloakLogin";

const authFile = "playwright/.auth/user.json";

/**
 * Signs in once through Keycloak and saves the browser state (the Keycloak SSO
 * cookie) so every project test starts with a real session that the app
 * restores with check-sso. Requires the docker Keycloak realm to be running.
 */
setup("authenticate through Keycloak", async ({ page }) => {
  const username = process.env.E2E_USERNAME || "designer.lead";

  await signInWithKeycloak(page, username);

  await page.goto("/cadence-clinical/");
  await page.waitForLoadState("networkidle");
  await expect(page).not.toHaveURL(/.*login/);

  await page.context().storageState({ path: authFile });
});
