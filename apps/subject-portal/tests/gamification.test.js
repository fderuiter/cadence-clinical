import { describe, it, expect, vi, beforeEach } from "vitest";
import { state, fetchGamificationProgress, processGamificationSubmission } from "../src/index.js";

describe("Participant Gamification & Compliance Streaks", () => {
  beforeEach(() => {
    state.session.isDemoMode = true;
    state.session.isOfflineMode = true;
    state.gamification = {
      subjectId: "sub_100",
      studyId: "study_001",
      currentStreak: 2,
      longestStreak: 5,
      totalSubmissions: 10,
      onTimeSubmissions: 9,
      lastSubmissionDate: new Date().toISOString(),
      milestonesAchieved: ["STREAK_3_DAY"],
      totalBonusEarned: 0.0,
      loading: false,
      error: null,
    };
    state.walletBalance = 100.0;
  });

  it("updates gamification state on ePRO submission", async () => {
    const submissionData = {
      subject_id: "sub_100",
      study_id: "study_001",
      diary_id: "inst_001",
      sequence_number: 11,
      device_timestamp: new Date().toISOString(),
    };

    await processGamificationSubmission(submissionData);

    expect(state.gamification.totalSubmissions).toBe(11);
    expect(state.gamification.currentStreak).toBe(3);
  });

  it("awards $25 stipend bonus and unlocks STREAK_7_DAY milestone upon 7-day streak", async () => {
    state.gamification.currentStreak = 6;
    state.gamification.longestStreak = 6;

    const submissionData = {
      subject_id: "sub_100",
      study_id: "study_001",
      diary_id: "inst_001",
      sequence_number: 12,
      device_timestamp: new Date().toISOString(),
    };

    await processGamificationSubmission(submissionData);

    expect(state.gamification.currentStreak).toBe(7);
    expect(state.gamification.longestStreak).toBe(7);
    expect(state.gamification.milestonesAchieved).toContain("STREAK_7_DAY");
    expect(state.gamification.totalBonusEarned).toBe(25.0);
    expect(state.walletBalance).toBe(125.0);
  });

  it("handles fetchGamificationProgress in demo mode gracefully", async () => {
    await fetchGamificationProgress("sub_100", "study_001");
    expect(state.gamification.loading).toBe(false);
    expect(state.gamification.error).toBeNull();
  });
});
