import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ProtocolTrainingConsole from "../../src/components/clinical/ProtocolTrainingConsole.vue";
import { trainingApi } from "../../src/api/training";

describe("ProtocolTrainingConsole.vue", () => {
  const mockAssignment = {
    id: "asn-101",
    user_id: "site_investigator_01",
    user_name: "Dr. Eleanor Vance",
    user_role: "Principal Investigator",
    study_id: "STUDY-2026",
    site_id: "SITE-01",
    status: "IN_PROGRESS",
    time_spent_seconds: 0,
    quiz_score: null,
    completed_at: null,
    curriculum: {
      id: "curr-1",
      title: "Protocol Version 2.0 Delta Training",
      protocol_version: "2.0",
      modules: [
        {
          id: "mod-1",
          title: "Introduction to Protocol Changes",
          module_type: "PROCEDURE_DELTA",
          min_reading_time_seconds: 30,
          content_text: "Detailed procedural updates for Version 2.0...",
          quiz_questions: [
            {
              id: "q-1",
              question: "What is the updated dose adjustment window?",
              options: ["12 hours", "24 hours", "48 hours"],
              correct_option_index: 1,
              rationale: "Section 4.2 specifies a 24-hour window.",
            },
          ],
        },
      ],
    },
  };

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();

    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([
      mockAssignment,
    ]);
    vi.spyOn(trainingApi, "getAssignmentDetail").mockResolvedValue(
      mockAssignment
    );
    vi.spyOn(trainingApi, "recordProgress").mockResolvedValue({
      success: true,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it("mounts and starts timer under normal conditions", async () => {
    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    expect(wrapper.find(".console-title").text()).toContain(
      "Protocol Amendment Training & Qualification Console"
    );
    expect(wrapper.find(".sync-error-banner").exists()).toBe(false);

    // Advance by 15 seconds to trigger heartbeat
    await vi.advanceTimersByTimeAsync(15000);

    expect(trainingApi.recordProgress).toHaveBeenCalledWith("asn-101", {
      time_spent_delta_seconds: 15,
      reason_for_change: "Heartbeat training time-on-page recording",
    });
    expect(wrapper.find(".sync-error-banner").exists()).toBe(false);
  });

  it("halts client timer immediately when recordProgress heartbeat fails and renders inline banner", async () => {
    vi.spyOn(trainingApi, "recordProgress").mockRejectedValue(
      new Error("Network connection error: 503 Service Unavailable")
    );

    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    expect(wrapper.find(".sync-error-banner").exists()).toBe(false);

    // Advance to 15s to fire heartbeat and fail
    await vi.advanceTimersByTimeAsync(15000);

    expect(wrapper.find(".sync-error-banner").exists()).toBe(true);
    expect(wrapper.find(".banner-message").text()).toContain(
      "Network connection error: 503 Service Unavailable"
    );

    const timeSpentBefore = (wrapper.vm as any).totalTimeSpent;

    // Advance timer by 5 more seconds - since syncError is true, timer must not advance
    await vi.advanceTimersByTimeAsync(5000);

    const timeSpentAfter = (wrapper.vm as any).totalTimeSpent;
    expect(timeSpentAfter).toBe(timeSpentBefore);
  });

  it("clears sync error, hides banner, and resumes timer progression on successful retry", async () => {
    const recordSpy = vi
      .spyOn(trainingApi, "recordProgress")
      .mockRejectedValueOnce(new Error("Part 11 Heartbeat Failed"));

    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    // Trigger heartbeat failure
    await vi.advanceTimersByTimeAsync(15000);

    expect(wrapper.find(".sync-error-banner").exists()).toBe(true);
    expect(wrapper.find(".banner-message").text()).toContain(
      "Part 11 Heartbeat Failed"
    );

    // Mock successful retry
    recordSpy.mockResolvedValueOnce({ success: true });

    const retryBtn = wrapper.find(".btn-retry");
    expect(retryBtn.exists()).toBe(true);
    await retryBtn.trigger("click");
    await vi.runOnlyPendingTimersAsync();

    expect(wrapper.find(".sync-error-banner").exists()).toBe(false);

    // Verify timer resumes advancing
    const timeSpentBefore = (wrapper.vm as any).totalTimeSpent;
    await vi.advanceTimersByTimeAsync(2000);
    const timeSpentAfter = (wrapper.vm as any).totalTimeSpent;

    expect(timeSpentAfter).toBe(timeSpentBefore + 2);
  });

  it("resets sync error state when switching active assignment", async () => {
    vi.spyOn(trainingApi, "recordProgress").mockRejectedValue(
      new Error("Authentication expired")
    );

    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    // Trigger error
    await vi.advanceTimersByTimeAsync(15000);
    expect(wrapper.find(".sync-error-banner").exists()).toBe(true);

    // Switch assignment
    (wrapper.vm as any).onAssignmentChange();
    await vi.runOnlyPendingTimersAsync();

    expect((wrapper.vm as any).syncError).toBe(false);
    expect(wrapper.find(".sync-error-banner").exists()).toBe(false);
  });
});
