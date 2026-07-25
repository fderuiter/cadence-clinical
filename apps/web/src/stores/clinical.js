import { defineStore } from "pinia";
import { sha256 } from "../../index.js";
import { generateGatewaySignature, generateJwtHS256 } from "ui";

export const useClinicalStore = defineStore("clinical", {
  state: () => {
    let savedFormValues = null;
    let savedFormQueries = null;
    let savedLedgerBlocks = null;
    if (typeof window !== "undefined" && window.localStorage) {
      try {
        savedFormValues = JSON.parse(window.localStorage.getItem("formValues"));
        savedFormQueries = JSON.parse(
          window.localStorage.getItem("formQueries")
        );
        savedLedgerBlocks = JSON.parse(
          window.localStorage.getItem("ledgerBlocks")
        );
      } catch (e) {
        console.error("Failed to parse saved state from localStorage", e);
      }
    }

    return {
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
        arms: [
          { arm_id: "ARM-A", arm_name: "Arm A: Active 10mg daily" },
          { arm_id: "ARM-B", arm_name: "Arm B: Placebo Control" },
        ],
        epochs: [
          { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
          {
            epoch_id: "EP-TRT-A",
            epoch_name: "Treatment Phase",
            sequence: 2,
            arm_id: "ARM-A",
          },
          {
            epoch_id: "EP-TRT-B",
            epoch_name: "Treatment Phase",
            sequence: 2,
            arm_id: "ARM-B",
          },
        ],
        encounters: [
          {
            encounter_id: "V-SCR",
            encounter_name: "Day -7 to -1",
            epoch_id: "EP-SCR",
            sequence: 1,
          },
          {
            encounter_id: "V-TRT-A1",
            encounter_name: "Week 2",
            epoch_id: "EP-TRT-A",
            sequence: 2,
          },
          {
            encounter_id: "V-TRT-A2",
            encounter_name: "Week 4",
            epoch_id: "EP-TRT-A",
            sequence: 3,
          },
          {
            encounter_id: "V-TRT-B1",
            encounter_name: "Week 2",
            epoch_id: "EP-TRT-B",
            sequence: 4,
          },
        ],
        rows: [
          {
            activity_id: "ACT-DEM",
            activity_name: "Informed Consent & Demographics",
            cells: [
              {
                encounter_id: "V-SCR",
                is_applicable: true,
                details: "Mandatory",
              },
              { encounter_id: "V-TRT-A1", is_applicable: false },
              { encounter_id: "V-TRT-A2", is_applicable: false },
              { encounter_id: "V-TRT-B1", is_applicable: false },
            ],
          },
          {
            activity_id: "ACT-VS",
            activity_name: "Vital Signs (BP & Pulse)",
            cells: [
              { encounter_id: "V-SCR", is_applicable: true, details: "Day -7" },
              {
                encounter_id: "V-TRT-A1",
                is_applicable: true,
                details: "Within 10 mins",
              },
              {
                encounter_id: "V-TRT-A2",
                is_applicable: true,
                details: "Conditional",
              },
              {
                encounter_id: "V-TRT-B1",
                is_applicable: true,
                details: "Within 10 mins",
              },
            ],
          },
          {
            activity_id: "ACT-AE",
            activity_name: "Adverse Events Check",
            cells: [
              { encounter_id: "V-SCR", is_applicable: false },
              {
                encounter_id: "V-TRT-A1",
                is_applicable: true,
                details: "Continuous",
              },
              {
                encounter_id: "V-TRT-A2",
                is_applicable: true,
                details: "Continuous",
              },
              {
                encounter_id: "V-TRT-B1",
                is_applicable: true,
                details: "Optional",
              },
            ],
          },
          {
            activity_id: "ACT-MED",
            activity_name: "Study Medication Log",
            cells: [
              { encounter_id: "V-SCR", is_applicable: false },
              {
                encounter_id: "V-TRT-A1",
                is_applicable: true,
                details: "Daily entry",
              },
              {
                encounter_id: "V-TRT-A2",
                is_applicable: true,
                details: "Daily entry",
              },
              {
                encounter_id: "V-TRT-B1",
                is_applicable: true,
                details: "Daily entry",
              },
            ],
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
      formValues: savedFormValues || {
        brthdt: "1980-05-12",
        sex: "F",
        vssbp: "120",
        vsdpb: "80",
        pulse: "72",
      },
      formQueries: savedFormQueries || {},
      ledgerBlocks: savedLedgerBlocks || [],
      // Keycloak mock user info
      user: {
        username: "fderuiter",
        roles: ["Monitor", "Sponsor Admin"],
        authenticated: true,
      },
      syncInterval: null,
    };
  },
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
        synced: false,
      };

      this.ledgerBlocks.push(block);

      // Save persistent fields to localStorage
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem(
          "formValues",
          JSON.stringify(this.formValues)
        );
        window.localStorage.setItem(
          "formQueries",
          JSON.stringify(this.formQueries)
        );
        window.localStorage.setItem(
          "ledgerBlocks",
          JSON.stringify(this.ledgerBlocks)
        );
      }

      return block;
    },
    clearLedger() {
      this.ledgerBlocks = [];
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem(
          "ledgerBlocks",
          JSON.stringify(this.ledgerBlocks)
        );
      }
    },
    async startSyncTimer() {
      if (this.syncInterval) return;
      // Sync immediately on boot
      this.syncUnsyncedBlocks();
      this.syncInterval = setInterval(async () => {
        await this.syncUnsyncedBlocks();
      }, 10000); // Check for offline blocks every 10 seconds
    },
    async syncUnsyncedBlocks() {
      const unsynced = this.ledgerBlocks.filter(
        (b) =>
          !b.synced &&
          [
            "QUERY_CREATE",
            "QUERY_RESPOND",
            "QUERY_CLOSE",
            "QUERY_REOPEN",
          ].includes(b.action)
      );
      if (unsynced.length === 0) return;

      console.log(
        `Background sync: syncing ${unsynced.length} queued clinical query blocks.`
      );

      try {
        const userId = "fderuiter";
        const roles = "CRA,Data Manager"; // Grant sync role privilege map
        const timestamp = String(Date.now() / 1000);
        const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret

        // Generate Gateway signature for the HTTP headers
        const gatewaySignature = await generateGatewaySignature(
          userId,
          roles,
          timestamp,
          "2",
          "Background sync of clinical query ledger blocks",
          secret
        );

        // Generate X-Sig-Token (JWT) for signature gating
        const sigToken = await generateJwtHS256(
          {
            sub: userId,
            action: "/api/v1/execution/queries/sync",
            exp: Math.floor(Date.now() / 1000) + 300,
          },
          secret
        );

        // Send fetch request
        const response = await fetch(
          "http://localhost:8000/api/v1/execution/queries/sync",
          {
            method: "POST",
            headers: {
              "Content-Type": "application/json",
              "X-User-Id": userId,
              "X-User-Roles": roles,
              "X-Gateway-Timestamp": timestamp,
              "X-Gateway-Signature": gatewaySignature,
              "X-Signature-Version": "2",
              "X-Change-Reason":
                "Background sync of clinical query ledger blocks",
              "X-Sig-Token": sigToken,
            },
            body: JSON.stringify({ blocks: unsynced }),
          }
        );

        if (!response.ok) {
          throw new Error(`HTTP sync error! status: ${response.status}`);
        }

        // Successfully synced! Update local blocks
        unsynced.forEach((b) => {
          b.synced = true;
        });

        // Save updated blocks to localStorage
        if (typeof window !== "undefined" && window.localStorage) {
          window.localStorage.setItem(
            "ledgerBlocks",
            JSON.stringify(this.ledgerBlocks)
          );
        }
        console.log("Background sync: Successfully synchronized query blocks.");
      } catch (err) {
        console.warn("Background sync failed (retrying automatically):", err);
      }
    },
  },
});
