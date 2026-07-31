import { Page, expect } from "@playwright/test";

export class LoginPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto("/login");
  }

  async loginOfflineDemo() {
    const btn = this.page.locator(".btn-login-demo");
    await expect(btn).toBeVisible();
    await btn.click();
    await expect(this.page).not.toHaveURL(/.*login/);
  }
}
