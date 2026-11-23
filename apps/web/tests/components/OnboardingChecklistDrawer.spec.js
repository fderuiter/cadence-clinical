import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { mount, enableAutoUnmount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AppShell from "../../src/components/AppShell.vue";
import OnboardingChecklistDrawer from "../../src/components/OnboardingChecklistDrawer.vue";
import {
  useOnboardingStore,
  DEFAULT_SETUP_TASKS,
} from "../../src/stores/onboarding";
import { useAuthStore } from "../../src/stores/auth";
import {
  AVAILABLE_SCREENS,
  isScreenAvailable,
} from "../../src/router/availability";
import { hasRequiredRole } from "../../src/router";

const DummyView = { template: "<div>Dummy</div>" };

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "landing", redirect: "/overview" },
    {
      path: "/overview",
      name: "overview",
      component: DummyView,
      meta: { requiresAuth: true },
    },
    {
      path: "/forbidden",
      name: "forbidden",
      component: DummyView,
      meta: { requiresAuth: false },
    },
    {
      path: "/unavailable",
      name: "unavailable",
      component: DummyView,
      meta: { requiresAuth: false },
    },
    {
      path: "/studies/:studyCode?",
      name: "study",
      component: DummyView,
      meta: { requiresAuth: true, layout: "canvas" },
    },
    {
      path: "/ecrf",
      name: "ecrf",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: [
          "site_investigator",
          "crc",
          "data_manager",
          "sponsor_admin",
          "cra",
          "monitor",
        ],
      },
    },
    {
      path: "/ctms",
      name: "ctms",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: ["cra", "monitor", "sponsor_admin"],
      },
    },
    {
      path: "/ssu",
      name: "ssu",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: [
          "cra",
          "monitor",
          "sponsor_admin",
          "site_investigator",
          "crc",
        ],
      },
    },
    {
      path: "/mdr",
      name: "mdr",
      component: DummyView,
      meta: {
        requiresAuth: true,
        requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
      },
    },
  ],
});

router.beforeEach((to) => {
  const authStore = useAuthStore();
  const requiredRoles = to.meta?.requiresRole;
  if (
    requiredRoles?.length > 0 &&
    authStore.rawRoles?.length > 0 &&
    !hasRequiredRole(authStore.normalizedRoles, requiredRoles)
  ) {
    return { path: "/forbidden" };
  }
  if (to.name && !isScreenAvailable(String(to.name))) {
    return { name: "unavailable", query: { screen: String(to.name) } };
  }
});

describe("OnboardingChecklistDrawer.vue & AppShell Integration", () => {
  enableAutoUnmount(afterEach);
  let pinia;

  beforeEach(async () => {
    vi.restoreAllMocks();
    window.localStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
    await router.push("/overview");
    await router.isReady();
  });

  it("renders setup progress badge in AppShell header and opens drawer on click", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
    });

    const setupBadge = wrapper.find("#btn-onboarding-checklist-trigger");
    expect(setupBadge.exists()).toBe(true);
    expect(setupBadge.text()).toContain("Orientation");
    expect(setupBadge.text()).toContain("0/5");

    // Drawer should initially be closed
    expect(wrapper.find("#onboarding-checklist-drawer").exists()).toBe(false);

    // Click setup badge to open drawer
    await setupBadge.trigger("click");
    expect(wrapper.find("#onboarding-checklist-drawer").exists()).toBe(true);
    expect(wrapper.find("#checklist-drawer-title").text()).toContain(
      "Workspace Setup Checklist"
    );
  });

  it("allows interactive task completion toggling and updates progress percentage", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const onboardingStore = useOnboardingStore();
    onboardingStore.openDrawer();

    const wrapper = mount(OnboardingChecklistDrawer, {
      global: {
        plugins: [router],
      },
    });

    expect(onboardingStore.progressPercentage).toBe(0);

    // Toggle the first task
    const firstCheckbox = wrapper.find("#task-check-task_protocol");
    expect(firstCheckbox.exists()).toBe(true);

    await firstCheckbox.setValue(true);
    expect(onboardingStore.completedTasksCount).toBe(1);
    expect(onboardingStore.progressPercentage).toBe(20);

    // Toggle second task
    const secondCheckbox = wrapper.find("#task-check-task_soa");
    await secondCheckbox.setValue(true);
    expect(onboardingStore.completedTasksCount).toBe(2);
    expect(onboardingStore.progressPercentage).toBe(40);

    // Verify localStorage persistence
    const saved = JSON.parse(
      window.localStorage.getItem("onboarding_super_admin_tasks")
    );
    expect(saved.find((t) => t.id === "task_protocol").completed).toBe(true);
    expect(saved.find((t) => t.id === "task_soa").completed).toBe(true);
  });

  it("auto-completes setup tasks when matching telemetry events are logged", async () => {
    const authStore = useAuthStore();
    authStore.rawRoles = ["Sponsor Admin"];
    const onboardingStore = useOnboardingStore();
    expect(onboardingStore.completedTasksCount).toBe(0);

    // Emit telemetry event matching Schedule of Activities (SoA)
    onboardingStore.addEvent(
      "step_completed",
      "Interactive SoA & USDM Tab",
      "User configured SoA matrix"
    );

    const soaTask = onboardingStore.tasks.find((t) => t.id === "task_soa");
    expect(soaTask.completed).toBe(true);
    expect(onboardingStore.completedTasksCount).toBe(1);

    // Emit telemetry event matching eCRF visual canvas
    onboardingStore.addEvent(
      "canvas_updated",
      "eCRF Visual Canvas",
      "Added new eCRF section"
    );

    const crfTask = onboardingStore.tasks.find((t) => t.id === "task_crf");
    expect(crfTask.completed).toBe(true);
    expect(onboardingStore.completedTasksCount).toBe(2);
  });

  it("navigates to live task route when 'Go to Task' button is clicked", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const pushSpy = vi.spyOn(router, "push");
    const onboardingStore = useOnboardingStore();
    onboardingStore.openDrawer();

    const wrapper = mount(OnboardingChecklistDrawer, {
      global: {
        plugins: [router],
      },
    });

    const navBtn = wrapper.find(".btn-navigate-task");
    expect(navBtn.exists()).toBe(true);

    await navBtn.trigger("click");
    expect(pushSpy).toHaveBeenCalledWith("/studies");
    expect(onboardingStore.isDrawerOpen).toBe(false);
  });

  it("resets checklist progress when 'Reset Progress' button is clicked", async () => {
    const authStore = useAuthStore();
    authStore.rawRoles = ["Sponsor Admin"];
    const onboardingStore = useOnboardingStore();
    onboardingStore.openDrawer();
    onboardingStore.completeTask("task_protocol");
    onboardingStore.completeTask("task_rules");
    expect(onboardingStore.completedTasksCount).toBe(2);

    const wrapper = mount(OnboardingChecklistDrawer, {
      global: {
        plugins: [router],
      },
    });

    const resetBtn = wrapper.find(".btn-reset-checklist");
    expect(resetBtn.exists()).toBe(true);

    await resetBtn.trigger("click");
    expect(onboardingStore.completedTasksCount).toBe(0);
    expect(onboardingStore.progressPercentage).toBe(0);
  });

  it("isolates onboarding checklist task completion per user persona in localStorage", async () => {
    const authStore = useAuthStore();
    authStore.rawRoles = ["Sponsor Admin"];
    const onboardingStore = useOnboardingStore();

    // 1. As super_admin complete task_protocol
    authStore.currentPersona = "super_admin";
    onboardingStore.loadPersonaState("super_admin");
    onboardingStore.completeTask("task_protocol");
    expect(onboardingStore.completedTasksCount).toBe(1);

    // 2. Switch to data_manager
    authStore.currentPersona = "data_manager";
    onboardingStore.loadPersonaState("data_manager");
    expect(onboardingStore.completedTasksCount).toBe(0);

    // Complete task_crf for data_manager
    onboardingStore.completeTask("task_crf");
    expect(onboardingStore.completedTasksCount).toBe(1);

    // 3. Switch back to super_admin
    authStore.currentPersona = "super_admin";
    onboardingStore.loadPersonaState("super_admin");
    expect(onboardingStore.completedTasksCount).toBe(1);
    expect(
      onboardingStore.tasks.find((t) => t.id === "task_protocol").completed
    ).toBe(true);
    expect(
      onboardingStore.tasks.find((t) => t.id === "task_crf").completed
    ).toBe(false);
  });

  it("closes drawer when Escape key is pressed or close button clicked", async () => {
    const onboardingStore = useOnboardingStore();
    onboardingStore.openDrawer();

    const wrapper = mount(OnboardingChecklistDrawer, {
      global: {
        plugins: [router],
      },
    });

    expect(onboardingStore.isDrawerOpen).toBe(true);

    // Click close button
    const closeBtn = wrapper.find(".btn-close");
    await closeBtn.trigger("click");
    expect(onboardingStore.isDrawerOpen).toBe(false);

    // Reopen and test Escape key
    onboardingStore.openDrawer();
    await nextTick();
    expect(onboardingStore.isDrawerOpen).toBe(true);

    const keydownEvent = new KeyboardEvent("keydown", { key: "Escape" });
    window.dispatchEvent(keydownEvent);
    await nextTick();
    expect(onboardingStore.isDrawerOpen).toBe(false);
  });

  it("exposes aria-expanded/aria-controls on #btn-onboarding-checklist-trigger, blocks Ctrl+K while drawer is open, and closes <nav id='mobile-nav-menu'> on Escape restoring focus", async () => {
    await router.push("/overview");
    await router.isReady();

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const onboardingStore = useOnboardingStore();
    const wrapper = mount(AppShell, {
      attachTo: document.body,
      global: { plugins: [pinia, router] },
    });

    const checklistBtn = wrapper.find("#btn-onboarding-checklist-trigger");
    expect(checklistBtn.attributes("aria-expanded")).toBe("false");
    expect(checklistBtn.attributes("aria-controls")).toBe(
      "onboarding-checklist-drawer"
    );

    await checklistBtn.trigger("click");
    await nextTick();
    expect(checklistBtn.attributes("aria-expanded")).toBe("true");

    // Pressing Ctrl+K while OnboardingChecklistDrawer is open must NOT open #canvas-palette
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "k", ctrlKey: true, bubbles: true })
    );
    await nextTick();
    expect(document.getElementById("canvas-palette")).toBeNull();

    // Close checklist drawer with a single Escape press
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    expect(onboardingStore.isDrawerOpen).toBe(false);
    expect(checklistBtn.attributes("aria-expanded")).toBe("false");

    // Mobile menu renders as <nav id="mobile-nav-menu"> and closes on Escape restoring focus to #btn-mobile-menu-toggle
    const mobileToggle = wrapper.find("#btn-mobile-menu-toggle");
    const mobileMenu = wrapper.find("#mobile-nav-menu");
    expect(mobileMenu.element.tagName).toBe("NAV");
    expect(mobileMenu.attributes("hidden")).toBeDefined();

    mobileToggle.element.focus();
    await mobileToggle.trigger("click");
    await nextTick();
    expect(mobileMenu.attributes("hidden")).toBeUndefined();

    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    expect(mobileMenu.attributes("hidden")).toBeDefined();
    expect(document.activeElement).toBe(mobileToggle.element);
  });

  it("hides #btn-onboarding-checklist-trigger in AppShell when zero tasks are available", () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Auditor"];

    const onboardingStore = useOnboardingStore();
    onboardingStore.tasks = [
      {
        id: "task_hidden",
        title: "Hidden MDR Task",
        description: "Targets unavailable route",
        routeName: "mdr",
        route: "/mdr",
        completed: false,
      },
      {
        id: "task_forbidden",
        title: "Forbidden CTMS Task",
        description: "Targets unauthorized route",
        routeName: "ctms",
        route: "/ctms",
        completed: false,
      },
    ];

    expect(onboardingStore.totalTasksCount).toBe(0);

    const wrapper = mount(AppShell, {
      global: { plugins: [router] },
    });

    expect(wrapper.find("#btn-onboarding-checklist-trigger").exists()).toBe(
      false
    );
  });

  it("guards navigateToTask against unavailable or unauthorized routes and maps CRC site readiness to /ssu", async () => {
    await router.push("/overview");
    await flushPromises();
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["CRC"];
    authStore.currentPersona = "site_crc";

    const onboardingStore = useOnboardingStore();
    onboardingStore.loadPersonaState("site_crc");
    onboardingStore.openDrawer();

    const wrapper = mount(OnboardingChecklistDrawer, {
      global: { plugins: [router] },
    });

    const readinessTask = onboardingStore.visibleTasks.find(
      (t) => t.id === "task_rules"
    );
    expect(readinessTask?.route).toBe("/ssu");

    // Attempt to invoke navigateToTask with an unavailable task via store state injection
    const pushSpy = vi.spyOn(router, "push");
    wrapper.vm.$.setupState.navigateToTask({
      id: "bad_mdr",
      routeName: "mdr",
      route: "/mdr",
    });
    wrapper.vm.$.setupState.navigateToTask({
      id: "bad_ctms",
      routeName: "ctms",
      route: "/ctms",
    });
    expect(pushSpy).not.toHaveBeenCalled();
    expect(onboardingStore.isDrawerOpen).toBe(true);

    // Clicking live CRC site readiness task navigates to /ssu
    const readinessIndex = onboardingStore.visibleTasks.findIndex(
      (t) => t.id === "task_rules"
    );
    const navBtns = wrapper.findAll(".btn-navigate-task");
    await navBtns[readinessIndex].trigger("click");
    expect(pushSpy).toHaveBeenCalledWith("/ssu");
    pushSpy.mockRestore();
  });
});
