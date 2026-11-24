import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { setKeycloakClient } from "../src/auth/keycloak.js";
import { useAuthStore } from "../src/stores/auth";
import { useOnboardingStore } from "../src/stores/onboarding.js";
import { useStudyCanvasStore } from "../src/stores/studyCanvas";

describe("Router OIDC & RBAC Navigation Guards", () => {
  beforeEach(async () => {
    vi.unstubAllEnvs();
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

  afterEach(() => {
    vi.unstubAllEnvs();
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

    it("should redirect Site Investigator (CRC) from /ecrf to Clinical Canvas Conduct outside demo mode while preserving /ecrf in demo mode", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Site Investigator"]; // maps to site_investigator & crc

      await router.push("/ecrf");
      expect(router.currentRoute.value.path).toBe("/studies");

      await router.push(
        "/ecrf?studyId=CADENCE-101&subjectId=SUBJ-101-001&siteId=SITE-101&collectionSpecId=cs.vitals&plannedActivityId=pa.screening&itemId=systolic_bp&formId=new"
      );
      expect(router.currentRoute.value.path).toBe("/studies/CADENCE-101");
      expect(router.currentRoute.value.query).toMatchObject({
        stage: "conduct",
        subject: "SUBJ-101-001",
        site: "SITE-101",
        spec: "cs.vitals",
        visit: "pa.screening",
        item: "systolic_bp",
        form: "new",
      });

      vi.stubEnv("VITE_DEMO_MODE", "true");
      await router.push("/login");
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

    it("redirects /data-lock into StudyCanvasView (stage=data&section=locks) for Data Manager and blocks unauthorized roles", async () => {
      const authStore = useAuthStore();
      const canvasStore = useStudyCanvasStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Data Manager"];
      canvasStore.studyCode = "CAD-101";

      await router.push("/data-lock");
      expect(router.currentRoute.value.name).toBe("study");
      expect(router.currentRoute.value.params.studyCode).toBe("CAD-101");
      expect(router.currentRoute.value.query).toMatchObject({
        stage: "data",
        section: "locks",
      });

      authStore.rawRoles = ["CRC"];
      await router.push("/data-lock");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });

    it("grants Auditor and TMF Auditor access on /exports and redirects into StudyCanvasView (stage=submit&section=exports)", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Auditor"]; // maps to auditor & tmf_auditor

      await router.push("/exports?studyCode=CAD-101");
      expect(router.currentRoute.value.name).toBe("study");
      expect(router.currentRoute.value.params.studyCode).toBe("CAD-101");
      expect(router.currentRoute.value.query).toMatchObject({
        stage: "submit",
        section: "exports",
      });

      authStore.rawRoles = ["TMF Auditor"];
      await router.push("/exports");
      expect(router.currentRoute.value.name).toBe("study");
      expect(router.currentRoute.value.query).toMatchObject({
        stage: "submit",
        section: "exports",
      });

      authStore.rawRoles = ["CRC"];
      await router.push("/exports");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });
  });

  describe("Dynamic Landing Route Redirections on Root (/) Navigation", () => {
    it("should redirect unauthenticated users on root navigation to /login", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = false;

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/login");
    });

    it("should redirect authenticated users to /overview hub when no explicit bypass preference exists", async () => {
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Site Investigator"];
      onboardingStore.bypassOverview = false;

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/overview");
    });

    it("should dynamically redirect CRC (Site Investigator) to /studies outside demo mode and /ecrf in demo mode when bypass preference is set", async () => {
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Site Investigator"];
      onboardingStore.setBypassOverview(true);

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/studies");

      vi.stubEnv("VITE_DEMO_MODE", "true");
      await router.push("/login");
      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ecrf");
    });

    it("should dynamically redirect CRA to /ctms when bypass preference is set", async () => {
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["CRA"];
      onboardingStore.setBypassOverview(true);

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/ctms");
    });

    it("should land Data Manager on /studies outside demo mode and /rules in demo mode when bypass preference is set while /rules has no .NET backend", async () => {
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Data Manager"];
      onboardingStore.setBypassOverview(true);

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/studies");

      vi.stubEnv("VITE_DEMO_MODE", "true");
      await router.push("/login");
      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/rules");
    });

    it("should admit Data Manager directly to /ctms and /ssu matching ROLE_SPONSOR_DM", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Data Manager"];

      await router.push("/ctms");
      expect(router.currentRoute.value.path).toBe("/ctms");

      await router.push("/ssu");
      expect(router.currentRoute.value.path).toBe("/ssu");
    });

    it("should land TMF Auditor on /studies when bypass preference is set", async () => {
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Auditor"];
      onboardingStore.setBypassOverview(true);

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/studies");
    });

    it("should land Study Designer on /studies when bypass preference is set", async () => {
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Study Designer"];
      onboardingStore.setBypassOverview(true);

      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/studies");
    });

    it("routes all clinical personas including Principal Investigator and Pharmacist from / to live authorized workspaces without hitting /unavailable or /forbidden", async () => {
      const cases = [
        { role: "Sponsor Admin", expectedBypassPath: "/studies" },
        { role: "Study Designer", expectedBypassPath: "/studies" },
        { role: "Data Manager", expectedBypassPath: "/studies" },
        { role: "CRC", expectedBypassPath: "/studies" },
        { role: "Principal Investigator", expectedBypassPath: "/studies" },
        { role: "Pharmacist", expectedBypassPath: "/studies" },
        { role: "CRA", expectedBypassPath: "/ctms" },
        { role: "Auditor", expectedBypassPath: "/studies" },
      ];

      for (const { role, expectedBypassPath } of cases) {
        const authStore = useAuthStore();
        const onboardingStore = useOnboardingStore();
        authStore.isAuthenticated = true;
        authStore.rawRoles = [role];

        onboardingStore.setBypassOverview(false);
        await router.push("/login");
        await router.push("/");
        expect(router.currentRoute.value.path, `${role} default`).toBe(
          "/overview"
        );

        onboardingStore.setBypassOverview(true);
        await router.push("/login");
        await router.push("/");
        expect(router.currentRoute.value.path, `${role} bypass`).toBe(
          expectedBypassPath
        );
      }

      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      for (const role of ["Principal Investigator", "Pharmacist"]) {
        authStore.rawRoles = [role];
        await router.push("/ctms");
        expect(router.currentRoute.value.path, `${role} /ctms`).toBe("/ctms");
        await router.push("/ssu");
        expect(router.currentRoute.value.path, `${role} /ssu`).toBe("/ssu");
      }
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
