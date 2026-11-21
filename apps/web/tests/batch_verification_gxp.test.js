import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useRouter } from "vue-router";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";
import { useNotificationsStore } from "../src/stores/notifications";
import { soaClient } from "../src/api/soaClient";
import EcrfView from "../src/views/EcrfView.vue";

// Mock the router hooks
vi.mock("vue-router", () => {
  const push = vi.fn();
  return {
    onBeforeRouteLeave: () => {},
    useRouter: () => ({
      push,
    }),
    useRoute: () => ({
      query: {
        studyId: "TEST-STUDY",
        siteId: "TEST-SITE",
        subjectId: "SUBJ-002",
        visitId: "Week2",
      },
    }),
  };
});

// Mock the soaClient backend API calls
vi.mock("../src/api/soaClient", () => {
  return {
    soaClient: {
      verifySignature: vi.fn(),
    },
  };
});

describe("Source Data Verification and GxP Compliance Flow Tests", () => {
  let pinia;
  let clinicalStore;
  let authStore;
  let notificationsStore;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    clinicalStore = useClinicalStore();
    authStore = useAuthStore();
    notificationsStore = useNotificationsStore();

    // Prevent network fetch in onMounted
    vi.spyOn(clinicalStore, "loadSubjects").mockResolvedValue([]);

    // Reset router push spy
    useRouter().push.mockClear();

    // Setup Auth Store values
    authStore.isAuthenticated = true;
    authStore.user = {
      username: "cra_tester",
      email: "cra_tester@example.com",
    };
    authStore.rawRoles = ["cra"];

    // Setup mocks
    vi.mocked(soaClient.verifySignature).mockReset();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("successfully parses route query parameters and preserves study/site context on land", () => {
    mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Check store synchronization on mount outside demo mode
    expect(clinicalStore.activeStudyId).toBe("TEST-STUDY");
    expect(clinicalStore.activeSiteId).toBe("TEST-SITE");
    expect(clinicalStore.activeSubjectId).toBe("SUBJ-002");
  });

  it("automatically clears verification status and dispatches an high-priority alert notification when a verified field is modified (demo build)", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Pre-mark field as verified
    const sKey = wrapper.vm.getSdvKey("vssbp");
    wrapper.vm.sdvStates[sKey] = true;

    // Ensure notifications store is clean
    notificationsStore.notifications = [];

    // Trigger value change to commit a modification
    wrapper.vm.commitChange(
      { id: "vssbp", label: "Systolic Blood Pressure" },
      "120",
      "135",
      "Correction of typographical error"
    );

    // Status should be cleared
    expect(wrapper.vm.sdvStates[sKey]).toBe(false);

    // Verify a high priority ALERTS notification has been pushed to the workspace inbox
    const activeAlerts = notificationsStore.notifications.filter(
      (n) => n.category === "ALERTS" && n.priority === "HIGH"
    );
    expect(activeAlerts.length).toBe(1);
    expect(activeAlerts[0].message_content).toContain(
      "Verification cleared automatically"
    );
    expect(activeAlerts[0].related_entity_id).toBe("vssbp");
  });
});
