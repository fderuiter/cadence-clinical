import { defineStore } from "pinia";
import { sha256 } from "../../index.js";

export const useClinicalStore = defineStore("clinical", {
  state: () => ({
    currentUsdm: {
      studyId: "STUDY-USDM-001",
      studyTitle: "Phase II Trial of Cadence-001 in Essential Hypertension",
      objectives: [
        {
          id: "OBJ-001",
          type: "Primary",
          description:
            "Evaluate the reduction of mean sitting Systolic Blood Pressure (SBP) from baseline.",
        },
        {
          id: "OBJ-002",
          type: "Secondary",
          description:
            "Evaluate safety and tolerability of daily oral administration of Cadence-001.",
        },
      ],
      visits: [
        "Screening",
        "Baseline (Day 1)",
        "Week 2",
        "Week 4",
        "End of Study",
      ],
      forms: [
        {
          name: "Demographics",
          statuses: ["Complete", "N/A", "N/A", "N/A", "N/A"],
        },
        {
          name: "Vital Signs (BP & Pulse)",
          statuses: ["Complete", "Pending", "Pending", "Pending", "Pending"],
        },
        {
          name: "Adverse Events Check",
          statuses: ["N/A", "Complete", "Pending", "Pending", "Complete"],
        },
        {
          name: "Study Medication Log",
          statuses: ["N/A", "Complete", "Complete", "Complete", "Complete"],
        },
      ],
    },
    currentCtmsData: {
      milestones: [
        {
          id: "M1",
          type: "SITE_SELECTION",
          plannedDate: "2026-08-01",
          actualDate: "2026-08-01",
          status: "ACHIEVED",
        },
        {
          id: "M2",
          type: "INITIATION_VISIT",
          plannedDate: "2026-08-10",
          actualDate: "2026-08-12",
          status: "ACHIEVED",
        },
        {
          id: "M3",
          type: "SITE_ACTIVATION",
          plannedDate: "2026-08-15",
          actualDate: "",
          status: "PLANNED",
        },
        {
          id: "M4",
          type: "FIRST_SUBJECT_ENROLLED",
          plannedDate: "2026-09-01",
          actualDate: "",
          status: "PLANNED",
        },
      ],
      visits: [
        {
          id: "V1",
          type: "SIV",
          scheduledDate: "2026-08-10",
          actualDate: "2026-08-12",
          status: "SIGNED_OFF",
          cra: "cra_fderuiter",
        },
        {
          id: "V2",
          type: "IMV",
          scheduledDate: "2026-08-25",
          actualDate: "",
          status: "SCHEDULED",
          cra: "cra_fderuiter",
        },
      ],
      allocations: [
        {
          cra: "cra_fderuiter",
          activeAllocations: 3,
          sites: ["Site-01", "Site-02", "Site-09"],
          studies: ["STUDY-01", "STUDY-02"],
        },
        {
          cra: "cra_alice",
          activeAllocations: 1,
          sites: ["Site-03"],
          studies: ["STUDY-01"],
        },
      ],
      recruitment: [
        { siteId: "Site-01", screened: 15, enrolled: 8, target: 20 },
        { siteId: "Site-02", screened: 8, enrolled: 4, target: 15 },
      ],
    },
    ecrfFields: [
      {
        id: "brthdt",
        label: "Date of Birth (YYYY-MM-DD)",
        type: "text",
        gridSpan: 6,
        cdash: "DM.BRTHDT",
        value: "1980-05-12",
        validation: {
          required: true,
          pattern: "^\\d{4}-\\d{2}-\\d{2}$",
          message: "Date must be in YYYY-MM-DD format",
        },
      },
      {
        id: "sex",
        label: "Sex at Birth",
        type: "radio",
        gridSpan: 6,
        options: [
          { value: "M", label: "Male" },
          { value: "F", label: "Female" },
          { value: "U", label: "Unknown" },
        ],
        cdash: "DM.SEX",
        value: "F",
      },
      {
        id: "vssbp",
        label: "Systolic Blood Pressure (mmHg)",
        type: "text",
        gridSpan: 4,
        cdash: "VS.VSSBP",
        value: "120",
        validation: {
          required: true,
          min: 50,
          max: 250,
          message: "Systolic Blood Pressure must be between 50 and 250 mmHg",
        },
      },
      {
        id: "vsdpb",
        label: "Diastolic Blood Pressure (mmHg)",
        type: "text",
        gridSpan: 4,
        cdash: "VS.VSDPB",
        value: "80",
        validation: {
          required: true,
          min: 30,
          max: 150,
          message: "Diastolic Blood Pressure must be between 30 and 150 mmHg",
        },
      },
      {
        id: "pulse",
        label: "Pulse Rate (bpm)",
        type: "text",
        gridSpan: 4,
        cdash: "VS.VSHR",
        value: "72",
        validation: {
          required: true,
          min: 30,
          max: 200,
          message: "Pulse Rate must be between 30 and 200 bpm",
        },
      },
    ],
    formValues: {
      brthdt: "1980-05-12",
      sex: "F",
      vssbp: "120",
      vsdpb: "80",
      pulse: "72",
    },
    formQueries: {},
    ledgerBlocks: [],
    // Keycloak mock user info
    user: {
      username: "fderuiter",
      roles: ["Monitor", "Sponsor Admin"],
      authenticated: true,
    },
  }),
  actions: {
    async addLedgerBlock(action, details, reason = "System Action") {
      const timestamp = new Date().toISOString();
      const index = this.ledgerBlocks.length;
      const prevHash =
        index === 0
          ? "0000000000000000000000000000000000000000000000000000000000000000"
          : this.ledgerBlocks[index - 1].hash;

      const payloadString = `${index}|${timestamp}|${action}|${JSON.stringify(details)}|${reason}|${prevHash}`;
      const hash = await sha256(payloadString);

      const block = {
        index,
        timestamp,
        action,
        details,
        reason,
        prevHash,
        hash,
      };

      this.ledgerBlocks.push(block);
      return block;
    },
    clearLedger() {
      this.ledgerBlocks = [];
    },
  },
});
