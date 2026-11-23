import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defineComponent, h, nextTick } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { PERSONA_CATALOG, PersonaSwitcher, getPersonaCatalogEntry } from "ui";
import AppShell from "../src/components/AppShell.vue";
import OnboardingChecklistDrawer from "../src/components/OnboardingChecklistDrawer.vue";
import OnboardingTour from "../src/components/OnboardingTour.vue";
import OverviewView from "../src/views/OverviewView.vue";
import EcrfLiveWorkspace from "../src/views/EcrfLiveWorkspace.vue";
import EcrfDemoWorkspace from "../src/views/demo/EcrfDemoWorkspace.vue";
import { i18n } from "../src/i18n";
import { router, landingRouteFor } from "../src/router";
import { ecrfFormsApi, type EcrfForm } from "../src/api/ecrfFormsApi";
import { executionApi } from "../src/api/executionApi";
import { kernelApi } from "../src/api/kernelApi";
import {
  useEcrfForm,
  ECRF_FORM_DRAFT_NAMESPACE,
} from "../src/composables/useEcrfForm";
import {
  PERSONA_PRESETS,
  resolveEffectivePersonaRoles,
  useAuthStore,
} from "../src/stores/auth";
import {
  useOnboardingStore,
  getOnboardingStorageKey,
  ROLE_STEP_SEQUENCES,
} from "../src/stores/onboarding";
import { scopedKey, storageScopeOf } from "../src/utils/userScopedStorage";
import { signInAs } from "./helpers/signIn.js";

function mockLoadedForm(): EcrfForm {
  return {
    form_id: "form-demo-101",
    enrollment_id: "enr-101",
    study_id: "CADENCE-101",
    subject_id: "SUBJ-101",
    site_id: "SITE-101",
    planned_activity_id: "VISIT-1",
    collection_spec_id: "VS",
    collection_title: "Vital Signs",
    protocol_version_id: "pv-1",
    status: "IN_PROGRESS",
    version: 1,
    locks: [],
    assertions: [],
    signatures: [],
    items: [
      {
        item_id: "VS_SYSBP",
        label: "Systolic Blood Pressure",
        value_type: "quantity",
        required: true,
        order: 1,
        unit: "mmHg",
        allowed_units: ["mmHg"],
        codelist: null,
        revision: 1,
        locked: false,
        value: { type: "quantity", value: 120, unit: "mmHg" },
      },
    ],
  } as unknown as EcrfForm;
}

describe("Unified Persona Catalog & Scoped Onboarding Acceptance Suite", () => {
  const originalInnerWidth = window.innerWidth;
  const originalInnerHeight = window.innerHeight;
  let pinia!: ReturnType<typeof createPinia>;

  beforeEach(async () => {
    vi.restoreAllMocks();
    vi.useRealTimers();
    vi.unstubAllEnvs();
    window.localStorage.clear();
    window.sessionStorage.clear();
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 1280,
    });
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: 800,
    });
    pinia = createPinia();
    setActivePinia(pinia);
    await router.push("/login");
    await router.isReady();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllEnvs();
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: originalInnerWidth,
    });
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: originalInnerHeight,
    });
  });

  it("AC1: displays dedicated titles, descriptions, checklist items, and tour sequences for site_investigator and safety_officer across /overview, PersonaSwitcher, and AppShell", async () => {
    expect(PERSONA_PRESETS).toBe(PERSONA_CATALOG);
    expect(Object.keys(ROLE_STEP_SEQUENCES)).toEqual(
      PERSONA_CATALOG.map((entry) => entry.key)
    );

    const authStore = signInAs(
      useAuthStore(),
      ["Principal Investigator", "Safety Reviewer"],
      { username: "dr_patel", tenantId: "tenant-1" }
    );
    const onboardingStore = useOnboardingStore();
    await router.push("/overview");

    const shell = mount(AppShell, {
      global: { plugins: [pinia, router, i18n] },
      slots: { default: () => h(OverviewView) },
    });
    const switcher = mount(PersonaSwitcher, {
      props: { modelValue: authStore.currentPersona },
    });
    await switcher.find(".persona-toggle-btn").trigger("click");

    // 1. Verify site_investigator across shell, PersonaSwitcher, OverviewView, checklist, and tour
    const personaSelect = shell.find("#persona-select");
    expect(personaSelect.exists()).toBe(true);
    await personaSelect.setValue("site_investigator");
    await switcher.setProps({ modelValue: authStore.currentPersona });
    await nextTick();

    const piCatalog = getPersonaCatalogEntry("site_investigator")!;
    expect(authStore.currentPersona).toBe("site_investigator");
    expect(switcher.find(".persona-label").text()).toBe(
      "Principal Investigator"
    );
    expect(switcher.text()).toContain(piCatalog.title);
    expect(switcher.text()).toContain(piCatalog.description);
    expect(shell.text()).toContain(piCatalog.title);
    expect(shell.text()).toContain(piCatalog.description);
    expect(onboardingStore.visibleTasks.map((t) => t.id)).toEqual(
      piCatalog.tasks.map((t) => t.id)
    );
    expect(onboardingStore.personaSteps[0].title).toBe(
      "Welcome to Investigator Oversight"
    );

    // 2. Verify safety_officer across shell, PersonaSwitcher, OverviewView, checklist, and tour
    await personaSelect.setValue("safety_officer");
    await switcher.setProps({ modelValue: authStore.currentPersona });
    await nextTick();

    const safetyCatalog = getPersonaCatalogEntry("safety_officer")!;
    expect(authStore.currentPersona).toBe("safety_officer");
    expect(switcher.find(".persona-label").text()).toBe(
      "Pharmacovigilance & Safety"
    );
    expect(switcher.text()).toContain(safetyCatalog.title);
    expect(switcher.text()).toContain(safetyCatalog.description);
    expect(shell.text()).toContain(safetyCatalog.title);
    expect(shell.text()).toContain(safetyCatalog.description);
    expect(onboardingStore.visibleTasks.map((t) => t.id)).toEqual(
      safetyCatalog.tasks.map((t) => t.id)
    );
    expect(onboardingStore.personaSteps[0].title).toBe(
      "Welcome to Pharmacovigilance & Safety"
    );

    switcher.unmount();
    shell.unmount();
  });

  it("AC2: routes principal_investigator and safety_reviewer from / when bypassOverview is enabled without hitting /forbidden in both live and demo modes", async () => {
    // Live mode: principal_investigator -> /studies, safety_reviewer -> /studies
    const authStore = signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi_live",
      tenantId: "tenant-1",
    });
    const onboardingStore = useOnboardingStore();
    onboardingStore.loadPersonaState("site_investigator");
    onboardingStore.setBypassOverview(true);

    expect(
      landingRouteFor(["principal_investigator"], "site_investigator")
    ).toBe("study");
    await router.push("/");
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.path).not.toBe("/forbidden");

    signInAs(authStore, ["Safety Reviewer"], {
      username: "safety_live",
      tenantId: "tenant-1",
    });
    onboardingStore.loadPersonaState("safety_officer");
    onboardingStore.setBypassOverview(true);

    expect(landingRouteFor(["safety_reviewer"], "safety_officer")).toBe(
      "safety"
    );
    await router.push("/login");
    await router.push("/");
    await flushPromises();
    expect(router.currentRoute.value.name).toBe("study");
    expect(router.currentRoute.value.path).not.toBe("/forbidden");

    // Demo mode: principal_investigator -> /ecrf, safety_reviewer -> /safety
    vi.stubEnv("VITE_DEMO_MODE", "true");
    signInAs(authStore, ["Principal Investigator"], {
      username: "pi_demo",
      tenantId: "tenant-1",
    });
    onboardingStore.loadPersonaState("site_investigator");
    onboardingStore.setBypassOverview(true);

    expect(
      landingRouteFor(["principal_investigator"], "site_investigator")
    ).toBe("ecrf");
    await router.push("/login");
    await router.push("/");
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/ecrf");

    signInAs(authStore, ["Safety Reviewer"], {
      username: "safety_demo",
      tenantId: "tenant-1",
    });
    onboardingStore.loadPersonaState("safety_officer");
    onboardingStore.setBypassOverview(true);

    expect(landingRouteFor(["safety_reviewer"], "safety_officer")).toBe(
      "safety"
    );
    await router.push("/login");
    await router.push("/");
    await flushPromises();
    expect(router.currentRoute.value.path).toBe("/safety");
  });

  it("AC3: narrows active persona roles on #persona-select switch to auditor (hiding /ecrf and /ctms launchers and tasks) while preserving full roles on super_admin", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const authStore = signInAs(
      useAuthStore(),
      ["Sponsor Admin", "CRC", "CRA", "Auditor"],
      { username: "multi_role", tenantId: "tenant-1" }
    );
    const onboardingStore = useOnboardingStore();
    await router.push("/overview");

    const shell = mount(AppShell, {
      global: { plugins: [pinia, router, i18n] },
      slots: { default: () => h(OverviewView) },
    });

    // Under super_admin, /ecrf and /ctms are authorized and visible
    expect(authStore.currentPersona).toBe("super_admin");
    expect(authStore.normalizedRoles).toContain("sponsor_admin");
    expect(authStore.normalizedRoles).toContain("crc");
    expect(authStore.normalizedRoles).toContain("cra");
    expect(shell.find("#nav-data-collection").exists()).toBe(true);
    expect(shell.find("#nav-site-readiness").exists()).toBe(true);

    // Switch multi-role user to auditor via #persona-select
    await shell.find("#persona-select").setValue("auditor");
    onboardingStore.openDrawer();
    await nextTick();

    expect(authStore.currentPersona).toBe("auditor");
    expect(
      resolveEffectivePersonaRoles(
        authStore.rawRoles,
        authStore.currentPersona,
        {
          isAuthenticated: true,
        }
      )
    ).toEqual(["auditor", "tmf_auditor"]);
    expect(shell.find("#nav-data-collection").exists()).toBe(false);
    expect(shell.find("#nav-site-readiness").exists()).toBe(false);

    const overviewTitles = shell
      .findAll(".launcher-title")
      .map((el) => el.text());
    expect(overviewTitles).toEqual(["Studies"]);

    const visibleTaskRoutes = onboardingStore.visibleTasks.map((t) => t.route);
    expect(visibleTaskRoutes).not.toContain("/ecrf");
    expect(visibleTaskRoutes).not.toContain("/ctms");

    const drawerText = shell.find("#onboarding-checklist-drawer").text();
    expect(drawerText).not.toContain("Design Casebook Forms");
    expect(drawerText).not.toContain("Verify Site Readiness & Monitoring");

    shell.unmount();
  });

  it("AC4: flushes pending useEcrfForm draft synchronously within <500ms before demo switchPersona changes account scope", async () => {
    vi.useFakeTimers();
    vi.stubEnv("VITE_DEMO_MODE", "true");

    const loaded = mockLoadedForm();
    vi.spyOn(ecrfFormsApi, "getForm").mockResolvedValue(loaded);
    vi.spyOn(ecrfFormsApi, "previewValues").mockResolvedValue({
      form_id: loaded.form_id,
      warnings: [],
    });

    const authStore = useAuthStore();
    authStore.startDemoSession();
    authStore.switchPersona("site_crc");
    const crcScope = storageScopeOf(authStore)!;
    expect(crcScope).toBeTruthy();

    let formApi!: ReturnType<typeof useEcrfForm>;
    const Harness = defineComponent({
      setup() {
        formApi = useEcrfForm();
        return () => h("div");
      },
    });

    const wrapper = mount(Harness, {
      global: { plugins: [pinia] },
    });
    await formApi.load(loaded.form_id);

    // Edit an eCRF field and advance only 150ms (< 500ms debounce window)
    formApi.entries.VS_SYSBP.text = "138";
    await nextTick();
    vi.advanceTimersByTime(150);

    const crcDraftKey = scopedKey(
      crcScope,
      ECRF_FORM_DRAFT_NAMESPACE,
      loaded.form_id
    )!;
    expect(window.localStorage.getItem(crcDraftKey)).toBeNull();

    // Switch demo persona within 500ms -> synchronous flush runs BEFORE identity swaps
    authStore.switchPersona("auditor");
    const auditorScope = storageScopeOf(authStore)!;
    expect(auditorScope?.userId).not.toBe(crcScope.userId);

    const flushedRaw = window.localStorage.getItem(crcDraftKey);
    expect(flushedRaw).toBeTruthy();
    expect(JSON.parse(flushedRaw!).items.VS_SYSBP.text).toBe("138");

    // Advance past 500ms and verify auditor scope receives zero leaked drafts
    vi.advanceTimersByTime(600);
    await nextTick();
    const auditorDraftKey = scopedKey(
      auditorScope,
      ECRF_FORM_DRAFT_NAMESPACE,
      loaded.form_id
    )!;
    expect(window.localStorage.getItem(auditorDraftKey)).toBeNull();
    expect(formApi.entries.VS_SYSBP.text).toBe("120");

    // Switching back to site_crc restores the flushed 138 draft
    authStore.switchPersona("site_crc");
    await nextTick();
    expect(formApi.entries.VS_SYSBP.text).toBe("138");

    wrapper.unmount();
  });

  it("AC5: keeps /overview, AppShell header badge, and OnboardingChecklistDrawer synchronized on task toggle and reset", async () => {
    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi_sync",
      tenantId: "tenant-1",
    });
    const onboardingStore = useOnboardingStore();
    onboardingStore.loadPersonaState("site_investigator");
    onboardingStore.openDrawer();
    await router.push("/overview");

    const shell = mount(AppShell, {
      global: { plugins: [pinia, router, i18n] },
      slots: { default: () => h(OverviewView) },
    });

    const total = onboardingStore.totalTasksCount;
    expect(total).toBe(4);
    expect(shell.find("#btn-onboarding-checklist-trigger").text()).toContain(
      `0/${total}`
    );
    expect(shell.find(".progress-badge").text()).toContain(`0 of ${total}`);
    expect(
      shell.find("#onboarding-checklist-drawer .progress-counts").text()
    ).toContain(`0 of ${total} Tasks Completed`);

    // Toggle task 1 in OverviewView -> updates Overview, AppShell badge, and Drawer
    await shell.findAll(".overview-section .task-checkbox")[0].setValue(true);
    await nextTick();

    expect(shell.find("#btn-onboarding-checklist-trigger").text()).toContain(
      `1/${total}`
    );
    expect(shell.find(".progress-badge").text()).toContain(`1 of ${total}`);
    expect(
      shell.find("#onboarding-checklist-drawer .progress-counts").text()
    ).toContain(`1 of ${total} Tasks Completed`);

    // Toggle task 2 in OnboardingChecklistDrawer -> updates all three
    await shell
      .find("#onboarding-checklist-drawer #task-check-pi_oversight")
      .setValue(true);
    await nextTick();

    expect(shell.find("#btn-onboarding-checklist-trigger").text()).toContain(
      `2/${total}`
    );
    expect(shell.find(".progress-badge").text()).toContain(`2 of ${total}`);
    expect(
      shell.find("#onboarding-checklist-drawer .progress-counts").text()
    ).toContain(`2 of ${total} Tasks Completed`);

    // Click Reset Progress in OverviewView -> resets all three to 0
    await shell.find("#btn-overview-reset-checklist").trigger("click");
    await nextTick();

    expect(onboardingStore.completedTasksCount).toBe(0);
    expect(shell.find("#btn-onboarding-checklist-trigger").text()).toContain(
      `0/${total}`
    );
    expect(shell.find(".progress-badge").text()).toContain(`0 of ${total}`);
    expect(
      shell.find("#onboarding-checklist-drawer .progress-counts").text()
    ).toContain(`0 of ${total} Tasks Completed`);

    shell.unmount();
  });

  it("AC6: stores onboarding keys under cadence_user:<tenant>:<user>:onboarding:<persona>:<field>, writes nothing without claims, and purges all scoped + legacy onboarding_* keys + memory on logout", async () => {
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();

    // Missing tenantId / userId claims -> writes nothing to localStorage
    authStore.isAuthenticated = true;
    authStore.user = { id: "user-no-tenant", tenantId: "" } as never;
    authStore.rawRoles = ["CRC"];
    onboardingStore.loadPersonaState("site_crc");
    onboardingStore.startTour();
    onboardingStore.completeTask("crc_enroll");
    onboardingStore.setBypassOverview(true);
    expect(window.localStorage.length).toBe(0);

    // Full claims -> writes under cadence_user:<tenant>:<user>:onboarding:<persona>:<field>
    signInAs(authStore, ["CRC"], {
      username: "crc_jane",
      tenantId: "tenant-acme",
    });
    onboardingStore.loadPersonaState("site_crc");
    onboardingStore.startTour();
    onboardingStore.completeTask("crc_enroll");
    onboardingStore.setBypassOverview(true);

    // Seed legacy onboarding_* key and unrelated non-user preference
    window.localStorage.setItem("onboarding_site_crc_tasks", "[]");
    window.localStorage.setItem("onboarding_global_disabled", "true");
    window.localStorage.setItem("cadence_theme", "light");

    const expectedTaskKey = getOnboardingStorageKey("tasks", "site_crc")!;
    expect(expectedTaskKey).toBe(
      "cadence_user:tenant-acme:crc_jane-id:onboarding:site_crc:tasks"
    );
    expect(window.localStorage.getItem(expectedTaskKey)).toBeTruthy();

    await authStore.logout();

    const remainingKeys: string[] = [];
    for (let i = 0; i < window.localStorage.length; i++) {
      const k = window.localStorage.key(i);
      if (k) remainingKeys.push(k);
    }
    expect(remainingKeys).toEqual(["cadence_theme"]);
    expect(onboardingStore.isActive).toBe(false);
    expect(onboardingStore.bypassOverview).toBe(false);
    expect(onboardingStore.completedTasksCount).toBe(0);
    expect(onboardingStore.events).toEqual([]);
  });

  it("AC7: anchors site_crc tour Steps 2 and 3 on /ecrf to #ecrf-participant-bar and #ecrf-form-instance across live and demo workspaces", async () => {
    // 1. Live workspace DOM check
    vi.spyOn(kernelApi, "listStudies").mockResolvedValue([]);
    vi.spyOn(executionApi, "listSubjects").mockResolvedValue({
      items: [],
      total: 0,
      limit: 50,
      offset: 0,
    });
    const liveWrapper = mount(EcrfLiveWorkspace, {
      attachTo: document.body,
      global: { plugins: [pinia, router, i18n] },
    });
    await flushPromises();
    const liveBar = document.querySelector(
      "#ecrf-participant-bar"
    ) as HTMLElement | null;
    const liveInstance = document.querySelector(
      "#ecrf-form-instance"
    ) as HTMLElement | null;
    expect(liveBar).not.toBeNull();
    expect(liveInstance).not.toBeNull();
    liveWrapper.unmount();

    // 2. Demo workspace + OnboardingTour anchoring check
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const demoPinia = createPinia();
    setActivePinia(demoPinia);
    const authStore = useAuthStore(demoPinia);
    authStore.startDemoSession("site_crc");
    await router.push("/ecrf");
    await flushPromises();

    const demoWrapper = mount(EcrfDemoWorkspace, {
      attachTo: document.body,
      global: { plugins: [demoPinia, router, i18n] },
    });
    await flushPromises();

    const barEl = document.querySelector(
      "#ecrf-participant-bar"
    ) as HTMLElement;
    const formEl = document.querySelector("#ecrf-form-instance") as HTMLElement;
    expect(barEl).not.toBeNull();
    expect(formEl).not.toBeNull();

    vi.spyOn(barEl, "getBoundingClientRect").mockReturnValue({
      left: 80,
      top: 90,
      width: 600,
      height: 56,
      right: 680,
      bottom: 146,
      x: 80,
      y: 90,
      toJSON: () => ({}),
    });
    vi.spyOn(formEl, "getBoundingClientRect").mockReturnValue({
      left: 120,
      top: 180,
      width: 720,
      height: 360,
      right: 840,
      bottom: 540,
      x: 120,
      y: 180,
      toJSON: () => ({}),
    });

    const onboardingStore = useOnboardingStore(demoPinia);
    onboardingStore.loadPersonaState("site_crc");
    onboardingStore.startTour();

    const tour = mount(OnboardingTour, {
      attachTo: document.body,
      global: { plugins: [demoPinia, router, i18n] },
    });
    await flushPromises();

    // Advance to Step 2 -> #ecrf-participant-bar (bottom 146 + 12 = 158px, center 80 + 300 = 380px)
    await tour.find(".btn-tour-next").trigger("click");
    await flushPromises();
    expect(onboardingStore.currentStep).toBe(2);
    expect(onboardingStore.currentStepConfig?.targetSelector).toContain(
      "#ecrf-participant-bar"
    );
    const cardStep2 = tour.find(".onboarding-popover-card")
      .element as HTMLElement;
    expect(cardStep2.style.top).toBe("158px");
    expect(cardStep2.style.left).toBe("380px");

    // Advance to Step 3 -> #ecrf-form-instance (bottom 540 + 12 = 552px, center 120 + 360 = 480px)
    await tour.find(".btn-tour-next").trigger("click");
    await flushPromises();
    expect(onboardingStore.currentStep).toBe(3);
    expect(onboardingStore.currentStepConfig?.targetSelector).toContain(
      "#ecrf-form-instance"
    );
    const cardStep3 = tour.find(".onboarding-popover-card")
      .element as HTMLElement;
    expect(cardStep3.style.top).toBe("552px");
    expect(cardStep3.style.left).toBe("480px");

    tour.unmount();
    demoWrapper.unmount();
  });

  it("AC8: clamps tour popover inside horizontal margins across 375px–1440px and remaps <860px #nav-* steps to #btn-mobile-menu-toggle", async () => {
    signInAs(useAuthStore(), ["Sponsor Admin"], {
      username: "admin_resp",
      tenantId: "tenant-1",
    });
    await router.push("/studies");
    await flushPromises();

    const shell = mount(AppShell, {
      attachTo: document.body,
      global: { plugins: [pinia, router, i18n] },
      slots: { default: () => h("div", "Studies Canvas") },
    });

    const navStudiesEl = document.querySelector("#nav-studies") as HTMLElement;
    const mobileToggleEl = document.querySelector(
      "#btn-mobile-menu-toggle"
    ) as HTMLElement;
    const helpBtnEl = document.querySelector(
      "#btn-contextual-help-trigger"
    ) as HTMLElement;

    expect(navStudiesEl).not.toBeNull();
    expect(mobileToggleEl).not.toBeNull();
    expect(helpBtnEl).not.toBeNull();

    // Desktop 1440px: right-edge #btn-contextual-help-trigger clamps inside 1440 - 16 = 1424px
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 1440,
    });
    vi.spyOn(navStudiesEl, "getBoundingClientRect").mockReturnValue({
      left: 220,
      top: 12,
      width: 80,
      height: 36,
      right: 300,
      bottom: 48,
      x: 220,
      y: 12,
      toJSON: () => ({}),
    });
    vi.spyOn(helpBtnEl, "getBoundingClientRect").mockReturnValue({
      left: 1350,
      top: 12,
      width: 80,
      height: 36,
      right: 1430,
      bottom: 48,
      x: 1350,
      y: 12,
      toJSON: () => ({}),
    });

    const onboardingStore = useOnboardingStore();
    onboardingStore.startTour();
    // Step 2 targets #nav-studies
    onboardingStore.currentStep = 2;
    await nextTick();
    window.dispatchEvent(new Event("resize"));
    await nextTick();

    const getPopoverBounds = (viewportWidth: number) => {
      const card = shell.find(".onboarding-popover-card")
        .element as HTMLElement;
      const cardWidth = Math.min(320, viewportWidth - 24);
      const center = parseFloat(card.style.left);
      return {
        leftEdge: center - cardWidth / 2,
        rightEdge: center + cardWidth / 2,
        center,
        top: parseFloat(card.style.top),
      };
    };

    const bounds1440Nav = getPopoverBounds(1440);
    expect(bounds1440Nav.center).toBe(260);
    expect(bounds1440Nav.leftEdge).toBeGreaterThanOrEqual(12);
    expect(bounds1440Nav.rightEdge).toBeLessThanOrEqual(1440 - 12);

    // Resize to 375px (<860px): #nav-studies remaps to #btn-mobile-menu-toggle and stays within [12, 363]
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 375,
    });
    vi.spyOn(navStudiesEl, "getBoundingClientRect").mockReturnValue({
      left: 0,
      top: 0,
      width: 0,
      height: 0,
      right: 0,
      bottom: 0,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    });
    vi.spyOn(mobileToggleEl, "getBoundingClientRect").mockReturnValue({
      left: 325,
      top: 10,
      width: 38,
      height: 38,
      right: 363,
      bottom: 48,
      x: 325,
      y: 10,
      toJSON: () => ({}),
    });

    window.dispatchEvent(new Event("resize"));
    await nextTick();

    const bounds375 = getPopoverBounds(375);
    expect(bounds375.top).toBe(60); // 48 + 12 -> anchored to #btn-mobile-menu-toggle, NOT centered 50% fallback!
    expect(bounds375.leftEdge).toBeGreaterThanOrEqual(12);
    expect(bounds375.rightEdge).toBeLessThanOrEqual(375 - 12);

    shell.unmount();
  });
});
