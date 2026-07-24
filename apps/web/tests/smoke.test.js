import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import App from "../src/App.vue";
import MdrView from "../src/views/MdrView.vue";
import EcrfView from "../src/views/EcrfView.vue";
import CtmsView from "../src/views/CtmsView.vue";
import AuditView from "../src/views/AuditView.vue";

// Setup router for testing App.vue with all routes
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/mdr" },
    { path: "/mdr", name: "mdr", component: MdrView },
    { path: "/ecrf", name: "ecrf", component: EcrfView },
    { path: "/ctms", name: "ctms", component: CtmsView },
    { path: "/audit", name: "audit", component: AuditView },
  ],
});

describe("Vue SPA Smoke Tests", () => {
  it("mounts the root App.vue component with router and pinia", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(App, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("Cadence");
    expect(wrapper.html()).toContain("Showcase Modules");
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

  it("mounts EcrfView and renders interactive CDASH form fields", () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("eCRF Runtime Renderer");
    expect(wrapper.html()).toContain("Subject eCRF Data Entry Form");
  });

  it("mounts CtmsView and renders milestones and visits dashboards", () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = mount(CtmsView, {
      global: {
        plugins: [pinia, router],
      },
    });

    expect(wrapper.html()).toContain("Clinical Trial Management System (CTMS)");
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

    expect(wrapper.html()).toContain("Cryptographic Audit Log Inspector");
    expect(wrapper.html()).toContain("21 CFR Part 11 Cryptographic Audit Ledger");
  });
});
