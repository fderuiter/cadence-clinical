import { describe, it, expect, vi, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import App from "../src/App.vue";
import MdrView from "../src/views/MdrView.vue";
import EcrfView from "../src/views/EcrfView.vue";
import CtmsView from "../src/views/CtmsView.vue";
import AuditView from "../src/views/AuditView.vue";
import { useAuthStore } from "../src/stores/auth";
import { signInAs } from "./helpers/signIn.js";
import RulesView from "../src/views/RulesView.vue";
import ImagingView from "../src/views/ImagingView.vue";

// Setup router for testing App.vue with all routes
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/mdr" },
    { path: "/mdr", name: "mdr", component: MdrView },
    { path: "/ecrf", name: "ecrf", component: EcrfView },
    { path: "/ctms", name: "ctms", component: CtmsView },
    { path: "/audit", name: "audit", component: AuditView },
    { path: "/rules", name: "rules", component: RulesView },
    { path: "/imaging", name: "imaging", component: ImagingView },
  ],
});

describe("Vue SPA Smoke Tests", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("mounts the root App.vue component with router and pinia", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["Sponsor Admin"]);

    const wrapper = mount(App, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.text()).toContain("Cadence");
    expect(wrapper.text()).toContain("Studies");
    expect(wrapper.text()).toContain("Data collection");
    expect(wrapper.text()).not.toContain("Execution & EDC");
    // No design screen has a .NET backend yet (#4829).
    expect(wrapper.text()).not.toContain("Design & Metadata");
  });

  it("mounts MdrView and renders USDM structure", () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("MDR / Protocol Visualizer");
    expect(wrapper.html()).toContain("Schedule of Activities (SoA) Matrix");
  });

  it("mounts EcrfView focused on kernel forms outside demo builds", () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("Data collection");
    expect(wrapper.html()).toContain("Case report form");
    expect(wrapper.html()).not.toContain("Subject eCRF Data Entry Form");
  });

  it("mounts EcrfView and renders interactive CDASH form fields (demo build)", () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("Data collection");
    expect(wrapper.html()).toContain("Subject eCRF Data Entry Form");
  });

  it("mounts CtmsView and renders milestones and visits dashboards (demo build)", () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(CtmsView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("Site readiness");
    expect(wrapper.html()).toContain("Site Operational Milestones");
  });

  it("mounts AuditView and renders audit ledger component", () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(AuditView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain(
      "Regulatory Auditor &amp; Inspection Portal"
    );
    expect(wrapper.html()).toContain("GxP Execution Ledger Chain Verification");
  });

  it("mounts RulesView and renders Interactive Rules Designer", () => {
    // Editing needs a designer role; the demo build supplies one (#4523).
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);
    signInAs(useAuthStore(), ["Data Manager"]);

    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("Interactive Rules Designer");
    expect(wrapper.html()).toContain("Study Active Ruleset");
  });

  it("mounts ImagingView and renders DICOM MPR viewer with RECIST calipers", () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(ImagingView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain(
      "Medical Imaging &amp; Oncologic Adjudication Console"
    );
    expect(wrapper.html()).toContain("Longitudinal Tumor Response Trajectory");
  });
});
