import { describe, it, expect, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { useAuthStore } from "../src/stores/auth";

describe("Router OIDC & RBAC Navigation Guards", () => {
  beforeEach(async () => {
    // Reset Pinia state
    const pinia = createPinia();
    setActivePinia(pinia);

    if (typeof window !== "undefined") {
      delete window.keycloakInstance;
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
      authStore.isDemoMode = true;

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
    it("should allow navigation to /mdr if the user has designer role", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Study Designer"]; // maps to sponsor_designer

      await router.push("/mdr");
      expect(router.currentRoute.value.path).toBe("/mdr");
    });

    it("should allow navigation to /mdr if the user has Sponsor Designer role", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Designer"]; // maps to sponsor_designer

      await router.push("/mdr");
      expect(router.currentRoute.value.path).toBe("/mdr");
    });

    it("should redirect to /forbidden if the user does not have the required roles", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Auditor"]; // does not have designer roles

      await router.push("/mdr");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });

    it("should allow TMF Auditor to access /audit view", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Auditor"]; // maps to auditor & tmf_auditor

      await router.push("/audit");
      expect(router.currentRoute.value.path).toBe("/audit");
    });

    it("should allow Site Investigator (CRC) to access /ecrf view", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Site Investigator"]; // maps to site_investigator & crc

      await router.push("/ecrf");
      expect(router.currentRoute.value.path).toBe("/ecrf");
    });

    it("should allow CRA to access /ctms view", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["CRA"]; // maps to cra & monitor

      await router.push("/ctms");
      expect(router.currentRoute.value.path).toBe("/ctms");
    });

    it("should allow Data Manager to access /rules view", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Data Manager"]; // maps to data_manager

      await router.push("/rules");
      expect(router.currentRoute.value.path).toBe("/rules");
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
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Site Investigator"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ecrf");
    });

    it("should dynamically redirect CRA to /ctms when landing on /", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["CRA"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ctms");
    });

    it("should dynamically redirect Data Manager to /rules when landing on /", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Data Manager"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/rules");
    });

    it("should dynamically redirect TMF Auditor to /audit when landing on /", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Auditor"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/audit");
    });

    it("should dynamically redirect Study Designer to /mdr when landing on /", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Study Designer"];

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/mdr");
    });
  });

  describe("Active Keycloak Session Verification & Role Anti-Spoofing", () => {
    it("should block routing and redirect to login when local storage/session storage values are spoofed but active Keycloak session is missing or invalid", async () => {
      const authStore = useAuthStore();

      // Simulate altered/spoofed local state
      authStore.isAuthenticated = true;
      authStore.isDemoMode = false;
      authStore.rawRoles = ["Sponsor Admin"]; // Spoofed admin role

      // Mock an invalid/missing Keycloak session
      window.keycloakInstance = {
        authenticated: false,
        updateToken: async () => {
          throw new Error("Invalid session");
        },
      };

      await router.push("/mdr");

      // Should be redirected to /login because session is invalid/missing
      expect(router.currentRoute.value.path).toBe("/login");
      expect(router.currentRoute.value.query.redirect).toBe("/mdr");

      // Pinia store state should have been cleaned
      expect(authStore.isAuthenticated).toBe(false);
      expect(authStore.rawRoles).toEqual([]);
    });

    it("should successfully block navigation to restricted route if spoofed local storage roles don't match active Keycloak session roles", async () => {
      const authStore = useAuthStore();

      // Simulate altered/spoofed local state
      authStore.isAuthenticated = true;
      authStore.isDemoMode = false;
      authStore.rawRoles = ["Sponsor Admin"]; // Spoofed admin role

      // Mock active Keycloak session with a different, non-admin role (e.g., CRC)
      window.keycloakInstance = {
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
      };

      await router.push("/mdr");

      // Should be redirected to /forbidden because real role doesn't have access to /mdr
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });
  });
});
