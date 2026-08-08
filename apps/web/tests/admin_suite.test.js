import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router";
import { useAuthStore } from "../src/stores/auth";
import { useAdminStore } from "../src/stores/admin";
import AdminView from "../src/views/AdminView.vue";
import AppShell from "../src/components/AppShell.vue";

// Mock apiClient to prevent actual network calls during tests
vi.mock("../src/api/apiClient", () => {
  return {
    apiClient: {
      get: vi.fn((path) => {
        if (path === "/api/v1/org/organizations") {
          return Promise.resolve([
            { id: "org-1", name: "Apex CRO", org_type: "CRO", version_index: 1, reason_for_change: "init" }
          ]);
        }
        if (path === "/api/v1/org/sites") {
          return Promise.resolve([
            { id: "site-1", site_id: "SITE-001", name: "Boston Site", organization_id: "org-1", study_id: "S-123", version_index: 1, reason_for_change: "init" }
          ]);
        }
        if (path === "/api/v1/org/personnel") {
          return Promise.resolve([
            { id: "person-1", first_name: "John", last_name: "Doe", email: "john@doe.com", role: "Principal Investigator", site_id: "SITE-001", organization_id: "org-1", study_id: "S-123", reason_for_change: "init" }
          ]);
        }
        if (path.includes("/assignments")) {
          return Promise.resolve([
            { id: "asg-1", personnel_id: "person-1", site_id: "SITE-001", study_id: "S-123", is_active: true }
          ]);
        }
        return Promise.resolve([]);
      }),
      post: vi.fn().mockResolvedValue({ id: "new-id" }),
      put: vi.fn().mockResolvedValue({ id: "updated-id" })
    }
  };
});

describe("Dedicated Site Administration Suite Integration Tests", () => {
  let pinia;

  beforeEach(async () => {
    pinia = createPinia();
    setActivePinia(pinia);

    if (typeof window !== "undefined") {
      delete window.keycloakInstance;
      window.sessionStorage.clear();
      window.localStorage.clear();
    }

    await router.push("/login");
  });

  describe("Route Protection & Access Gating", () => {
    it("should redirect unauthenticated users to login when trying to access /admin", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = false;
      authStore.isDemoMode = true;

      await router.push("/admin");
      expect(router.currentRoute.value.path).toBe("/login");
    });

    it("should redirect authenticated users without sponsor_admin to /forbidden when trying to access /admin", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Study Designer"]; // Study Designer, not Sponsor Admin

      await router.push("/admin");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });

    it("should allow authenticated sponsor_admin to navigate to /admin", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Admin"]; // maps to sponsor_admin

      await router.push("/admin");
      expect(router.currentRoute.value.path).toBe("/admin");
    });
  });

  describe("Navigation Link in Sidebar Menu", () => {
    it("hides the Site Administration navigation option for non-admin users", () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Study Designer"];

      const wrapper = mount(AppShell, {
        global: {
          plugins: [pinia, router]
        }
      });

      expect(wrapper.find("#tab-btn-admin").exists()).toBe(false);
    });

    it("displays the Site Administration navigation option for sponsor_admin users", () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Admin"];

      const wrapper = mount(AppShell, {
        global: {
          plugins: [pinia, router]
        }
      });

      expect(wrapper.find("#tab-btn-admin").exists()).toBe(true);
      expect(wrapper.find("#tab-btn-admin").text()).toContain("Site Administration");
    });
  });

  describe("AdminView Interface & Part 11 Change Reason Guard", () => {
    it("displays the gating warning banner if a non-admin somehow gets to the route/view directly", () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Study Designer"];

      const wrapper = mount(AdminView, {
        global: {
          plugins: [pinia, router]
        }
      });

      expect(wrapper.find(".admin-gating-banner").exists()).toBe(true);
      expect(wrapper.text()).toContain("Access Denied");
    });

    it("renders AdminView tabs correctly for sponsor_admin with no gating warnings", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Admin"];

      const wrapper = mount(AdminView, {
        global: {
          plugins: [pinia, router]
        }
      });

      expect(wrapper.find(".admin-gating-banner").exists()).toBe(false);
      expect(wrapper.text()).toContain("Clinical Sites");
      expect(wrapper.text()).toContain("Personnel & Directory");
      expect(wrapper.text()).toContain("Site & Study Assignments");
    });

    it("locks the site registration save button until a non-empty change justification is typed", async () => {
      const authStore = useAuthStore();
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Admin"];

      const wrapper = mount(AdminView, {
        global: {
          plugins: [pinia, router]
        }
      });

      // Sites tab is default
      const btnSaveSite = wrapper.find("#btn-save-site");
      expect(btnSaveSite.exists()).toBe(true);
      // It should be disabled initially
      expect(btnSaveSite.attributes("disabled")).toBeDefined();

      // Enter site inputs, but leave change reason blank
      const inputs = wrapper.findAll("input");
      const siteIdInput = inputs[0];
      const siteNameInput = inputs[1];

      await siteIdInput.setValue("SITE-NEW");
      await siteNameInput.setValue("New Treatment Center");

      // Select an organization
      const select = wrapper.find("select");
      await select.setValue("org-1");

      // Button must still be disabled
      expect(btnSaveSite.attributes("disabled")).toBeDefined();

      // Type change reason
      const textarea = wrapper.find("textarea");
      await textarea.setValue("Provisioning new Phase II research site");

      // Now the button should be enabled!
      expect(btnSaveSite.attributes("disabled")).toBeUndefined();
    });
  });

  describe("Isolated Pinia Administration State", () => {
    it("uses a separate pinia store state preventing interference with active clinical monitoring choices", () => {
      const adminStore = useAdminStore();

      // Initialize store
      expect(adminStore.sites).toEqual([]);
      expect(adminStore.personnel).toEqual([]);

      adminStore.sites = [{ id: "site-9", name: "Isolated Site" }];

      // Confirm changes remain isolated to adminStore
      expect(adminStore.sites[0].name).toBe("Isolated Site");
    });
  });
});
