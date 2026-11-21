// Sandbox (demo/offline) fixtures, used when there is no authenticated
// gateway session. Their dates are anchored to the canonical demo epoch.
import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_EPOCH_MS,
  DEMO_SPONSOR,
  DEMO_STUDY,
} from "demo-fixtures";

const DEMO_BASE_EPOCH_MS = DEMO_EPOCH_MS || Date.UTC(2026, 0, 5, 9, 0, 0);
const DEMO_SUBJECT_ID = DEFAULT_DEMO_PARTICIPANT?.subjectCode || "";
const DEMO_STUDY_ID = DEMO_STUDY?.code || "";
const DEMO_SITE_ID = DEFAULT_DEMO_SITE?.code || "";
const DEMO_SPONSOR_ID = DEMO_SPONSOR?.code || "";

export const MOCK_ASSIGNMENTS = [
  {
    id: "assign_01",
    subject_id: DEMO_SUBJECT_ID,
    study_id: DEMO_STUDY_ID,
    site_id: DEMO_SITE_ID,
    instrument_id: "inst_daily_diary",
    instrument_name: "Daily Health & Vital Diary",
    due_at: new Date(DEMO_BASE_EPOCH_MS + 4 * 3600 * 1000).toISOString(),
    end_date: new Date(DEMO_BASE_EPOCH_MS + 24 * 3600 * 1000).toISOString(),
    status: "PENDING",
  },
  {
    id: "assign_02",
    subject_id: DEMO_SUBJECT_ID,
    study_id: DEMO_STUDY_ID,
    site_id: DEMO_SITE_ID,
    instrument_id: "inst_weekly_symptoms",
    instrument_name: "Weekly Symptoms & eCOA Checklist",
    due_at: new Date(DEMO_BASE_EPOCH_MS - 2 * 3600 * 1000).toISOString(),
    end_date: new Date(DEMO_BASE_EPOCH_MS + 12 * 3600 * 1000).toISOString(),
    status: "OVERDUE",
  },
  {
    id: "assign_03",
    subject_id: DEMO_SUBJECT_ID,
    study_id: DEMO_STUDY_ID,
    site_id: DEMO_SITE_ID,
    instrument_id: "inst_weekly_symptoms",
    instrument_name: "Weekly Symptoms & eCOA Checklist",
    due_at: new Date(DEMO_BASE_EPOCH_MS - 48 * 3600 * 1000).toISOString(),
    end_date: new Date(DEMO_BASE_EPOCH_MS - 24 * 3600 * 1000).toISOString(),
    status: "COMPLETED",
    submitted_at: new Date(DEMO_BASE_EPOCH_MS - 47 * 3600 * 1000).toISOString(),
  },
];

export const MOCK_INSTRUMENTS = {
  inst_daily_diary: {
    id: "inst_daily_diary",
    name: "Daily Health & Vital Diary",
    description:
      "Please record your systolic/diastolic blood pressure, pulse, and current symptoms.",
    items: {
      vssbp: {
        label: "Systolic Blood Pressure (mmHg)",
        type: "numeric",
        required: true,
        min: 50,
        max: 250,
      },
      vsdpb: {
        label: "Diastolic Blood Pressure (mmHg)",
        type: "numeric",
        required: true,
        min: 30,
        max: 150,
      },
      vshr: {
        label: "Pulse Rate (bpm)",
        type: "numeric",
        required: true,
        min: 30,
        max: 200,
      },
      pain_scale: {
        label: "Symptom Severity (Visual Analog Scale 0-10)",
        type: "visual_analog",
        required: true,
        min: 0,
        max: 10,
        step: 1,
        min_label: "No Pain (0)",
        max_label: "Worst Possible (10)",
      },
      has_symptoms: {
        label: "Are you experiencing any new physical symptoms today?",
        type: "choice_single",
        options: ["Yes", "No"],
      },
    },
  },
  inst_weekly_symptoms: {
    id: "inst_weekly_symptoms",
    name: "Weekly Symptoms & eCOA Checklist",
    description:
      "Please complete this survey detailing any adverse clinical signs experienced during the week.",
    items: {
      severity: {
        label: "Overall symptom severity this week",
        type: "choice_single",
        options: ["None", "Mild", "Moderate", "Severe"],
      },
      restricted_activity: {
        label: "Did symptoms restrict your daily activities?",
        type: "choice_single",
        options: ["Yes", "No"],
      },
      missed_doses: {
        label: "Did you miss any medication doses?",
        type: "choice_single",
        options: ["Yes", "No"],
      },
    },
  },
};

export const MOCK_NOTIFICATIONS = [
  {
    id: "notif_01",
    subject_id: DEMO_SUBJECT_ID,
    study_id: DEMO_STUDY_ID,
    assignment_id: "assign_01",
    message: "Reminder: Daily Health & Vital Diary is due shortly.",
    due_at: new Date(DEMO_BASE_EPOCH_MS + 4 * 3600 * 1000).toISOString(),
    channel: "IN_APP",
    is_read: false,
  },
  {
    id: "notif_02",
    subject_id: DEMO_SUBJECT_ID,
    study_id: DEMO_STUDY_ID,
    assignment_id: "assign_02",
    message:
      "ALERT: Weekly Symptoms & eCOA Checklist is OVERDUE! Please complete immediately.",
    due_at: new Date(DEMO_BASE_EPOCH_MS - 2 * 3600 * 1000).toISOString(),
    channel: "SMS",
    is_read: false,
  },
];
export const MOCK_APPROVED_CONTENT = {
  template_id: "template-icf",
  template_name: "Main Informed Consent Form",
  study_id: DEMO_STUDY_ID,
  site_id: DEMO_SITE_ID,
  sponsor_code: DEMO_SPONSOR_ID,
  protocol_version: "v1.0",
  language_code: "en",
  version_index: 1,
  clauses: [
    {
      clause_id: "clause-risk",
      title: "Risks & Side Effects",
      text: "There is a risk of mild temporary headache.",
      version_index: 1,
    },
    {
      clause_id: "clause-benefit",
      title: "Expected Benefits",
      text: "You will receive expert medical monitoring and potential symptom improvement.",
      version_index: 1,
    },
  ],
  workflow_steps: [
    { type: "comprehension_check" },
    { type: "signature_placeholder" },
  ],
};

export const MOCK_COMPREHENSION_CHECK = {
  template_id: "template-icf",
  version_index: 1,
  questions: [
    {
      id: "q_headache",
      text: "What is a potential temporary side effect?",
      options: ["Headache", "Nausea", "Vision Loss"],
    },
    {
      id: "q_withdraw",
      text: "Can you withdraw from the study at any time?",
      options: ["Yes", "No"],
    },
  ],
  expected_answers: {
    q_headache: "Headache",
    q_withdraw: "Yes",
  },
  threshold_policy: { min_correct: 2 },
};
