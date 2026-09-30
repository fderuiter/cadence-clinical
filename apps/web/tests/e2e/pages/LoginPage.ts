import { Page } from "@playwright/test";
import { signInWithKeycloak } from "../helpers/keycloakLogin";

export class LoginPage {
  constructor(private page: Page) {}

  async goto() {
    await this.page.goto("/cadence-clinical/login");
  }

  async loginWithKeycloak(username: string, password?: string) {
    await signInWithKeycloak(this.page, username, password);
  }
}
