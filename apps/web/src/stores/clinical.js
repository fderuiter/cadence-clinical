import { defineStore } from "pinia";
import { buildLedgerBlock, debounce } from "ui";
import { useAuthStore } from "./auth.js";
import { soaClient } from "../api/soaClient.js";
import { executionService } from "../api/execution.js";
import { evaluateAST } from "../evaluator.js";

export const useClinicalStore = defineStore("clinical", {
  state: () => {
    let savedFormValues = null;
    let savedFormQueries = null;
    let savedLedgerBlocks = null;
    let savedUsdm = null;
    if (typeof window !== "undefined" && window.localStorage) {
      try {
        savedFormValues = JSON.parse(window.localStorage.getItem("formValues"));
        savedFormQueries = JSON.parse(
          window.localStorage.getItem("formQueries")
        );
        savedLedgerBlocks = JSON.parse(
          window.localStorage.getItem("ledgerBlocks")
        );
        savedUsdm = JSON.parse(window.localStorage.getItem("currentUsdm"));
      } catch (e) {
        console.error("Failed to parse saved state from localStorage", e);
      }
    }

    return {
      currentUsdm: savedUsdm || {
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
            plannedDate: "2026-08-01", // deid-ignore
            actualDate: "2026-08-01", // deid-ignore
            status: "ACHIEVED",
          },
          {
            id: "M2",
            type: "INITIATION_VISIT",
            plannedDate: "2026-08-10", // deid-ignore
            actualDate: "2026-08-12", // deid-ignore
            status: "ACHIEVED",
          },
          {
            id: "M3",
            type: "SITE_ACTIVATION",
            plannedDate: "2026-08-15", // deid-ignore
            actualDate: "",
            status: "PLANNED",
          },
          {
            id: "M4",
            type: "FIRST_SUBJECT_ENROLLED",
            plannedDate: "2026-09-01", // deid-ignore
            actualDate: "",
            status: "PLANNED",
          },
        ],
        visits: [
          {
            id: "V1",
            type: "SIV",
            scheduledDate: "2026-08-10", // deid-ignore
            actualDate: "2026-08-12", // deid-ignore
            status: "SIGNED_OFF",
            cra: "cra_fderuiter",
          },
          {
            id: "V2",
            type: "IMV",
            scheduledDate: "2026-08-25", // deid-ignore
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
          id: "concept_code",
          label: "Concept Code Lookup (NCI Thesaurus)",
          type: "concept_code",
          gridSpan: 12,
          cdash: "DM.CONCEPT_CODE",
          value: "",
        },
        {
          id: "brthdt",
          label: "Date of Birth (YYYY-MM-DD)",
          type: "text",
          gridSpan: 6,
          cdash: "DM.BRTHDT",
          value: "1980-05-12", // deid-ignore
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
        {
          id: "pulse_details",
          label: "Pulse Details (Tachycardia comment)",
          type: "text",
          gridSpan: 12,
          cdash: "VS.VSHR_DETAILS",
          value: "",
          relevant: {
            type: "comparison",
            operator: ">",
            operands: [
              { type: "field_ref", field_ref: { field_id: "pulse" } },
              { type: "constant", value: 100 },
            ],
          },
        },
        {
          id: "weight",
          label: "Weight (kg)",
          type: "text",
          gridSpan: 6,
          cdash: "VS.WT",
          value: "70",
          validation: {
            required: true,
            min: 10,
            max: 300,
          },
        },
        {
          id: "height",
          label: "Height (m)",
          type: "text",
          gridSpan: 6,
          cdash: "VS.HT",
          value: "1.75",
          validation: {
            required: true,
            min: 0.5,
            max: 3.0,
          },
          constraint: {
            condition: {
              type: "comparison",
              operator: ">",
              operands: [
                { type: "field_ref", field_ref: { field_id: "height" } },
                { type: "constant", value: 0 },
              ],
            },
            query_message: "Height must be strictly greater than zero.",
          },
        },
        {
          id: "bmi_status",
          label: "BMI Status Information",
          type: "text",
          gridSpan: 12,
          cdash: "VS.BMI_STATUS",
          value: "Normal",
          relevant: {
            type: "comparison",
            operator: ">",
            operands: [
              { type: "field_ref", field_ref: { field_id: "height" } },
              { type: "constant", value: 0 },
            ],
          },
        },
        {
          id: "concept_code",
          label: "NCI Thesaurus Concept Code",
          type: "concept_code",
          gridSpan: 12,
          cdash: "VS.CONCEPT_CODE",
          value: "",
          validation: {
            required: true,
          },
        },
      ],
      formValues: savedFormValues || {
        concept_code: "",
        brthdt: "1980-05-12", // deid-ignore
        sex: "F",
        vssbp: "120",
        vsdpb: "80",
        pulse: "72",
        pulse_details: "",
        weight: "70",
        height: "1.75",
        bmi_status: "Normal",
      },
      fieldVisibility: {},
      formQueries: savedFormQueries || {},
      ledgerBlocks: savedLedgerBlocks || [],
      syncInterval: null,

      // --- SoA State ---
      activeStudyVersionId: "v_draft_01",
      soaLoading: false,
      soaError: null,
    };
  },
  getters: {
    user: () => {
      const authStore = useAuthStore();
      if (authStore.isAuthenticated && !authStore.isDemoMode) {
        return {
          username: authStore.identity?.username || "unknown",
          roles: authStore.normalizedRoles,
          authenticated: true,
        };
      }
      return {
        username: "fderuiter",
        roles: ["Monitor", "Sponsor Admin"],
        authenticated: true,
      };
    },
  },
  actions: {
    async evaluateRules() {
      let changed = true;
      let passes = 0;
      while (changed && passes < 10) {
        changed = false;
        passes++;
        for (const field of this.ecrfFields) {
          const isRelevant = field.relevant
            ? evaluateAST(field.relevant, this.formValues) !== false
            : true;
          const wasVisible = this.fieldVisibility[field.id] !== false;
          if (
            isRelevant !== wasVisible ||
            this.fieldVisibility[field.id] === undefined
          ) {
            this.fieldVisibility[field.id] = isRelevant;
            changed = true;
          }
          if (!isRelevant) {
            const val = this.formValues[field.id];
            if (val !== undefined && val !== "" && val !== null) {
              this.formValues[field.id] = "";
              await this.addLedgerBlock(
                "FIELD_PURGE",
                {
                  fieldId: field.id,
                  label: field.label,
                  oldValue: val,
                  newValue: "",
                },
                "System-initiated purge of inactive child variable due to parent value mutation"
              );
              changed = true;
            }
          }
        }
      }
    },
    triggerValueChange() {
      if (!this.debouncedEvaluateRules) {
        this.debouncedEvaluateRules = debounce(async () => {
          await this.evaluateRules();
        }, 50);
      }
      this.debouncedEvaluateRules();
    },
    async addLedgerBlock(action, details, reason = "System Action") {
      const timestamp = new Date().toISOString();
      const index = this.ledgerBlocks.length;
      const prevHash =
        index === 0
          ? "0000000000000000000000000000000000000000000000000000000000000000" // deid-ignore
          : this.ledgerBlocks[index - 1].hash;

      const block = await buildLedgerBlock(
        index,
        timestamp,
        action,
        details,
        reason,
        prevHash
      );
      block.synced = false;

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
        window.localStorage.setItem(
          "currentUsdm",
          JSON.stringify(this.currentUsdm)
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
      }, 10000); // deid-ignore
    },
    async syncUnsyncedBlocks(sigToken = null) {
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
        const options = {
          changeReason: "Background sync of clinical query ledger blocks",
        };
        if (sigToken) {
          options.headers = {
            "X-Sig-Token": sigToken,
          };
        }

        await executionService.syncQueries(unsynced, options);

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
        throw err;
      }
    },

    // --- SoA Pinia Actions ---
    async fetchSoAProjection() {
      this.soaLoading = true;
      this.soaError = null;
      try {
        const data = await soaClient.getSoAProjection(
          this.currentUsdm.studyId,
          this.activeStudyVersionId,
          { userId: "fderuiter", roles: "STUDY_DESIGNER" }
        );
        // Map fetched Neo4j projection structure back to our local currentUsdm state
        this.currentUsdm.epochs = data.epochs || [];
        this.currentUsdm.encounters = data.encounters || [];
        this.currentUsdm.rows = data.rows || [];
        this.soaLoading = false;
      } catch (err) {
        this.soaError = err.message;
        this.soaLoading = false;
        console.warn(
          "Backend SoA endpoint failed, relying on local state:",
          err
        );
      }
    },

    async pushSoAMutation(type, id, properties, changeReason) {
      this.soaLoading = true;
      this.soaError = null;
      const opts = {
        userId: "fderuiter",
        roles: "STUDY_DESIGNER",
        changeReason,
      };
      try {
        if (type === "arms") {
          await soaClient.saveArm(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        } else if (type === "epochs") {
          await soaClient.saveEpoch(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        } else if (type === "visits") {
          await soaClient.saveVisit(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        } else if (type === "procedures") {
          await soaClient.saveProcedure(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        }
        await this.addLedgerBlock(
          `SOA_MUTATION_${type.toUpperCase()}`,
          { id, properties },
          changeReason
        );
        await this.fetchSoAProjection();
      } catch (err) {
        this.soaError = err.message;
        this.soaLoading = false;
        // Log mutation locally even on network failure for compliance
        await this.addLedgerBlock(
          `SOA_MUTATION_OFFLINE_${type.toUpperCase()}`,
          { id, properties, error: err.message },
          changeReason
        );
        throw err;
      }
    },

    async pushSoALink(linkType, payload, changeReason) {
      this.soaLoading = true;
      this.soaError = null;
      try {
        await soaClient.createLink(
          this.currentUsdm.studyId,
          this.activeStudyVersionId,
          linkType,
          payload,
          { userId: "fderuiter", roles: "STUDY_DESIGNER", changeReason }
        );
        await this.addLedgerBlock(
          `SOA_LINK_${linkType.toUpperCase()}`,
          payload,
          changeReason
        );
        await this.fetchSoAProjection();
      } catch (err) {
        this.soaError = err.message;
        this.soaLoading = false;
        await this.addLedgerBlock(
          `SOA_LINK_OFFLINE_${linkType.toUpperCase()}`,
          { payload, error: err.message },
          changeReason
        );
        throw err;
      }
    },
  },
});
