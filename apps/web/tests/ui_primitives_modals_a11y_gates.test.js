import { describe, it, expect } from "vitest";
import { h } from "vue";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia } from "pinia";
import fs from "node:fs";
import path from "node:path";
import { Linter } from "eslint";
import vueParser from "vue-eslint-parser";
import {
  ClinicalFieldLayout,
  ClinicalInput,
  ClinicalFormField,
  ClinicalRadioGroup,
  ClinicalLookupInput,
} from "ui";
import ReasonModal from "../src/components/ReasonModal.vue";
import ReAuthModal from "../src/components/shared/ReAuthModal.vue";
import SignatureCaptureModal from "../src/features/signatures/components/SignatureCaptureModal.vue";
import TicketSignModal from "../src/components/tickets/TicketSignModal.vue";
import AuditTrailViewer from "../src/components/auditor/AuditTrailViewer.vue";
import IcfSectionEditor from "../src/components/econsent/IcfSectionEditor.vue";
import eslintConfig from "../../../eslint.config.mjs";

describe("UI primitives constraint forwarding & slot context (ADR-2679)", () => {
  it("keeps only data-* attributes on ClinicalFieldLayout wrapper and exposes unit, range, role=alert, and slot props", () => {
    const wrapper = mount(ClinicalFieldLayout, {
      props: {
        id: "hr",
        label: "Heart Rate",
        required: true,
        unit: "bpm",
        range: "60-100 bpm",
        hint: "Resting",
        error: "Bradycardia alert",
        attributes: {
          "data-cdash": "VSORRES_HR",
          placeholder: "ShouldNotLeak",
          min: 30,
        },
      },
      slots: {
        default: (slotProps) =>
          h("input", {
            id: slotProps.id,
            "aria-describedby": slotProps.describedBy,
            "data-has-error": String(slotProps.hasError),
            "data-req": String(slotProps.required),
            "data-err-id": slotProps.errorId,
          }),
      },
    });

    expect(wrapper.attributes("data-cdash")).toBe("VSORRES_HR");
    expect(wrapper.attributes("placeholder")).toBeUndefined();
    expect(wrapper.attributes("min")).toBeUndefined();
    expect(wrapper.find(".clinical-field-unit").text()).toBe("bpm");
    expect(wrapper.find(".clinical-field-range").text()).toBe("60-100 bpm");
    expect(wrapper.find(".validation-error-msg").attributes("role")).toBe(
      "alert"
    );

    const inner = wrapper.find("input");
    expect(inner.attributes("aria-describedby")).toBe(
      "field-unit-hr field-range-hr field-hint-hr validation-error-hr"
    );
    expect(inner.attributes("data-has-error")).toBe("true");
    expect(inner.attributes("data-req")).toBe("true");
  });

  it("forwards constraints and aria-describedby across ClinicalFormField, ClinicalInput, ClinicalRadioGroup, and ClinicalLookupInput without clobbering attributes", () => {
    const numField = mount(ClinicalFormField, {
      props: {
        field: {
          id: "temp",
          label: "Temperature",
          type: "number",
          required: true,
          min: 34,
          max: 42,
          step: 0.1,
          unit: "C",
          attributes: { "data-cdash": "TEMP" },
        },
        modelValue: "36.8",
        error: "Check value",
      },
    });
    const numInput = numField.find("input");
    expect(
      numField.find("#field-container-temp").attributes("data-cdash")
    ).toBe("TEMP");
    expect(numInput.attributes("type")).toBe("number");
    expect(numInput.element.required).toBe(true);
    expect(numInput.attributes("min")).toBe("34");
    expect(numInput.attributes("max")).toBe("42");
    expect(numInput.attributes("step")).toBe("0.1");
    expect(numInput.attributes("aria-invalid")).toBe("true");
    expect(numInput.attributes("aria-describedby")).toContain(
      "field-unit-temp"
    );
    expect(numInput.attributes("aria-describedby")).toContain(
      "validation-error-temp"
    );

    const attrInput = mount(ClinicalInput, {
      props: {
        id: "dia",
        label: "Diastolic",
        modelValue: "80",
        attributes: { min: 40, max: 140, required: true, type: "number" },
      },
    });
    expect(attrInput.find("input").attributes("min")).toBe("40");
    expect(attrInput.find("input").attributes("max")).toBe("140");
    expect(attrInput.find("input").element.required).toBe(true);

    const radio = mount(ClinicalRadioGroup, {
      props: {
        id: "fasting",
        label: "Fasting",
        required: true,
        hint: "8h fast",
        error: "Required selection",
        options: [
          { value: "Y", label: "Yes" },
          { value: "N", label: "No" },
        ],
      },
    });
    const firstRadio = radio.find('input[type="radio"]');
    expect(firstRadio.element.required).toBe(true);
    expect(firstRadio.attributes("aria-invalid")).toBe("true");
    expect(firstRadio.attributes("aria-describedby")).toContain(
      "field-hint-fasting"
    );
    expect(firstRadio.attributes("aria-describedby")).toContain(
      "validation-error-fasting"
    );

    const lookup = mount(ClinicalLookupInput, {
      props: {
        id: "meddra",
        label: "MedDRA Code",
        required: true,
        status: "valid",
        hint: "LLT code",
      },
    });
    const lookupEl = lookup.find("input");
    expect(lookupEl.element.required).toBe(true);
    expect(lookupEl.attributes("aria-describedby")).toContain(
      "lookup-status-meddra"
    );
    expect(lookupEl.attributes("aria-describedby")).toContain(
      "field-hint-meddra"
    );
  });
});

describe("Regulated modals <form> Enter submission, retry focus, and autocomplete (ADR-2679)", () => {
  it("submits ReasonModal via <form> and focuses #change-reason-text when narrative is missing", async () => {
    const wrapper = mount(ReasonModal, {
      attachTo: document.body,
      props: {
        show: true,
        options: [
          {
            value: "Typographical Error",
            text: "Correction of typographical error",
          },
          {
            value: "Transcription Error",
            text: "Source data transcription error",
          },
          { value: "Other", text: "Other (specify below)" },
        ],
        defaultOption: "Other",
      },
    });
    await flushPromises();
    const form = wrapper.find("form");
    expect(form.exists()).toBe(true);
    const submitBtn = wrapper.find('button[type="submit"]');
    expect(submitBtn.attributes("form")).toBe(form.attributes("id"));

    await form.trigger("submit");
    await flushPromises();
    expect(document.activeElement?.id).toBe("change-reason-text");

    await wrapper.find("#change-reason-text").setValue("Verified source chart");
    await form.trigger("submit");
    expect(wrapper.emitted("confirm")?.[0]).toEqual(["Verified source chart"]);
    wrapper.unmount();
  });

  it("mounts ReAuthModal on ClinicalModal, submits via <form>, clears shadowed errors on retry/dismiss, and focuses #reauth-credential", async () => {
    const wrapper = mount(ReAuthModal, {
      attachTo: document.body,
      props: {
        isOpen: true,
        username: "dr.chen",
        errorMessage: "Invalid password",
      },
    });
    await flushPromises();
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true);
    expect(wrapper.find("#reauth-username").attributes("autocomplete")).toBe(
      "username"
    );
    expect(wrapper.find("#reauth-credential").attributes("autocomplete")).toBe(
      "current-password"
    );

    const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
    expect(retryBtn.exists()).toBe(true);
    await retryBtn.trigger("click");
    await flushPromises();
    expect(wrapper.text()).not.toContain("Invalid password");
    expect(document.activeElement?.id).toBe("reauth-credential");

    await wrapper
      .find('[data-testid="modal-error-dismiss-btn"]')
      .trigger("click");
    await flushPromises();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);

    await wrapper.find("#reauth-credential").setValue("Secret123!");
    await wrapper.find("form").trigger("submit");
    expect(wrapper.emitted("confirm")?.[0]?.[0]).toMatchObject({
      credential: "Secret123!",
    });
    wrapper.unmount();
  });

  it("binds autocomplete presets and focuses #sig-username / #sig-password / #sig-reason on SignatureCaptureModal and TicketSignModal", async () => {
    const pinia = createPinia();
    const sigModal = mount(SignatureCaptureModal, {
      attachTo: document.body,
      global: { plugins: [pinia] },
      props: {
        isOpen: true,
        actionUrl: "/api/v1/etmf/documents/doc-1/sign-off",
      },
    });
    await flushPromises();
    expect(sigModal.find("#sig-signer-name").attributes("autocomplete")).toBe(
      "name"
    );
    expect(sigModal.find("#sig-username").attributes("autocomplete")).toBe(
      "username"
    );
    expect(sigModal.find("#sig-password").attributes("autocomplete")).toBe(
      "current-password"
    );
    expect(sigModal.find("#sig-totp").attributes("autocomplete")).toBe(
      "one-time-code"
    );

    await sigModal.find("form").trigger("submit");
    await flushPromises();
    expect(document.activeElement?.id).toBe("sig-username");

    await sigModal.find("#sig-username").setValue("dr.chen");
    await sigModal
      .find('[data-testid="modal-error-retry-btn"]')
      .trigger("click");
    await flushPromises();
    expect(document.activeElement?.id).toBe("sig-password");

    await sigModal.find("#sig-password").setValue("Secret123!");
    await sigModal
      .find('[data-testid="modal-error-retry-btn"]')
      .trigger("click");
    await flushPromises();
    expect(document.activeElement?.id).toBe("sig-reason");
    sigModal.unmount();

    const ticketModal = mount(TicketSignModal, {
      attachTo: document.body,
      global: { plugins: [pinia] },
      props: {
        isOpen: true,
        ticket: { id: "t-1", ticket_number: "TKT-1", title: "Verify lock" },
      },
    });
    await flushPromises();
    expect(
      ticketModal.find("#signature-password").attributes("autocomplete")
    ).toBe("current-password");
    await ticketModal.find("form").trigger("submit");
    await flushPromises();
    expect(document.activeElement?.id).toBe("signature-password");

    await ticketModal.find("#signature-reason").setValue("");
    await ticketModal
      .find('[data-testid="modal-error-retry-btn"]')
      .trigger("click");
    await flushPromises();
    expect(document.activeElement?.id).toBe("signature-reason");
    ticketModal.unmount();
  });

  it("avoids Initial Entry in ObservationView correction reasons and labels AuditTrailViewer & IcfSectionEditor controls", () => {
    const obsSource = fs.readFileSync(
      path.resolve(__dirname, "../src/views/ObservationView.vue"),
      "utf8"
    );
    expect(obsSource).toContain('default-option="Typographical Error"');
    expect(obsSource).toContain("OBSERVATION_CORRECTION_REASON_OPTIONS");
    const match = obsSource.match(
      /OBSERVATION_CORRECTION_REASON_OPTIONS\s*=\s*\[([\s\S]*?)\]/
    );
    expect(match).toBeTruthy();
    expect(match[1]).not.toContain("Initial Entry");

    const pinia = createPinia();
    const audit = mount(AuditTrailViewer, {
      global: { plugins: [pinia] },
      props: { events: [] },
    });
    for (const control of audit.findAll("input, select")) {
      const id = control.attributes("id");
      const hasLabel =
        Boolean(control.attributes("aria-label")) ||
        Boolean(id && audit.find(`label[for="${id}"]`).exists());
      expect(hasLabel).toBe(true);
    }

    const icf = mount(IcfSectionEditor, {
      global: { plugins: [pinia] },
      props: {
        section: {
          id: "sec-1",
          title: "Study Purpose",
          html: "<p>Overview</p>",
        },
      },
    });
    const canvas = icf.find(".editor-canvas");
    if (canvas.exists()) {
      expect(canvas.attributes("aria-multiline")).toBe("true");
      expect(canvas.attributes("aria-labelledby")).toBeTruthy();
    }
  });
});

describe("ESLint accessibility & password autocomplete AST gates (ADR-2679)", () => {
  const customRuleBlock = eslintConfig.find(
    (entry) =>
      entry.plugins?.["custom-vue"]?.rules?.["require-password-autocomplete"]
  );
  const customPlugin = customRuleBlock.plugins["custom-vue"];

  it("blocks vuejs-accessibility suppressions in apps/web/src/** and missing password autocomplete", () => {
    const linter = new Linter({ configType: "flat" });
    const testConfig = [
      {
        files: ["**/*.vue"],
        languageOptions: { parser: vueParser },
        plugins: { "custom-vue": customPlugin },
        rules: {
          "custom-vue/no-vuejs-a11y-suppression": "error",
          "custom-vue/require-password-autocomplete": "error",
        },
      },
    ];

    const suppressedMsgs = linter.verify(
      `<template><!-- eslint-disable-next-line vuejs-accessibility/label-has-for --><input type="text" /></template>`,
      testConfig,
      "apps/web/src/views/demo/Example.vue"
    );
    expect(
      suppressedMsgs.some(
        (m) => m.ruleId === "custom-vue/no-vuejs-a11y-suppression"
      )
    ).toBe(true);

    const missingAutocompleteMsgs = linter.verify(
      `<template><input id="pw" type="password" /></template>`,
      testConfig,
      "apps/web/src/components/Example.vue"
    );
    expect(
      missingAutocompleteMsgs.some(
        (m) => m.ruleId === "custom-vue/require-password-autocomplete"
      )
    ).toBe(true);

    const validPasswordMsgs = linter.verify(
      `<template><input id="pw" type="password" autocomplete="current-password" /></template>`,
      testConfig,
      "apps/web/src/components/Example.vue"
    );
    expect(validPasswordMsgs).toHaveLength(0);
  });
});
