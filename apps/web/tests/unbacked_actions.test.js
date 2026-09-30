import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { ref } from "vue";
import {
  allowUnbackedAction,
  unbackedActionsEnabled,
} from "../src/utils/unbackedActions.js";
import { useToastStore } from "../src/stores/toasts.js";
import { useClinicalStore } from "../src/stores/clinical";
import { useEconsentStore } from "../src/stores/econsent.js";
import { useConsentGating } from "../src/composables/useConsentGating";
import TicketSignModal from "../src/components/tickets/TicketSignModal.vue";
import MedicalAutocodeWidget from "../src/components/crf/widgets/MedicalAutocodeWidget.vue";

// #4827: regulated actions with no backend must not record anything or report success outside demo builds.
describe("Regulated actions without a backend", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.useRealTimers();
  });

  it("refuses outside a demo build and tells the user nothing was saved", () => {
    expect(allowUnbackedAction("Signing a ticket")).toBe(false);
    expect(unbackedActionsEnabled()).toBe(false);

    const [toast] = useToastStore().toasts;
    expect(toast.tone).toBe("error");
    expect(toast.title).toBe("Signing a ticket isn't available yet");
    expect(toast.message).toContain("nothing was saved");
  });

  it("allows the action in a demo build without a warning", () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");

    expect(allowUnbackedAction("Signing a ticket")).toBe(true);
    expect(unbackedActionsEnabled()).toBe(true);
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it("does not enrol a subject or write a ledger block", async () => {
    const store = useClinicalStore();
    const subjectsBefore = store.subjects.length;
    const ledger = vi.spyOn(store, "addLedgerBlock");

    const result = await store.enrollSubject({ id: "SUBJ-NEW" });

    expect(result).toBeNull();
    expect(store.subjects).toHaveLength(subjectsBefore);
    expect(ledger).not.toHaveBeenCalled();
  });

  it("keeps the re-consent gate locked", async () => {
    const store = useClinicalStore();
    const clear = vi.spyOn(store, "clearReconsentGate");
    const gating = useConsentGating(ref("SUBJ-002"));
    gating.showEconsentModal.value = true;

    await gating.handleCompleteReconsent("ECONSENT");

    expect(clear).not.toHaveBeenCalled();
    expect(gating.showEconsentModal.value).toBe(true);
  });

  it("neither records a consent signature nor publishes an ICF version", () => {
    const store = useEconsentStore();
    const versionBefore = store.currentIcf?.version;

    expect(store.recordSignedConsent({ signerName: "P" })).toBe(false);
    expect(store.signedConsents).toHaveLength(0);
    expect(store.publishIcfVersion("reason")).toBe(false);
    expect(store.currentIcf?.version).toBe(versionBefore);
  });

  it("does not issue an electronic signature for a ticket", async () => {
    const wrapper = mount(TicketSignModal, {
      props: { isOpen: true, ticket: { id: "tk-1", version_index: 1 } },
    });
    await wrapper.find("input[type='password']").setValue("secret");

    await wrapper.vm.handleSign();

    expect(wrapper.emitted("signed")).toBeUndefined();
    expect(wrapper.text()).toContain("Nothing was signed");
  });

  it("suggests no made-up medical codes", async () => {
    vi.useFakeTimers();
    try {
      const wrapper = mount(MedicalAutocodeWidget, {
        props: { modelValue: "", dictionary: "MedDRA" },
      });

      await wrapper.find("input").setValue("headache");
      vi.advanceTimersByTime(500);
      await wrapper.vm.$nextTick();

      expect(wrapper.text()).not.toContain("10019211");
      expect(wrapper.emitted("update:modelValue")?.at(-1)).toEqual(["headache"]);
    } finally {
      vi.useRealTimers();
    }
  });
});
