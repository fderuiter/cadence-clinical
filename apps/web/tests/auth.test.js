import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";

describe("useAuthStore - Keycloak & OIDC Authentication Store", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    if (typeof window !== "undefined") {
      delete window.keycloakInstance;
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
});
