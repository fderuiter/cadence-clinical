import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { setKeycloakClient } from "../src/auth/keycloak.js";
import { useAuthStore } from "../src/stores/auth";

describe("Router OIDC & RBAC Navigation Guards", () => {
  beforeEach(async () => {
    // Reset Pinia state
    const pinia = createPinia();
    setActivePinia(pinia);

    setKeycloakClient(null);
    if (typeof window !== "undefined") {
      window.sessionStorage.clear();
      window.localStorage.clear();
    }

    // Always reset router to initial route before each test
    await router.push("/login");
  });

  describe("Unauthenticated Access Restrictions", () => {
    it("should redirect unauthenticated users to /login when attempting to access a protected route", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = false;

      // Navigate to protected MDR view
      await router.push("/mdr");

      expect(router.currentRoute.value.path).toBe("/login");
      expect(router.currentRoute.value.query.redirect).toBe("/mdr");
    });

    it("should allow unauthenticated users to access /login and /forbidden without redirect loops", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = false;

      await router.push("/login");
      expect(router.currentRoute.value.path).toBe("/login");

      await router.push("/forbidden");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });
  });

  describe("Role-Based Authorization Guards", () => {
    it("should route a permitted designer to /unavailable for /mdr, which has no .NET backend yet", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Study Designer"]; // maps to sponsor_designer

      await router.push("/mdr");
      expect(router.currentRoute.value.path).toBe("/unavailable");
      expect(router.currentRoute.value.query.screen).toBe("mdr");
    });

    it("should redirect to /forbidden if the user does not have the required roles", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Auditor"]; // does not have designer roles

      await router.push("/mdr");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });

    it("should route TMF Auditor to /unavailable for /audit, which has no .NET backend yet", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Auditor"]; // maps to auditor & tmf_auditor

      await router.push("/audit");
      expect(router.currentRoute.value.path).toBe("/unavailable");
      expect(router.currentRoute.value.query.screen).toBe("audit");
    });

    it("should allow Site Investigator (CRC) to access /ecrf view", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Site Investigator"]; // maps to site_investigator & crc

      await router.push("/ecrf");
      expect(router.currentRoute.value.path).toBe("/ecrf");
    });

    it("should allow CRA to access /ctms view", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"]; // maps to cra & monitor

      await router.push("/ctms");
      expect(router.currentRoute.value.path).toBe("/ctms");
    });

    it("should route Data Manager to /unavailable for /rules, which has no .NET backend yet", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Data Manager"]; // maps to data_manager

      await router.push("/rules");
      expect(router.currentRoute.value.path).toBe("/unavailable");
      expect(router.currentRoute.value.query.screen).toBe("rules");
    });

    it("should still show /forbidden, not /unavailable, when the role is missing", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Auditor"];

      await router.push("/rules");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });

    it("should leave unknown paths alone rather than calling them unavailable", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      await router.push("/no-such-screen");
      expect(router.currentRoute.value.name).toBeUndefined();
    });
  });

  describe("Dynamic Landing Route Redirections on Root (/) Navigation", () => {
    it("should redirect unauthenticated users on root navigation to /login", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = false;

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/login");
    });

    it("should dynamically redirect CRC (Site Investigator) to /ecrf when landing on /", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Site Investigator"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ecrf");
    });

    it("should dynamically redirect CRA to /ctms when landing on /", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ctms");
    });

    it("should land Data Manager on /ecrf while /rules has no .NET backend", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Data Manager"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ecrf");
    });

    it("should land TMF Auditor on /unavailable while no auditor screen has a .NET backend", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Auditor"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/unavailable");
      expect(router.currentRoute.value.query.screen).toBeUndefined();
    });

    it("should land Study Designer on /unavailable while no designer screen has a .NET backend", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Study Designer"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/unavailable");
    });
  });

  describe("Active Keycloak Session Verification & Role Anti-Spoofing", () => {
    it("should redirect to login and clear local state when the Keycloak session can no longer be refreshed", async () => {
      const authStore = useAuthStore();

      // Simulate locally altered store state
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Sponsor Admin"]; // Spoofed admin role

      // Mock an invalid/missing Keycloak session
      setKeycloakClient({
        authenticated: false,
        updateToken: async () => {
          throw new Error("Invalid session");
        },
      });

      await router.push("/mdr");

      // Should be redirected to /login because session is invalid/missing
      expect(router.currentRoute.value.path).toBe("/login");
      expect(router.currentRoute.value.query.redirect).toBe("/mdr");

      // Pinia store state should have been cleaned
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.rawRoles).toEqual([]);
    });

    it("should authorize against the Keycloak token roles, not locally altered roles", async () => {
      const authStore = useAuthStore();

      // Simulate locally altered store state
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Sponsor Admin"]; // Spoofed admin role

      // Mock active Keycloak session with a different, non-admin role (e.g., CRC)
      setKeycloakClient({
        authenticated: true,
        token: "real-token",
        tokenParsed: {
          sub: "user-id",
          preferred_username: "user",
          realm_access: {
            roles: ["Site Investigator"], // Actual role is CRC, not admin/designer
          },
        },
        updateToken: async () => true,
      });

      await router.push("/mdr");

      // Should be redirected to /forbidden because real role doesn't have access to /mdr
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });
  });
});
