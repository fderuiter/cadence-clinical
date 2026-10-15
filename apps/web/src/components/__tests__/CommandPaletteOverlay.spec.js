import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import { nextTick } from "vue";
import CommandPaletteOverlay from "../CommandPaletteOverlay.vue";
import { useAuthStore } from "../../stores/auth";
import { useKnowledgeStore } from "../../stores/knowledge";

vi.mock("../../router/availability", async (importOriginal) => ({
  ...(await importOriginal()),
  isScreenAvailable: () => true,
}));

const DummyView = { template: "<div>Dummy</div>" };

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "home", component: DummyView },
    { path: "/overview", name: "overview", component: DummyView },
    { path: "/mdr", name: "mdr", component: DummyView },
    { path: "/digitization", name: "digitization", component: DummyView },
    { path: "/icf-builder", name: "icf-builder", component: DummyView },
    {
      path: "/econsent-authoring",
      name: "econsent-authoring",
      component: DummyView,
    },
    { path: "/amendment-diff", name: "amendment-diff", component: DummyView },
    { path: "/ecrf", name: "ecrf", component: DummyView },
    { path: "/imaging", name: "imaging", component: DummyView },
    { path: "/devices", name: "devices", component: DummyView },
    { path: "/cec", name: "cec", component: DummyView },
    { path: "/ctms", name: "ctms", component: DummyView },
    { path: "/audit", name: "audit", component: DummyView },
    { path: "/rules", name: "rules", component: DummyView },
    { path: "/coding", name: "coding", component: DummyView },
    { path: "/data-lock", name: "data-lock", component: DummyView },
    { path: "/lakehouse", name: "lakehouse", component: DummyView },
    { path: "/sensors", name: "sensors", component: DummyView },
    { path: "/labs", name: "labs", component: DummyView },
    { path: "/dct", name: "dct", component: DummyView },
    { path: "/safety", name: "safety", component: DummyView },
    { path: "/exports", name: "exports", component: DummyView },
    { path: "/etmf", name: "etmf", component: DummyView },
    { path: "/quality", name: "quality", component: DummyView },
    { path: "/notifications", name: "notifications", component: DummyView },
    { path: "/tickets", name: "tickets", component: DummyView },
    { path: "/documents", name: "documents", component: DummyView },
    { path: "/knowledge", name: "knowledge", component: DummyView },
    { path: "/training", name: "training", component: DummyView },
    {
      path: "/participant-wallet",
      name: "participant-wallet",
      component: DummyView,
    },
    { path: "/lay-summaries", name: "lay-summaries", component: DummyView },
    { path: "/studies", name: "studies", component: DummyView },
  ],
});

describe("CommandPaletteOverlay.vue - Searchable Command Palette Overlay", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: false,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.find(".command-palette-backdrop").exists()).toBe(false);
  });

  it("renders when isOpen is true and focuses the input", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
      attachTo: document.body,
    });

    await nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(wrapper.find(".command-palette-backdrop").exists()).toBe(true);
    expect(wrapper.find(".command-palette-input").exists()).toBe(true);

    const input = wrapper.find(".command-palette-input").element;
    expect(document.activeElement).toBe(input);

    wrapper.unmount();
  });

  it("dynamically filters items based on the user's roles", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Designer"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const items = wrapper.findAll(".command-item");
    const names = items.map((el) => el.find(".command-name").text());

    expect(names).toContain("MDR Protocol Designer");
    expect(names).toContain("eConsent Authoring");
    expect(names).toContain("Rules Designer");
    expect(names).toContain("Notifications");

    expect(names).not.toContain("CTMS Dashboard");
    expect(names).not.toContain("Cryptographic Ledger");
  });

  it("displays category badges ([Route], [Action], [Subject], [Document], [SOP])", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const items = wrapper.findAll(".command-item");
    const badges = items.map((el) => el.find(".command-badge").text());

    expect(badges).toContain("[Route]");
    expect(badges).toContain("[Action]");
    expect(badges).toContain("[Subject]");
    expect(badges).toContain("[Document]");
    expect(badges).toContain("[SOP]");
  });

  it("filters search results in real time as the user types across entities", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const input = wrapper.find(".command-palette-input");

    await input.setValue("CTMS Dashboard");
    let items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe("CTMS Dashboard");
    expect(items[0].find(".command-badge").text()).toBe("[Route]");

    await input.setValue("SUBJ-101");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe("Subject SUBJ-101");
    expect(items[0].find(".command-badge").text()).toBe("[Subject]");

    await input.setValue("protocol_v1");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe("protocol_v1_draft.pdf");
    expect(items[0].find(".command-badge").text()).toBe("[Document]");

    await input.setValue("SOP-ECRF-201");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toContain("SOP-ECRF-201");
    expect(items[0].find(".command-badge").text()).toBe("[SOP]");

    await input.setValue("Create Clinical Ticket");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe(
      "Create Clinical Ticket / Issue"
    );
    expect(items[0].find(".command-badge").text()).toBe("[Action]");

    await input.setValue("xyz123nonexistent");
    items = wrapper.findAll(".command-item");
    expect(items.length).toBe(0);
    expect(wrapper.find(".command-palette-no-results").text()).toContain(
      "No matching modules or commands found."
    );
  });

  it("dispatches action callbacks for action items and closes modal", async () => {
    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const knowledgeStore = useKnowledgeStore();
    const openHelpSpy = vi.spyOn(knowledgeStore, "openDrawer");

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const input = wrapper.find(".command-palette-input");

    await input.setValue("Open Contextual Help");
    const items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);

    await items[0].trigger("click");
    expect(openHelpSpy).toHaveBeenCalled();
    expect(wrapper.emitted("close")).toBeTruthy();
  });

  it("invokes AI Gateway intent endpoint and displays [AI Copilot] recommendations", async () => {
    const { apiClient } = await import("../../api/apiClient");
    vi.spyOn(apiClient, "post").mockResolvedValue({
      query: "log adverse event for subject 101",
      recommendations: [
        {
          id: "ai-intent-log-ae-101",
          name: "Log Adverse Event for Subject SUBJ-101",
          description:
            "Directly navigate to eCRF data entry for subject SUBJ-101",
          category: "[AI Copilot]",
          icon: "🩺",
          targetRoute: "/ecrf?subjectId=SUBJ-101&action=log_ae",
          action: "log_ae",
          confidence: 0.98,
          roles: ["site_investigator", "crc"],
        },
      ],
    });

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const input = wrapper.find(".command-palette-input");
    await input.setValue("log adverse event for subject 101");

    await new Promise((resolve) => setTimeout(resolve, 300));
    await nextTick();

    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/ai/intent/route", {
      query: "log adverse event for subject 101",
      context: undefined,
    });

    const aiItem = wrapper.findAll(".command-item").find((el) => {
      return el.find(".command-badge").text() === "[AI Copilot]";
    });

    expect(aiItem).toBeDefined();
    expect(aiItem.find(".command-name").text()).toBe(
      "Log Adverse Event for Subject SUBJ-101"
    );
  });

  it("enforces role restrictions on AI Copilot recommendations", async () => {
    const { apiClient } = await import("../../api/apiClient");
    vi.spyOn(apiClient, "post").mockResolvedValue({
      query: "check site status",
      recommendations: [
        {
          id: "ai-intent-ctms",
          name: "CTMS Site Monitoring",
          description: "View site visit logs",
          category: "[AI Copilot]",
          icon: "📊",
          targetRoute: "/ctms",
          confidence: 0.9,
          roles: ["cra", "monitor"],
        },
      ],
    });

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"]; // Not CRA or Monitor

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const input = wrapper.find(".command-palette-input");
    await input.setValue("check site status");

    await new Promise((resolve) => setTimeout(resolve, 300));
    await nextTick();

    const aiItem = wrapper.findAll(".command-item").find((el) => {
      return el.find(".command-badge").text() === "[AI Copilot]";
    });

    expect(aiItem).toBeUndefined();
  });

  it("navigates to target route when AI Copilot recommendation is selected", async () => {
    const { apiClient } = await import("../../api/apiClient");
    vi.spyOn(apiClient, "post").mockResolvedValue({
      query: "log adverse event for subject 101",
      recommendations: [
        {
          id: "ai-intent-log-ae-101",
          name: "Log Adverse Event for Subject SUBJ-101",
          description: "Directly navigate to eCRF",
          category: "[AI Copilot]",
          icon: "🩺",
          targetRoute: "/ecrf?subjectId=SUBJ-101&action=log_ae",
          confidence: 0.98,
          roles: ["site_investigator"],
        },
      ],
    });

    const routerPushSpy = vi.spyOn(router, "push");

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Site Investigator"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const input = wrapper.find(".command-palette-input");
    await input.setValue("log adverse event for subject 101");

    await new Promise((resolve) => setTimeout(resolve, 300));
    await nextTick();

    const aiItem = wrapper.findAll(".command-item").find((el) => {
      return el.find(".command-badge").text() === "[AI Copilot]";
    });

    expect(aiItem).toBeDefined();
    await aiItem.trigger("click");

    expect(routerPushSpy).toHaveBeenCalledWith(
      "/ecrf?subjectId=SUBJ-101&action=log_ae"
    );
    expect(wrapper.emitted("close")).toBeTruthy();
  });

  it("falls back to static substring filtering without UI errors when AI Gateway fails", async () => {
    const { apiClient } = await import("../../api/apiClient");
    vi.spyOn(apiClient, "post").mockRejectedValueOnce(
      new Error("Network Error 500")
    );

    const authStore = useAuthStore();
    authStore.isAuthenticated = true;
    authStore.rawRoles = ["Sponsor Admin"];

    const wrapper = mount(CommandPaletteOverlay, {
      props: {
        isOpen: true,
      },
      global: {
        plugins: [pinia, router],
      },
    });

    const input = wrapper.find(".command-palette-input");
    await input.setValue("CTMS Dashboard");

    await new Promise((resolve) => setTimeout(resolve, 300));
    await nextTick();

    const items = wrapper.findAll(".command-item");
    expect(items.length).toBe(1);
    expect(items[0].find(".command-name").text()).toBe("CTMS Dashboard");
    expect(items[0].find(".command-badge").text()).toBe("[Route]");
  });
});
