import { Page, expect } from "@playwright/test";

/**
 * Password for the Keycloak test users seeded in docker/cadence-realm.json.
 * Override with E2E_PASSWORD when running against another realm.
 */
export const E2E_PASSWORD = process.env.E2E_PASSWORD || "password";

/**
 * Signs in through the real Keycloak login form (authorization code + PKCE),
 * the same way a user does. There is no client-side identity to inject: the
 * app only trusts tokens issued by Keycloak.
 */
export async function signInWithKeycloak(
  page: Page,
  username: string,
  password: string = E2E_PASSWORD
): Promise<void> {
  await page.context().clearCookies();
  await page.goto("/cadence-clinical/login");

  const signIn = page.locator(".btn-login-keycloak");
  await expect(signIn).toBeVisible();
  await signIn.click();

  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.locator("#kc-login").click();

  await page.waitForURL(/\/cadence-clinical\//);
  await expect(page).not.toHaveURL(/\/login(\?|$)/);
}
