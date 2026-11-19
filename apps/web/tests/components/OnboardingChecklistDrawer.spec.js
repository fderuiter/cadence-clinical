import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import AppShell from "../../src/components/AppShell.vue";
import OnboardingChecklistDrawer from "../../src/components/OnboardingChecklistDrawer.vue";
import {
  useOnboardingStore,
  DEFAULT_SETUP_TASKS,
} from "../../src/stores/onboarding";
import { useAuthStore } from "../../src/stores/auth";

vi.mock("../../src/router/availability", async (importOriginal) => ({
  ...(await importOriginal()),
  isScreenAvailable: () => true,
}));

const DummyView = { template: "<div>Dummy</div>" };

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "home", component: DummyView },
    { path: "/overview", name: "overview", component: DummyView },
    { path: "/studies/:studyCode?", name: "study", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/ssu", name: "ssu", component: DummyView },
  ],
});

describe("OnboardingChecklistDrawer.vue & AppShell Integration", () => {
  beforeEach(() => {
    window.localStorage.clear();
    const pinia = createPinia();
    setActivePinia(pinia);
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
    expect(onboardingStore.isDrawerOpen).toBe(true);

    const keydownEvent = new KeyboardEvent("keydown", { key: "Escape" });
    window.dispatchEvent(keydownEvent);
    expect(onboardingStore.isDrawerOpen).toBe(false);
  });
});
