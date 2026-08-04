import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";

describe("useAuthStore - Keycloak & OIDC Authentication Store", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    if (typeof window !== "undefined") {
      delete window.keycloakInstance;
      window.localStorage.clear();
      window.sessionStorage.clear();
    }
  });

  describe("Fallback Offline / Demo Mode Behavior", () => {
    it("should initialize in demo mode with fallback identity and normalized roles", () => {
      const authStore = useAuthStore();

      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.isDemoMode).toBe(true);
      expect(authStore.token).toBeNull();

      // Check fallback identity details
      expect(authStore.identity).toEqual({
        username: "fderuiter",
        email: "fderuiter@example.com",
        firstName: "Frans",
        lastName: "de Ruiter",
        id: "fderuiter-id-12345",
      });

      // Check fallback normalized roles
      expect(authStore.normalizedRoles).toEqual(["monitor", "sponsor_admin"]);
    });

    it("allows mockup login and logout when keycloak is not initialized", async () => {
      const authStore = useAuthStore();

      await authStore.login();
      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.isDemoMode).toBe(true);

      await authStore.logout();
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.isDemoMode).toBe(true);
    });
  });

  describe("OIDC Integration and Keycloak Claims Normalization", () => {
    it("sets state and parses claims correctly from a standard keycloak token object", () => {
      const authStore = useAuthStore();

      const mockKeycloak = {
        authenticated: true,
        token: "mock-access-token-xyz-123",
        idToken: "mock-id-token-abc",
        refreshToken: "mock-refresh-token-pqr",
        tokenParsed: {
          sub: "user-uuid-9999",
          preferred_username: "testuser",
          email: "testuser@example.com",
          given_name: "Test",
          family_name: "User",
          realm_access: {
            roles: [
              "Sponsor Admin",
              "CRA",
              "Data Manager",
              "Site Investigator",
            ],
          },
        },
      };

      authStore.setAuth(mockKeycloak);

      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.isDemoMode).toBe(false);
      expect(authStore.token).toBe("mock-access-token-xyz-123");
      expect(authStore.idToken).toBe("mock-id-token-abc");
      expect(authStore.refreshToken).toBe("mock-refresh-token-pqr");

      // Verify identity parsing
      expect(authStore.identity).toEqual({
        username: "testuser",
        email: "testuser@example.com",
        firstName: "Test",
        lastName: "User",
        id: "user-uuid-9999",
      });

      // Verify exact role normalization mapping
      expect(authStore.normalizedRoles).toEqual([
        "sponsor_admin",
        "cra",
        "data_manager",
        "site_investigator",
      ]);
    });

    it("normalizes roles with extra whitespaces, mixed cases, and special characters", () => {
      const authStore = useAuthStore();

      const mockKeycloak = {
        authenticated: true,
        tokenParsed: {
          realm_access: {
            roles: [
              "  Sponsor Admin  ",
              "cRa",
              "quality_manager",
              "QA-LEAD",
              "quality oversight",
              "Subject",
              "Auditor",
            ],
          },
        },
      };

      authStore.setAuth(mockKeycloak);

      expect(authStore.normalizedRoles).toEqual([
        "sponsor_admin",
        "cra",
        "quality_manager",
        "qa_lead",
        "quality_oversight",
        "subject",
        "auditor",
      ]);
    });

    it("normalizes Sponsor Designer, study_designer, and designer roles into the canonical sponsor_designer token", () => {
      const authStore = useAuthStore();

      const mockKeycloak = {
        authenticated: true,
        tokenParsed: {
          realm_access: {
            roles: ["Sponsor Designer", "study_designer", "designer"],
          },
        },
      };

      authStore.setAuth(mockKeycloak);

      expect(authStore.normalizedRoles).toEqual([
        "sponsor_designer",
        "sponsor_designer",
        "sponsor_designer",
      ]);
    });

    it("resets authentication state when setAuth is called with a falsy or unauthenticated object", () => {
      const authStore = useAuthStore();

      // First set authenticated
      authStore.setAuth({
        authenticated: true,
        token: "token",
        tokenParsed: {
          realm_access: { roles: ["CRA"] },
        },
      });
      expect(authStore.isAuthenticated).toBe(true);

      // Now clear auth
      authStore.setAuth(null);
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.token).toBeNull();
      expect(authStore.identity).toBeNull();
      expect(authStore.normalizedRoles).toEqual([]);
    });
  });

  describe("Production Lockdown Constraints", () => {
    it("should refuse to login with offline mock in production environments", async () => {
      const authStore = useAuthStore();

      // Stub production environment
      const originalProd = import.meta.env.PROD;
      const originalMode = import.meta.env.MODE;
      import.meta.env.PROD = true;
      import.meta.env.MODE = "production";

      authStore.isDemoMode = true;

      await expect(authStore.login()).rejects.toThrow(
        "Offline login fallback is disabled in production environments."
      );

      // Clean up
      import.meta.env.PROD = originalProd;
      import.meta.env.MODE = originalMode;
    });

    it("should refuse to logout with offline mock in production environments", async () => {
      const authStore = useAuthStore();

      // Stub production environment
      const originalProd = import.meta.env.PROD;
      const originalMode = import.meta.env.MODE;
      import.meta.env.PROD = true;
      import.meta.env.MODE = "production";

      authStore.isDemoMode = true;

      await expect(authStore.logout()).rejects.toThrow(
        "Offline logout fallback is disabled in production environments."
      );

      // Clean up
      import.meta.env.PROD = originalProd;
      import.meta.env.MODE = originalMode;
    });
  });

  describe("Demo Mode Build Configurations", () => {
    it("should permit login and logout fallbacks in demo build mode even if PROD is true", async () => {
      const authStore = useAuthStore();

      // Stub demo environment with PROD = true
      const originalProd = import.meta.env.PROD;
      const originalMode = import.meta.env.MODE;
      import.meta.env.PROD = true;
      import.meta.env.MODE = "demo";

      authStore.isDemoMode = true;

      // This should succeed because of the check excluding MODE === "demo" from isProduction
      await authStore.login();
      expect(authStore.isAuthenticated).toBe(true);
      expect(authStore.rawRoles).toContain("Sponsor Designer");

      await authStore.logout();
      expect(authStore.isAuthenticated).toBe(false);

      // Clean up
      import.meta.env.PROD = originalProd;
      import.meta.env.MODE = originalMode;
    });
  });
});
