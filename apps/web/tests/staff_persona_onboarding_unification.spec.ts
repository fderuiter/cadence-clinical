import fs from "node:fs";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defineComponent, h, nextTick, ref } from "vue";
import { flushPromises, mount } from "@vue/test-utils";
import { setupServer } from "msw/node";
import { createPinia, setActivePinia } from "pinia";
import { DEMO_PARTICIPANTS, DEMO_PERSONAS } from "demo-fixtures";
import { DEFAULT_STAFF_PERSONAS } from "ui";
import { installAuthorizedFetch } from "../src/api/authorization.js";
import {
  STAFF_PERSONA_CATALOG,
  getPersonaCatalogEntry,
  resolveLensEffectiveRoles,
} from "../src/catalog/staffPersonas.js";
import AppShell from "../src/components/AppShell.vue";
import OnboardingChecklistDrawer from "../src/components/OnboardingChecklistDrawer.vue";
import OnboardingTour from "../src/components/OnboardingTour.vue";
import { useEcrfDrafts } from "../src/composables/useEcrfDrafts.js";
import {
  ECRF_FORM_DRAFT_NAMESPACE,
  useEcrfForm,
} from "../src/composables/useEcrfForm";
import { createWebDemoStore } from "../src/mocks/demoStore.js";
import { buildWebGatewayHandlers } from "../src/mocks/handlers.js";
import { isRouteAuthorizedForRoles, router } from "../src/router";
import { AVAILABLE_SCREENS } from "../src/router/availability.js";
import { PERSONA_PRESETS, useAuthStore } from "../src/stores/auth.js";
import {
  isTourRouteAuthorized,
  resolveAuthorizedTaskRoute,
  useOnboardingStore,
} from "../src/stores/onboarding.js";
import { scopedKey, storageScopeOf } from "../src/utils/userScopedStorage.js";
import OverviewView from "../src/views/OverviewView.vue";

const FORBIDDEN_UX1_ACRONYMS = ["EDC", "CTMS", "eTMF", "RTSM", "Sandbox"];
const EMOJI_REGEX = /\p{Extended_Pictographic}/u;

interface RealmUserRecord {
  id: string;
  username: string;
  email?: string;
  firstName?: string;
  lastName?: string;
  realmRoles?: string[];
  attributes?: Record<string, string[]>;
}

function loadSeededRealmUsers(): RealmUserRecord[] {
  const realmPath = path.resolve(
    process.cwd(),
    "../../docker/cadence-realm.json"
  );
  const parsed = JSON.parse(fs.readFileSync(realmPath, "utf8"));
  return parsed.users || [];
}

function buildRealmKeycloakSession(user: RealmUserRecord) {
  const tenantId =
    user.attributes?.tenant_id?.[0] || "00000000-0000-0000-0000-000000000001";
  const studyIds = user.attributes?.study_ids || [];
  const siteId = user.attributes?.site_id || [];
  return {
    authenticated: true,
    token: `token-${user.username}`,
    idToken: `id-token-${user.username}`,
    tokenParsed: {
      sub: user.id,
      preferred_username: user.username,
      email: user.email || `${user.username}@cadence.clinical`,
      given_name: user.firstName || "Clinical",
      family_name: user.lastName || "User",
      tenant_id: tenantId,
      study_ids: studyIds,
      site_id: siteId,
      exp: Math.floor(Date.now() / 1000) + 3600,
      realm_access: {
        roles: [...(user.realmRoles || [])],
      },
    },
    logout: vi.fn(async () => {}),
    login: vi.fn(async () => {}),
    updateToken: vi.fn(async () => false),
  };
}

function listLocalStorageKeys(): string[] {
  const keys: string[] = [];
  for (let i = 0; i < window.localStorage.length; i++) {
    const key = window.localStorage.key(i);
    if (key !== null) {
      keys.push(key);
    }
  }
  return keys;
}

describe("Staff Persona Catalog & Account-Scoped Onboarding Unification (ADR-2658)", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(async () => {
    vi.restoreAllMocks();
    window.localStorage.clear();
    window.sessionStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
    await router.push("/login");
    await router.isReady();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("AC-1: resolves every seeded account in cadence-realm.json and demo-fixtures manifest to its matching persona preset, authorized landing route, and /safety access", async () => {
    expect(DEFAULT_STAFF_PERSONAS.map((p) => p.id)).toEqual(
      PERSONA_PRESETS.map((p) => p.key)
    );
    expect(PERSONA_PRESETS).toHaveLength(10);

    // Zero emojis and zero UX-1 forbidden acronyms across catalog and shared PersonaSwitcher options
    for (const option of DEFAULT_STAFF_PERSONAS) {
      expect(EMOJI_REGEX.test(option.label)).toBe(false);
      expect(EMOJI_REGEX.test(option.badge || "")).toBe(false);
    }
    for (const entry of Object.values(STAFF_PERSONA_CATALOG)) {
      const serialized = JSON.stringify({
        label: entry.label,
        badge: entry.badge,
        orientation: entry.orientation,
        checklist: entry.checklist.map(
          (c: { title: string; description: string }) => ({
            title: c.title,
            description: c.description,
          })
        ),
        launchers: entry.launchers.map(
          (l: { title: string; description: string; badgeText?: string }) => ({
            title: l.title,
            description: l.description,
            badgeText: l.badgeText,
          })
        ),
        tourSequence: entry.tourSequence.map(
          (s: { title: string; description: string; prompt?: string }) => ({
            title: s.title,
            description: s.description,
            prompt: s.prompt,
          })
        ),
      });
      expect(EMOJI_REGEX.test(serialized)).toBe(false);
      for (const acronym of FORBIDDEN_UX1_ACRONYMS) {
        expect(serialized).not.toContain(acronym);
      }
    }

    const realmUsers = loadSeededRealmUsers();
    const safetyRoute = router.getRoutes().find((r) => r.name === "safety");
    expect(safetyRoute).toBeDefined();

    const expectedStaffAccounts: Array<{
      username: string;
      expectedPersona: string;
      expectedBypassLandingNonDemo: string;
      expectedBypassLandingDemo: string;
      canAccessSafety: boolean;
    }> = [
      {
        username: "designer_demo",
        expectedPersona: "sponsor_designer",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/mdr",
        canAccessSafety: false,
      },
      {
        username: "designer.lead",
        expectedPersona: "super_admin",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/mdr",
        canAccessSafety: true,
      },
      {
        username: "crc.site101",
        expectedPersona: "site_crc",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/ecrf",
        canAccessSafety: false,
      },
      {
        username: "pi.site101",
        expectedPersona: "site_investigator",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/ecrf",
        canAccessSafety: false,
      },
      {
        username: "cra.monitor",
        expectedPersona: "cra_monitor",
        expectedBypassLandingNonDemo: "/ctms",
        expectedBypassLandingDemo: "/ctms",
        canAccessSafety: true,
      },
      {
        username: "dm.lead",
        expectedPersona: "data_manager",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/rules",
        canAccessSafety: true,
      },
      {
        username: "safety.officer",
        expectedPersona: "safety_officer",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/safety",
        canAccessSafety: true,
      },
      {
        username: "auditor.lead",
        expectedPersona: "auditor",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/audit",
        canAccessSafety: true,
      },
      {
        username: "stat.author",
        expectedPersona: "unblinded_statistician",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/studies",
        canAccessSafety: false,
      },
      {
        username: "stat.approver",
        expectedPersona: "unblinded_statistician",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/studies",
        canAccessSafety: false,
      },
      {
        username: "pharmacist.site101",
        expectedPersona: "pharmacist",
        expectedBypassLandingNonDemo: "/studies",
        expectedBypassLandingDemo: "/studies",
        canAccessSafety: false,
      },
    ];

    for (const expected of expectedStaffAccounts) {
      const realmUser = realmUsers.find(
        (u) => u.username === expected.username
      );
      expect(realmUser, `realm user ${expected.username}`).toBeDefined();

      const manifestUser = DEMO_PERSONAS.find(
        (u) => u.username === expected.username
      );
      expect(manifestUser, `manifest user ${expected.username}`).toBeDefined();
      expect([...manifestUser!.realmRoles]).toEqual(realmUser!.realmRoles);
      expect(manifestUser!.workspacePersona).toBe(expected.expectedPersona);

      vi.stubEnv("VITE_DEMO_MODE", "false");
      const authStore = useAuthStore();
      const onboardingStore = useOnboardingStore();
      authStore.setAuth(buildRealmKeycloakSession(realmUser!));

      expect(authStore.currentPersona, `${expected.username} persona`).toBe(
        expected.expectedPersona
      );
      expect(authStore.availablePersonas.length).toBeGreaterThan(0);

      // Default landing is /overview when bypassOverview is false
      expect(onboardingStore.bypassOverview).toBe(false);
      await router.push("/");
      expect(router.currentRoute.value.path).toBe("/overview");

      // Bypass landing routes to authorized non-demo primary workspace
      onboardingStore.setBypassOverview(true);
      await router.push("/");
      const nonDemoLanding = router.currentRoute.value.path;
      expect(nonDemoLanding, `${expected.username} non-demo landing`).toBe(
        expected.expectedBypassLandingNonDemo
      );
      expect(
        isRouteAuthorizedForRoles(
          router.currentRoute.value,
          authStore.normalizedRoles
        ),
        `${expected.username} authorized for ${nonDemoLanding}`
      ).toBe(true);

      // Demo mode landing (e.g. /ecrf for CRC/PI, /safety for Safety Reviewer)
      vi.stubEnv("VITE_DEMO_MODE", "true");
      await router.push("/overview");
      await router.push("/");
      const demoLanding = router.currentRoute.value.path;
      expect(demoLanding, `${expected.username} demo landing`).toBe(
        expected.expectedBypassLandingDemo
      );

      // /safety route guard check
      expect(
        isRouteAuthorizedForRoles(safetyRoute!, authStore.normalizedRoles),
        `${expected.username} /safety access`
      ).toBe(expected.canAccessSafety);
    }

    // Also verify a standalone Safety Reviewer without Sponsor Admin resolves safety_officer and /safety access
    const authStore = useAuthStore();
    authStore.setAuth({
      authenticated: true,
      token: "standalone-safety-token",
      tokenParsed: {
        sub: "safety-reviewer-only",
        preferred_username: "pv.reviewer",
        tenant_id: "cadence-local",
        exp: Math.floor(Date.now() / 1000) + 3600,
        realm_access: { roles: ["Safety Reviewer"] },
      },
    });
    expect(authStore.currentPersona).toBe("safety_officer");
    expect(
      isRouteAuthorizedForRoles(safetyRoute!, authStore.normalizedRoles)
    ).toBe(true);
  });

  it("AC-2: switching #persona-select updates orientation copy, launchers, checklist items, and tour steps to the selected lens even when rawRoles holds Sponsor Admin, without mutating normalizedRoles (UX-6)", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();

    authStore.setAuth({
      authenticated: true,
      token: "oidc-admin-token",
      tokenParsed: {
        sub: "lead-admin-sub",
        preferred_username: "designer.lead",
        given_name: "Elena",
        family_name: "Vance",
        tenant_id: "tenant-cadence-01",
        exp: Math.floor(Date.now() / 1000) + 3600,
        realm_access: {
          roles: ["Sponsor Admin", "Sponsor Designer", "Data Manager"],
        },
      },
    });

    const initialNormalizedRoles = [...authStore.normalizedRoles];
    await router.push("/overview");

    const ShellWithOverview = defineComponent({
      render() {
        return h(AppShell, null, {
          default: () => h(OverviewView),
        });
      },
    });

    const wrapper = mount(ShellWithOverview, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const personaSelect = wrapper.find("#persona-select");
    expect(personaSelect.exists()).toBe(true);
    expect(personaSelect.findAll("option")).toHaveLength(10);

    const lensesToVerify = [
      "sponsor_designer",
      "site_crc",
      "site_investigator",
      "cra_monitor",
      "safety_officer",
      "auditor",
      "pharmacist",
      "unblinded_statistician",
    ] as const;

    for (const lens of lensesToVerify) {
      await personaSelect.setValue(lens);
      await flushPromises();

      const expectedCatalog = getPersonaCatalogEntry(lens, authStore.rawRoles);
      expect(authStore.currentPersona).toBe(lens);
      expect(onboardingStore.currentPersona).toBe(lens);

      // UX-6: granted token permissions are never mutated
      expect(authStore.normalizedRoles).toEqual(initialNormalizedRoles);

      // Orientation copy updates to selected lens
      expect(wrapper.find(".welcome-subtitle").text()).toContain(
        expectedCatalog.orientation.title
      );
      expect(wrapper.find(".section-intro").text()).toContain(
        expectedCatalog.orientation.roleName
      );

      // Launchers match lens-filtered routes (not Sponsor Admin's all-access launchers)
      const lensRoles = resolveLensEffectiveRoles(lens, authStore.rawRoles);
      const expectedLauncherTitles = (
        expectedCatalog.launchers as Array<{
          title: string;
          routeSpec: unknown;
        }>
      )
        .filter((l) =>
          Boolean(resolveAuthorizedTaskRoute(l.routeSpec, lensRoles))
        )
        .map((l) => l.title);
      const renderedLauncherTitles = wrapper
        .findAll(".launcher-title")
        .map((n) => n.text());
      expect(renderedLauncherTitles, `${lens} launchers`).toEqual(
        expectedLauncherTitles
      );

      // Checklist items match lens
      const expectedTaskIds = (
        expectedCatalog.checklist as Array<{ id: string; routeName?: string }>
      )
        .filter((t) => Boolean(resolveAuthorizedTaskRoute(t, lensRoles)))
        .map((t) => t.id);
      expect(
        onboardingStore.visibleTasks.map((t) => t.id),
        `${lens} checklist`
      ).toEqual(expectedTaskIds);

      // Tour steps match lens
      expect(onboardingStore.personaSteps[0].title, `${lens} tour`).toBe(
        expectedCatalog.tourSequence[0].title
      );
    }

    wrapper.unmount();
  });

  it("AC-3: switching personas during an unsaved demo eCRF edit flushes the draft under the outgoing user scope before changing identity", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const demoStore = createWebDemoStore();
    const server = setupServer(
      ...buildWebGatewayHandlers({ forceDemo: true, store: demoStore })
    );
    server.listen();
    installAuthorizedFetch(globalThis);

    try {
      const authStore = useAuthStore();
      authStore.startDemoSession("site_crc");
      const outgoingScope = storageScopeOf(authStore);
      expect(outgoingScope).toBeTruthy();

      // 1. Verify backed useEcrfForm.ts flushes pending edit under outgoing site_crc scope
      const opened = demoStore.openForm({
        enrollment_id: DEMO_PARTICIPANTS[0].enrollmentUuid,
        visit_code: "VISIT-BASELINE",
        form_code: "FORM-VITALS",
      });
      const EcrfFormHarness = defineComponent({
        setup() {
          return { ...useEcrfForm() };
        },
        render: () => h("div"),
      });

      const formWrapper = mount(EcrfFormHarness, {
        global: { plugins: [pinia] },
      });
      await formWrapper.vm.load(opened.form_id);
      await flushPromises();

      const loadedFormId = formWrapper.vm.form?.form_id;
      expect(loadedFormId).toBeTruthy();
      const firstItem = formWrapper.vm.items[0];
      expect(firstItem).toBeTruthy();

      // Edit field without waiting 500ms debounce timer
      formWrapper.vm.entries[firstItem.item_id].text = "139";
      await nextTick();

      const outgoingFormDraftKey = scopedKey(
        outgoingScope,
        ECRF_FORM_DRAFT_NAMESPACE,
        loadedFormId!
      );
      expect(outgoingFormDraftKey).toBeTruthy();
      // Still within 500ms debounce window before switchPersona
      expect(window.localStorage.getItem(outgoingFormDraftKey!)).toBeNull();

      // 2. Also verify standalone demo useEcrfDrafts.js flushes pending edit under outgoing scope
      const draftValues = ref<Record<string, string>>({ VS_HR: "72" });
      const StandaloneDraftHarness = defineComponent({
        setup() {
          const drafts = useEcrfDrafts(
            ref("DEMO-01"),
            ref("SUBJ-101"),
            ref("VISIT-BASELINE"),
            draftValues
          );
          drafts.setInitialValues({ VS_HR: "72" });
          return { drafts };
        },
        render: () => h("div"),
      });
      const standaloneWrapper = mount(StandaloneDraftHarness, {
        global: { plugins: [pinia] },
      });
      draftValues.value = { VS_HR: "88" };
      await nextTick();

      const outgoingStandaloneKey = scopedKey(
        outgoingScope,
        "ecrf_draft",
        "DEMO-01",
        "SUBJ-101",
        "VISIT-BASELINE"
      );
      expect(window.localStorage.getItem(outgoingStandaloneKey!)).toBeNull();

      // Switch persona -> flushes BOTH drafts synchronously under site_crc BEFORE identity changes to cra_monitor
      expect(authStore.switchPersona("cra_monitor")).toBe(true);
      const incomingScope = storageScopeOf(authStore);
      expect(incomingScope).toBeTruthy();
      expect(incomingScope!.userId).not.toBe(outgoingScope!.userId);

      const savedBackedDraft = window.localStorage.getItem(
        outgoingFormDraftKey!
      );
      expect(savedBackedDraft).not.toBeNull();
      expect(JSON.parse(savedBackedDraft!).items[firstItem.item_id].text).toBe(
        "139"
      );

      const savedStandaloneDraft = window.localStorage.getItem(
        outgoingStandaloneKey!
      );
      expect(savedStandaloneDraft).not.toBeNull();
      expect(JSON.parse(savedStandaloneDraft!).VS_HR).toBe("88");

      // Incoming persona has zero leaked draft keys
      const incomingFormDraftKey = scopedKey(
        incomingScope,
        ECRF_FORM_DRAFT_NAMESPACE,
        loadedFormId!
      );
      expect(window.localStorage.getItem(incomingFormDraftKey!)).toBeNull();

      standaloneWrapper.unmount();
      formWrapper.unmount();
    } finally {
      server.close();
    }
  });

  it("AC-4: completing or resetting a task in OverviewView.vue or OnboardingChecklistDrawer.vue updates the header badge and both checklists immediately", async () => {
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();

    authStore.setAuth({
      authenticated: true,
      token: "crc-token",
      tokenParsed: {
        sub: "crc-user-101",
        preferred_username: "crc.site101",
        tenant_id: "tenant-101",
        exp: Math.floor(Date.now() / 1000) + 3600,
        realm_access: { roles: ["Site Investigator", "CRC"] },
      },
    });

    await router.push("/overview");

    const ShellWithOverview = defineComponent({
      render() {
        return h(AppShell, null, {
          default: () => h(OverviewView),
        });
      },
    });

    const wrapper = mount(ShellWithOverview, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    const total = onboardingStore.totalTasksCount;
    expect(total).toBe(4);

    const headerBadge = wrapper.find("#btn-onboarding-checklist-trigger");
    expect(headerBadge.text()).toContain(`0/${total}`);
    expect(wrapper.find(".progress-badge").text()).toContain(
      `0 of ${total} tasks complete (0%)`
    );

    // Open checklist drawer so both checklists are rendered simultaneously
    await headerBadge.trigger("click");
    await nextTick();
    const drawer = wrapper.findComponent(OnboardingChecklistDrawer);
    expect(drawer.find(".progress-counts").text()).toContain(
      `0 of ${total} Tasks Completed`
    );

    // 1. Complete first task from OverviewView.vue
    const overviewCheckboxes = wrapper
      .findComponent(OverviewView)
      .findAll(".task-checkbox");
    await overviewCheckboxes[0].setValue(true);
    await nextTick();

    expect(headerBadge.text()).toContain(`1/${total}`);
    expect(wrapper.find(".progress-badge").text()).toContain(
      `1 of ${total} tasks complete (25%)`
    );
    expect(drawer.find(".progress-counts").text()).toContain(
      `1 of ${total} Tasks Completed`
    );
    expect(
      (drawer.findAll(".task-checkbox")[0].element as HTMLInputElement).checked
    ).toBe(true);

    // 2. Complete second task from OnboardingChecklistDrawer.vue
    await drawer.findAll(".task-checkbox")[1].setValue(true);
    await nextTick();

    expect(headerBadge.text()).toContain(`2/${total}`);
    expect(wrapper.find(".progress-badge").text()).toContain(
      `2 of ${total} tasks complete (50%)`
    );
    expect(drawer.find(".progress-counts").text()).toContain(
      `2 of ${total} Tasks Completed`
    );
    expect((overviewCheckboxes[1].element as HTMLInputElement).checked).toBe(
      true
    );

    // 3. Reset from OverviewView.vue -> updates header badge and both checklists immediately
    await wrapper
      .findComponent(OverviewView)
      .find(".btn-reset-checklist")
      .trigger("click");
    await nextTick();

    expect(headerBadge.text()).toContain(`0/${total}`);
    expect(wrapper.find(".progress-badge").text()).toContain(
      `0 of ${total} tasks complete (0%)`
    );
    expect(drawer.find(".progress-counts").text()).toContain(
      `0 of ${total} Tasks Completed`
    );

    // 4. Complete again and reset from OnboardingChecklistDrawer.vue
    await overviewCheckboxes[0].setValue(true);
    await nextTick();
    expect(headerBadge.text()).toContain(`1/${total}`);

    await drawer.find(".btn-reset-checklist").trigger("click");
    await nextTick();
    expect(headerBadge.text()).toContain(`0/${total}`);
    expect(wrapper.find(".progress-badge").text()).toContain(
      `0 of ${total} tasks complete (0%)`
    );

    wrapper.unmount();
  });

  it("AC-5: task and tour route resolvers authorize all 11 live screens in AVAILABLE_SCREENS according to role requirements", () => {
    const expectedLiveScreens = [
      "study",
      "ecrf",
      "observation",
      "ctms",
      "ssu",
      "etmf",
      "training",
      "tickets",
      "knowledge",
      "data-lock",
      "exports",
    ];
    expect([...AVAILABLE_SCREENS]).toEqual(expectedLiveScreens);

    const scope = {
      studyCode: "DEMO-01",
      siteId: "SITE-101",
      subjectCode: "SUBJ-101",
      observationRef: "OBS-101",
    };

    // Sponsor Admin is authorized for all 11 live screens
    for (const screen of expectedLiveScreens) {
      expect(
        resolveAuthorizedTaskRoute(
          { routeName: screen },
          ["sponsor_admin"],
          scope
        ),
        `sponsor_admin task ${screen}`
      ).toBeTruthy();
      expect(
        isTourRouteAuthorized(screen, ["sponsor_admin"]),
        `sponsor_admin tour ${screen}`
      ).toBe(true);
    }

    // Auditor: authorized on study, observation, etmf, training, tickets, knowledge, exports; denied on ecrf, ctms, ssu, data-lock
    const auditorAllowed = new Set([
      "study",
      "observation",
      "etmf",
      "training",
      "tickets",
      "knowledge",
      "exports",
    ]);
    for (const screen of expectedLiveScreens) {
      const taskRoute = resolveAuthorizedTaskRoute(
        { routeName: screen },
        ["auditor"],
        scope
      );
      const tourAllowed = isTourRouteAuthorized(screen, ["auditor"]);
      expect(Boolean(taskRoute), `auditor task ${screen}`).toBe(
        auditorAllowed.has(screen)
      );
      expect(tourAllowed, `auditor tour ${screen}`).toBe(
        auditorAllowed.has(screen)
      );
    }

    // Pharmacist: authorized on study, training, tickets, knowledge; denied on ecrf, observation, ctms, ssu, etmf, data-lock, exports
    const pharmacistAllowed = new Set([
      "study",
      "training",
      "tickets",
      "knowledge",
    ]);
    for (const screen of expectedLiveScreens) {
      const taskRoute = resolveAuthorizedTaskRoute(
        { routeName: screen },
        ["pharmacist"],
        scope
      );
      const tourAllowed = isTourRouteAuthorized(screen, ["pharmacist"]);
      expect(Boolean(taskRoute), `pharmacist task ${screen}`).toBe(
        pharmacistAllowed.has(screen)
      );
      expect(tourAllowed, `pharmacist tour ${screen}`).toBe(
        pharmacistAllowed.has(screen)
      );
    }

    // Safety screen: unavailable outside demo mode, authorized for safety_reviewer in demo mode
    expect(
      resolveAuthorizedTaskRoute(
        { routeName: "safety" },
        ["safety_reviewer"],
        scope
      )
    ).toBeNull();
    vi.stubEnv("VITE_DEMO_MODE", "true");
    expect(
      resolveAuthorizedTaskRoute(
        { routeName: "safety" },
        ["safety_reviewer"],
        scope
      )
    ).toBe("/safety?studyId=DEMO-01&siteId=SITE-101&subjectId=SUBJ-101");
    expect(isTourRouteAuthorized("safety", ["safety_reviewer"])).toBe(true);
    expect(isTourRouteAuthorized("safety", ["crc"])).toBe(false);
  });

  it("AC-6: isolates onboarding preferences per tenant + user with zero PII and clears all onboarding_* and cadence_user:* keys on sign-out", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();

    // Sign in User A
    authStore.setAuth({
      authenticated: true,
      token: "token-user-a",
      tokenParsed: {
        sub: "user-uuid-alpha",
        preferred_username: "crc.alpha",
        email: "crc.alpha@hospital.example",
        given_name: "Alice",
        family_name: "Coordinator",
        tenant_id: "tenant-uuid-01",
        exp: Math.floor(Date.now() / 1000) + 3600,
        realm_access: { roles: ["CRC"] },
      },
    });

    onboardingStore.setBypassOverview(true);
    onboardingStore.completeTask("task_crf");
    onboardingStore.startTour();
    onboardingStore.nextStep("Step 1");

    const keysAfterUserA = listLocalStorageKeys();
    const onboardingKeysUserA = keysAfterUserA.filter((k) =>
      k.includes(":onboarding:")
    );
    expect(onboardingKeysUserA.length).toBeGreaterThan(0);
    expect(
      keysAfterUserA.every((k) =>
        k.startsWith("cadence_user:tenant-uuid-01:user-uuid-alpha:")
      )
    ).toBe(true);
    expect(keysAfterUserA.some((k) => k.startsWith("onboarding_"))).toBe(false);

    // Verify zero PII (no email, name, or subject names) in keys or values
    for (const key of keysAfterUserA) {
      const value = window.localStorage.getItem(key) || "";
      expect(key).not.toContain("crc.alpha@hospital.example");
      expect(value).not.toContain("crc.alpha@hospital.example");
      expect(value).not.toContain("Alice Coordinator");
    }

    // Sign in User B on same workstation -> User A's scoped keys are purged and User B starts clean
    authStore.setAuth({
      authenticated: true,
      token: "token-user-b",
      tokenParsed: {
        sub: "user-uuid-bravo",
        preferred_username: "crc.bravo",
        tenant_id: "tenant-uuid-01",
        exp: Math.floor(Date.now() / 1000) + 3600,
        realm_access: { roles: ["CRC"] },
      },
    });

    expect(onboardingStore.bypassOverview).toBe(false);
    expect(onboardingStore.completedTasksCount).toBe(0);
    expect(onboardingStore.currentStep).toBe(1);
    expect(
      listLocalStorageKeys().some((k) => k.includes("user-uuid-alpha"))
    ).toBe(false);

    // Seed a leftover legacy onboarding_* key to prove sign-out cleans both formats
    window.localStorage.setItem("onboarding_site_crc_disabled", "true");
    onboardingStore.setBypassOverview(true);

    await authStore.logout();

    const remainingKeys = listLocalStorageKeys();
    expect(remainingKeys.filter((k) => k.startsWith("onboarding_"))).toEqual(
      []
    );
    expect(remainingKeys.filter((k) => k.startsWith("cadence_user:"))).toEqual(
      []
    );
    expect(onboardingStore.bypassOverview).toBe(false);
    expect(onboardingStore.completedTasksCount).toBe(0);
  });

  it("AC-7: advancing through guided tours preserves active studyCode outside demo mode, matches collapsed header selectors on narrow screens, and clamps 320px popovers inside viewport bounds", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "false");
    const authStore = useAuthStore();
    const onboardingStore = useOnboardingStore();

    authStore.setAuth({
      authenticated: true,
      token: "admin-tour-token",
      tokenParsed: {
        sub: "admin-tour-id",
        preferred_username: "designer.lead",
        tenant_id: "tenant-tour-01",
        exp: Math.floor(Date.now() / 1000) + 3600,
        realm_access: { roles: ["Sponsor Admin"] },
      },
    });

    // Start on study workspace /studies/DEMO-01 outside demo mode
    await router.push("/studies/DEMO-01");
    await flushPromises();
    expect(router.currentRoute.value.params.studyCode).toBe("DEMO-01");

    // Mount collapsed narrow-screen header structure (360x640 mobile viewport)
    const originalVisualViewport = window.visualViewport;
    Object.defineProperty(window, "visualViewport", {
      configurable: true,
      value: {
        width: 360,
        height: 640,
        offsetLeft: 0,
        offsetTop: 0,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      },
    });

    const secondaryMenu = document.createElement("div");
    secondaryMenu.id = "header-secondary-menu";
    secondaryMenu.style.display = "none";
    const helpBtn = document.createElement("button");
    helpBtn.id = "btn-contextual-help-trigger";
    secondaryMenu.appendChild(helpBtn);
    document.body.appendChild(secondaryMenu);

    const accountMenuBtn = document.createElement("button");
    accountMenuBtn.id = "btn-header-actions-menu";
    accountMenuBtn.getBoundingClientRect = () =>
      ({
        top: 8,
        bottom: 48,
        left: 296,
        right: 352,
        width: 56,
        height: 40,
      }) as DOMRect;
    document.body.appendChild(accountMenuBtn);

    const mobileToggle = document.createElement("button");
    mobileToggle.id = "btn-mobile-menu-toggle";
    mobileToggle.getBoundingClientRect = () =>
      ({
        top: 8,
        bottom: 48,
        left: 8,
        right: 52,
        width: 44,
        height: 40,
      }) as DOMRect;
    document.body.appendChild(mobileToggle);

    const mountNode = document.createElement("div");
    document.body.appendChild(mountNode);

    onboardingStore.loadPersonaState("super_admin");
    onboardingStore.startTour();

    const wrapper = mount(OnboardingTour, {
      global: { plugins: [pinia, router] },
      attachTo: mountNode,
    });
    await flushPromises();

    try {
      // Step 1 -> Step 2 (study, anchors to #btn-mobile-menu-toggle on narrow screen)
      await wrapper.find(".btn-tour-next").trigger("click");
      await flushPromises();
      await new Promise((r) => requestAnimationFrame(r));

      expect(onboardingStore.currentStep).toBe(2);
      expect(router.currentRoute.value.path).toBe("/studies/DEMO-01");
      const cardEl = wrapper.find(".onboarding-popover-card")
        .element as HTMLElement;
      // 320px card on 360px viewport: maxWidth = 336px, minCenterX = 12 + 160 = 172px -> left edge = 12px >= 12px!
      expect(cardEl.style.maxWidth).toBe("336px");
      expect(cardEl.style.left).toBe("172px");

      // Step 2 -> Step 3 (ecrf -> redirects outside demo mode to /studies/DEMO-01?stage=conduct, preserving DEMO-01!)
      await wrapper.find(".btn-tour-next").trigger("click");
      await vi.waitFor(() => {
        expect(onboardingStore.currentStep).toBe(3);
        expect(router.currentRoute.value.path).toBe("/studies/DEMO-01");
        expect(router.currentRoute.value.query.stage).toBe("conduct");
      });

      // Step 3 -> Step 4 (ctms -> preserves studyId=DEMO-01!)
      await wrapper.find(".btn-tour-next").trigger("click");
      await vi.waitFor(() => {
        expect(onboardingStore.currentStep).toBe(4);
        expect(router.currentRoute.value.path).toBe("/ctms");
        expect(router.currentRoute.value.query.studyId).toBe("DEMO-01");
      });

      // Step 4 -> Step 5 (study -> restores /studies/DEMO-01 AND resolves collapsed #btn-contextual-help-trigger to visible #btn-header-actions-menu!)
      await wrapper.find(".btn-tour-next").trigger("click");
      await vi.waitFor(() => {
        expect(onboardingStore.currentStep).toBe(5);
        expect(router.currentRoute.value.path).toBe("/studies/DEMO-01");
      });
      await new Promise((r) => requestAnimationFrame(r));
      // #btn-header-actions-menu is near right edge (center 324px); maxCenterX = 360 - 12 - 160 = 188px -> right edge = 348px = 360 - 12px!
      expect(cardEl.style.left).toBe("188px");
      expect(cardEl.style.top).toBe("60px");
    } finally {
      wrapper.unmount();
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        value: originalVisualViewport,
      });
      document.body.removeChild(mountNode);
      document.body.removeChild(mobileToggle);
      document.body.removeChild(accountMenuBtn);
      document.body.removeChild(secondaryMenu);
    }
  });
});
