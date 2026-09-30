import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SensorTelemetryConsole from "../src/components/clinical/SensorTelemetryConsole.vue";
import { useToastStore } from "../src/stores/toasts";
import { interopService } from "../src/api/interop";
import { ApiError } from "../src/api/client";

describe("SensorTelemetryConsole Failure & Toast Handling", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function mountConsole() {
    // Default mock implementation returning fulfilled promises with sample data
    vi.spyOn(interopService, "listSensorDevices").mockResolvedValue({
      data: {
        devices: [
          {
            device_id: "DEV-1",
            study_id: "STUDY-901",
            site_id: "SITE-101",
            subject_id: "SUBJ-101",
            device_type: "CGM",
            vendor: "Dexcom",
            model_name: "G7",
            udi_di: "UDI-001",
            mac_address_masked: "00:1A:7D:**:**:13",
            device_token: "TOK-123",
            calibration_status: "Calibrated",
          },
        ],
      },
      response: new Response("{}"),
    });

    vi.spyOn(interopService, "listDailySummaries").mockResolvedValue({
      data: {
        summaries: [
          {
            summary_id: "SUM-1",
            study_id: "STUDY-901",
            site_id: "SITE-101",
            subject_id: "SUBJ-101",
            summary_date: "2026-08-23",
            device_type: "CGM",
            device_token: "TOK-123",
            cgm: {
              tir_percentage: 80,
              mean_glucose_mg_dl: 120,
              glucose_sd: 15,
              glucose_cv_percent: 25,
              readings_count: 288,
            },
          },
        ],
      },
      response: new Response("{}"),
    });

    vi.spyOn(interopService, "listSafetyAlerts").mockResolvedValue({
      data: {
        alerts: [
          {
            alert_id: "ALT-101",
            study_id: "STUDY-901",
            subject_id: "SUBJ-101",
            device_type: "CGM",
            severity: "CRITICAL",
            parameter_name: "Glucose",
            observed_value: 45,
            threshold_value: 54,
            unit: "mg/dL",
            triggered_at: "2026-08-23T10:00:00Z",
            is_acknowledged: false,
          },
        ],
      },
      response: new Response("{}"),
    });

    vi.spyOn(interopService, "getSensorMetrics").mockResolvedValue({
      data: {
        metrics: {
          total_streams_ingested: 14,
          total_readings_ingested: 4032,
          active_devices_count: 6,
          mean_time_in_range_percent: 78.4,
          mean_daily_steps: 8420,
          mean_resting_hr_bpm: 64.2,
          mean_fev1_liters: 3.45,
        },
      },
      response: new Response("{}"),
    });

    const wrapper = mount(SensorTelemetryConsole, {
      props: { studyId: "STUDY-901" },
      global: { plugins: [pinia] },
    });
    await flushPromises();
    return wrapper;
  }

  it("refreshData evaluates rejected promise outcomes from Promise.allSettled and records per-domain error states without wiping prior data", async () => {
    const wrapper = await mountConsole();

    // Verify initial data loaded
    expect(wrapper.vm.devices.length).toBe(1);
    expect(wrapper.vm.safetyAlerts.length).toBe(1);

    // Mock failure for listSafetyAlerts and getSensorMetrics
    vi.spyOn(interopService, "listSafetyAlerts").mockRejectedValue(
      new ApiError("Safety alert service unavailable", 503)
    );
    vi.spyOn(interopService, "getSensorMetrics").mockRejectedValue(
      new ApiError("Metrics service timeout", 504)
    );

    // Trigger refresh
    await wrapper.vm.refreshData();
    await flushPromises();

    // Verify per-domain error states
    expect(wrapper.vm.domainErrors.alerts).toContain("Safety alert service unavailable (HTTP 503)");
    expect(wrapper.vm.domainErrors.metrics).toContain("Metrics service timeout (HTTP 504)");
    expect(wrapper.vm.domainErrors.devices).toBeNull();
    expect(wrapper.vm.domainErrors.summaries).toBeNull();
    expect(wrapper.vm.fetchError).toBe("Failed to refresh some sensor telemetry data.");

    // Verify previously loaded data was NOT wiped
    expect(wrapper.vm.devices.length).toBe(1);
    expect(wrapper.vm.safetyAlerts.length).toBe(1);

    // Verify toastStore.apiError was invoked for the rejected calls
    const toastStore = useToastStore();
    expect(toastStore.toasts.length).toBeGreaterThanOrEqual(2);
    expect(toastStore.toasts.some((t) => t.title === "Failed to refresh safety alerts")).toBe(true);
    expect(toastStore.toasts.some((t) => t.title === "Failed to refresh sensor metrics")).toBe(true);
  });

  it("displays warning banner above Safety Alerts section when safety alert API rejects", async () => {
    vi.spyOn(interopService, "listSensorDevices").mockResolvedValue({
      data: { devices: [] },
      response: new Response("{}"),
    });
    vi.spyOn(interopService, "listDailySummaries").mockResolvedValue({
      data: { summaries: [] },
      response: new Response("{}"),
    });
    vi.spyOn(interopService, "getSensorMetrics").mockResolvedValue({
      data: { metrics: {} },
      response: new Response("{}"),
    });
    vi.spyOn(interopService, "listSafetyAlerts").mockRejectedValue(
      new ApiError("Failed to fetch safety alerts from cluster", 500)
    );

    const wrapper = mount(SensorTelemetryConsole, {
      props: { studyId: "STUDY-901" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    // Switch to 'alerts' tab
    wrapper.vm.activeTab = "alerts";
    await wrapper.vm.$nextTick();

    const banner = wrapper.find(".alerts-warning-banner");
    expect(banner.exists()).toBe(true);
    expect(banner.classes()).toContain("error-banner");
    expect(banner.text()).toContain("Failed to fetch safety alerts from cluster");
  });

  it("acknowledgment modal displays inline .error-banner on failure, keeps modal open, and retains entered notes", async () => {
    const wrapper = await mountConsole();

    // Open acknowledge modal for ALT-101
    wrapper.vm.openAcknowledgeModal(wrapper.vm.safetyAlerts[0]);
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.showAckModal).toBe(true);

    // Enter notes
    wrapper.vm.ackNotes = "Attending physician contacted; insulin adjusted.";

    // Mock acknowledgeSafetyAlert to reject
    vi.spyOn(interopService, "acknowledgeSafetyAlert").mockRejectedValue(
      new ApiError("Signature verification failed", 400)
    );

    // Submit acknowledgment
    await wrapper.vm.submitAcknowledgment();
    await wrapper.vm.$nextTick();

    // Verify modal remains open and notes intact
    expect(wrapper.vm.showAckModal).toBe(true);
    expect(wrapper.vm.ackNotes).toBe("Attending physician contacted; insulin adjusted.");

    // Verify error banner rendered in modal
    const modalBanner = wrapper.find(".modal-dialog .modal-error-banner");
    expect(modalBanner.exists()).toBe(true);
    expect(modalBanner.classes()).toContain("error-banner");
    expect(modalBanner.text()).toContain("Signature verification failed");

    // Verify toast notification dispatched
    const toastStore = useToastStore();
    expect(toastStore.toasts.some((t) => t.title === "Failed to acknowledge safety alert")).toBe(true);
  });

  it("acknowledgment modal closes and shows success toast on successful submission", async () => {
    const wrapper = await mountConsole();

    wrapper.vm.openAcknowledgeModal(wrapper.vm.safetyAlerts[0]);
    await wrapper.vm.$nextTick();

    wrapper.vm.ackNotes = "Subject contacted and verified ok.";

    vi.spyOn(interopService, "acknowledgeSafetyAlert").mockResolvedValue({
      data: { status: "acknowledged" },
      response: new Response("{}"),
    });

    await wrapper.vm.submitAcknowledgment();
    await flushPromises();

    expect(wrapper.vm.showAckModal).toBe(false);

    const toastStore = useToastStore();
    const successToast = toastStore.toasts.find((t) => t.tone === "success");
    expect(successToast).toBeDefined();
    expect(successToast.title).toBe("Alert Acknowledged");
  });

  it("device registration modal displays inline .error-banner on failure, keeps modal open, and retains inputs", async () => {
    const wrapper = await mountConsole();

    wrapper.vm.openRegisterDeviceModal();
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.showRegisterModal).toBe(true);

    wrapper.vm.registerForm.vendor = "Abbott";
    wrapper.vm.registerForm.model_name = "FreeStyle Libre 3";

    vi.spyOn(interopService, "registerSensorDevice").mockRejectedValue(
      new ApiError("Device UDI duplicate error", 409)
    );

    await wrapper.vm.submitDeviceRegistration();
    await wrapper.vm.$nextTick();

    // Verify modal remains open and inputs intact
    expect(wrapper.vm.showRegisterModal).toBe(true);
    expect(wrapper.vm.registerForm.vendor).toBe("Abbott");
    expect(wrapper.vm.registerForm.model_name).toBe("FreeStyle Libre 3");

    // Verify error banner in modal
    const modalBanner = wrapper.find(".modal-dialog .modal-error-banner");
    expect(modalBanner.exists()).toBe(true);
    expect(modalBanner.text()).toContain("Device UDI duplicate error");

    // Verify toast
    const toastStore = useToastStore();
    expect(toastStore.toasts.some((t) => t.title === "Failed to register medical IoT device")).toBe(true);
  });

  it("simulation modal displays inline .error-banner on failure, keeps modal open, and retains inputs", async () => {
    const wrapper = await mountConsole();

    wrapper.vm.openSimulateIngestModal();
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.showSimulateModal).toBe(true);
    wrapper.vm.simulateForm.subject_id = "SUBJ-999";

    vi.spyOn(interopService, "ingestSensorTelemetry").mockRejectedValue(
      new ApiError("Ingestion stream pipeline busy", 503)
    );

    await wrapper.vm.submitSimulatedIngestion();
    await wrapper.vm.$nextTick();

    // Verify modal remains open and inputs intact
    expect(wrapper.vm.showSimulateModal).toBe(true);
    expect(wrapper.vm.simulateForm.subject_id).toBe("SUBJ-999");

    // Verify error banner in modal
    const modalBanner = wrapper.find(".modal-dialog .modal-error-banner");
    expect(modalBanner.exists()).toBe(true);
    expect(modalBanner.text()).toContain("Ingestion stream pipeline busy");

    // Verify toast
    const toastStore = useToastStore();
    expect(toastStore.toasts.some((t) => t.title === "Failed to ingest biometric telemetry stream")).toBe(true);
  });
});
