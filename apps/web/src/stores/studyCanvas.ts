import { defineStore } from "pinia";
import {
  executionApi,
  type RandomizationResult,
  type SubjectConsent,
  type SubjectDetail,
  type SubjectSummary,
} from "../api/executionApi";
import { findKernelEnrollmentRef } from "../canvas/kernelStudy";
import { PARTICIPANT_STATES, participantStateLabel } from "../canvas/lifecycle";
import { useToastStore } from "./toasts.js";

/** Blind-safe allocation metadata kept in memory only (never stores plaintext arms). */
export interface BlindSafeAllocationSummary {
  randomizationId: string;
  schemeVersion: number;
  stratumKey: string;
  kitNumber: string | null;
  randomizedAt: string;
}

export interface RecordParticipantConsentInput {
  versionTag: string;
  versionIndex: number;
  signedDate: string;
  isPaperOverride?: boolean;
  requiresReconsent?: boolean;
  reason: string;
}

/** Participants shown per page in the Participants section. */
export const PARTICIPANT_PAGE_SIZE = 50;

export interface ParticipantFilter {
  status?: string;
  siteId?: string;
}

export interface PulseCount {
  code: string;
  label: string;
  count: number;
}

// Execution may send int32 values as strings (its OpenAPI allows both).
const toCount = (value: number | string): number => Number(value) || 0;

let participantsRequest = 0;
let pulseRequest = 0;
let recordRequest = 0;
let detailRequest = 0;

const POST_RANDOMIZATION_STATES = new Set([
  "RANDOMIZED",
  "ACTIVE",
  "UNBLINDED",
  "COMPLETED",
]);

export function isPostRandomizationStatus(status: string): boolean {
  return POST_RANDOMIZATION_STATES.has(status);
}

/**
 * Data behind the Clinical Canvas for one study, read from Execution (#4833). The canvas shows only what
 * this store has read; anything a service does not serve yet is absent (docs/ux/CLINICAL_CANVAS.md §6.1).
 * A response for a study or filter the user has since left is dropped rather than shown.
 */
export const useStudyCanvasStore = defineStore("studyCanvas", {
  state: () => ({
    studyCode: "",
    participants: [] as SubjectSummary[],
    /** Every participant read for this study by id, so a selection survives changing the list's filter. */
    knownParticipants: {} as Record<string, SubjectSummary>,
    participantsTotal: 0,
    participantsFilter: {} as ParticipantFilter,
    participantsLoading: false,
    participantsError: false,
    pulse: null as { total: number; byState: PulseCount[] } | null,
    pulseLoading: false,
    pulseError: false,
    /**
     * The open participant's record: the participant's kernel enrollment, by subject code. Its eCRF queries are read
     * by enrollment id (#5461); `enrollmentId` is null when the kernel has no single enrollment for the participant.
     */
    record: {
      subjectCode: "",
      enrollmentId: null as string | null,
      loading: false,
      error: false,
    },
    /** Blind-aware subject detail read from GET /api/v1/execution/subjects/{subjectCode}, keyed by subject_id. */
    subjectDetails: {} as Record<string, SubjectDetail>,
    subjectDetailLoading: false,
    subjectDetailError: false,
    /** Blind-safe randomization receipts (kit number, stratum, timestamp; never arm) keyed by subject_id. */
    allocations: {} as Record<string, BlindSafeAllocationSummary>,
    /** Latest consent receipts recorded in this session, keyed by subject_id. */
    recordedConsents: {} as Record<string, SubjectConsent>,
  }),
  getters: {
    hasMoreParticipants: (state) =>
      state.participants.length < state.participantsTotal,
  },
  actions: {
    /** Switches the canvas to a study, clearing everything read for the previous one. */
    openStudy(studyCode: string) {
      if (studyCode === this.studyCode) return;
      participantsRequest++;
      pulseRequest++;
      recordRequest++;
      detailRequest++;
      this.$reset();
      this.studyCode = studyCode;
    },

    /** Loads the first page of participants for a filter, or the next page with `append`. */
    async loadParticipants(
      filter: ParticipantFilter = {},
      { append = false } = {}
    ): Promise<boolean> {
      const studyId = this.studyCode;
      if (!studyId) return false;
      const request = ++participantsRequest;
      const offset = append ? this.participants.length : 0;
      if (!append) {
        this.participants = [];
        this.participantsTotal = 0;
      }
      this.participantsFilter = { ...filter };
      this.participantsLoading = true;
      this.participantsError = false;
      try {
        const page = await executionApi.listSubjects({
          studyId,
          status: filter.status,
          siteId: filter.siteId,
          limit: PARTICIPANT_PAGE_SIZE,
          offset,
        });
        if (request !== participantsRequest) return false;
        this.participants = append
          ? [...this.participants, ...page.items]
          : page.items;
        this.participantsTotal = toCount(page.total);
        for (const participant of page.items) {
          this.knownParticipants[participant.id] = participant;
        }
        return true;
      } catch (err) {
        if (request !== participantsRequest) return false;
        this.participantsError = true;
        useToastStore().apiError("Couldn't load the study's participants", err);
        return false;
      } finally {
        if (request === participantsRequest) this.participantsLoading = false;
      }
    },

    /**
     * Counts the study's participants in each state. Each count is Execution's own total for that state, so
     * selecting it lists exactly the participants it counts (UX-7).
     */
    async loadPulse(): Promise<boolean> {
      const studyId = this.studyCode;
      if (!studyId) return false;
      const request = ++pulseRequest;
      this.pulseLoading = true;
      this.pulseError = false;
      try {
        const [all, ...perState] = await Promise.all([
          executionApi.listSubjects({ studyId, limit: 1 }),
          ...PARTICIPANT_STATES.map((state) =>
            executionApi.listSubjects({ studyId, status: state.code, limit: 1 })
          ),
        ]);
        if (request !== pulseRequest) return false;
        this.pulse = {
          total: toCount(all.total),
          byState: PARTICIPANT_STATES.map((state, i) => ({
            code: state.code,
            label: state.label,
            count: toCount(perState[i].total),
          })),
        };
        return true;
      } catch (err) {
        if (request !== pulseRequest) return false;
        this.pulse = null;
        this.pulseError = true;
        useToastStore().apiError(
          "Couldn't load the study's participant counts",
          err
        );
        return false;
      } finally {
        if (request === pulseRequest) this.pulseLoading = false;
      }
    },

    /**
     * Adds a participant to the study in screening, then refreshes the list and the counts. `participant` is
     * the new participant as the list reports it, or null when the list can't place it yet.
     */
    async addParticipant(
      subjectCode: string,
      siteId: string,
      reason: string
    ): Promise<{ added: boolean; participant: SubjectSummary | null }> {
      const studyId = this.studyCode;
      const toasts = useToastStore();
      if (!studyId) return { added: false, participant: null };
      try {
        await executionApi.addSubject({
          studyId,
          subjectCode,
          siteId,
          reason,
        });
      } catch (err) {
        toasts.apiError(`${subjectCode} was not added`, err);
        return { added: false, participant: null };
      }
      toasts.push({
        tone: "success",
        title: `${subjectCode} added in screening`,
        message: "",
      });
      const [participant] = await Promise.all([
        this.findNewestParticipant(studyId, subjectCode),
        // Added from the participant list, which may have been empty until now.
        this.refreshAfterChange({ list: true }),
      ]);
      return { added: true, participant };
    },

    /**
     * The participant just added, read back from the list: Execution lists in enrollment order, so it is the
     * last one unless someone else added a participant in between, in which case this returns null.
     */
    async findNewestParticipant(
      studyId: string,
      subjectCode: string
    ): Promise<SubjectSummary | null> {
      try {
        const { total } = await executionApi.listSubjects({
          studyId,
          limit: 1,
        });
        const last = toCount(total) - 1;
        if (last < 0) return null;
        const page = await executionApi.listSubjects({
          studyId,
          limit: 1,
          offset: last,
        });
        const newest = page.items[0];
        if (studyId !== this.studyCode || newest?.subject_id !== subjectCode) {
          return null;
        }
        this.knownParticipants[newest.id] = newest;
        return newest;
      } catch {
        return null;
      }
    },

    /**
     * Moves a participant to another state with the reason given. On success the participant shows its new
     * state everywhere the canvas lists it, and the list and counts are read again.
     */
    async changeParticipantState(
      participant: SubjectSummary,
      state: string,
      reason: string
    ): Promise<boolean> {
      const toasts = useToastStore();
      const label = participantStateLabel(state);
      try {
        const updated = await executionApi.changeSubjectState(
          participant.id,
          state,
          reason
        );
        const changed = { ...participant, status: updated.status };
        this.knownParticipants[participant.id] = changed;
        const index = this.participants.findIndex(
          (p) => p.id === participant.id
        );
        if (index >= 0) this.participants.splice(index, 1, changed);
        toasts.push({
          tone: "success",
          title: `${participant.subject_id} is now ${label.toLowerCase()}`,
          message: "",
        });
      } catch (err) {
        toasts.apiError(
          `${participant.subject_id} was not moved to ${label.toLowerCase()}`,
          err
        );
        return false;
      }
      await this.refreshAfterChange();
      if (state === "UNBLINDED" || isPostRandomizationStatus(state)) {
        await this.loadSubjectDetail({ ...participant, status: state });
      }
      return true;
    },

    /**
     * Records informed consent for a participant with an explicit signed date
     * (POST /api/v1/execution/subjects/{subjectId}/consent).
     */
    async recordParticipantConsent(
      participant: SubjectSummary,
      input: RecordParticipantConsentInput
    ): Promise<SubjectConsent | null> {
      const studyId = this.studyCode || participant.study_id;
      const toasts = useToastStore();
      if (!studyId) return null;
      const signedDate = input.signedDate.trim();
      if (!signedDate) {
        toasts.push({
          tone: "error",
          title: "Signed consent date required",
          message:
            "Enter the date the participant signed the informed consent form.",
        });
        return null;
      }
      const isoSignedDate = signedDate.includes("T")
        ? signedDate
        : `${signedDate}T00:00:00Z`;
      try {
        const consent = await executionApi.recordSubjectConsent(
          participant.id,
          {
            protocol_version: {
              study_id: studyId,
              version_tag: input.versionTag.trim() || "1.0",
              version_index: Number(input.versionIndex) || 1,
            },
            icf_signed: true,
            icf_signed_date: isoSignedDate,
            requires_reconsent: Boolean(input.requiresReconsent),
            is_paper_override: Boolean(input.isPaperOverride),
          },
          input.reason.trim() || "Informed consent recorded"
        );
        this.recordedConsents[participant.subject_id] = consent;
        toasts.push({
          tone: "success",
          title: `Consent recorded for ${participant.subject_id}`,
          message: `Protocol version ${consent.version_tag}`,
        });
        return consent;
      } catch (err) {
        toasts.apiError(
          `Consent was not recorded for ${participant.subject_id}`,
          err
        );
        return null;
      }
    },

    /**
     * Randomizes an enrolled participant using a single-use X-Sig-Token (`execution.subject.randomize`).
     * Updates the participant's state to RANDOMIZED and stores only blind-safe allocation details in memory.
     */
    async randomizeParticipant(
      participant: SubjectSummary,
      options: {
        stratificationFactors: Record<string, string>;
        reason: string;
        sigToken: string;
      }
    ): Promise<RandomizationResult | null> {
      const toasts = useToastStore();
      try {
        const result = await executionApi.randomizeSubject(participant.id, {
          stratificationFactors: options.stratificationFactors,
          reason: options.reason,
          sigToken: options.sigToken,
        });
        const changed: SubjectSummary = {
          ...participant,
          status: result.status || "RANDOMIZED",
        };
        this.knownParticipants[participant.id] = changed;
        const index = this.participants.findIndex(
          (p) => p.id === participant.id
        );
        if (index >= 0) this.participants.splice(index, 1, changed);
        this.allocations[participant.subject_id] = {
          randomizationId: result.randomization_id,
          schemeVersion: Number(result.scheme_version) || 1,
          stratumKey: result.stratum_key,
          kitNumber: result.kit_number ?? null,
          randomizedAt: result.randomized_at,
        };
        toasts.push({
          tone: "success",
          title: `${participant.subject_id} randomized`,
          message: result.kit_number
            ? `Randomized, arm masked · Kit ${result.kit_number}`
            : "Randomized, arm masked",
        });
        await Promise.all([
          this.refreshAfterChange(),
          this.loadSubjectDetail(changed),
        ]);
        return result;
      } catch (err) {
        toasts.apiError(`${participant.subject_id} was not randomized`, err);
        return null;
      }
    },

    /**
     * Loads blind-aware subject detail (GET /api/v1/execution/subjects/{subjectCode}).
     * Blinded callers receive null treatment_group; unblinded callers receive treatment_group and investigational_product_id.
     */
    async loadSubjectDetail(
      participant: Pick<SubjectSummary, "subject_id" | "status">
    ): Promise<SubjectDetail | null> {
      const studyId = this.studyCode;
      const subjectCode = participant.subject_id;
      if (!studyId || !subjectCode) return null;
      if (
        !isPostRandomizationStatus(participant.status) &&
        !this.allocations[subjectCode]
      ) {
        return null;
      }
      if (typeof executionApi.getSubjectDetail !== "function") {
        return null;
      }
      const request = ++detailRequest;
      this.subjectDetailLoading = true;
      this.subjectDetailError = false;
      try {
        const detail = await executionApi.getSubjectDetail(subjectCode);
        if (studyId !== this.studyCode || request !== detailRequest) {
          return null;
        }
        this.subjectDetails[subjectCode] = detail;
        return detail;
      } catch {
        if (request === detailRequest) {
          this.subjectDetailError = true;
        }
        return null;
      } finally {
        if (request === detailRequest) {
          this.subjectDetailLoading = false;
        }
      }
    },

    /** Reads the participant list and the counts again, where the canvas has already read them. */
    async refreshAfterChange({ list = false } = {}): Promise<void> {
      const listed =
        list || this.participants.length > 0 || this.participantsTotal > 0;
      await Promise.all([
        listed ? this.loadParticipants(this.participantsFilter) : null,
        this.pulse ? this.loadPulse() : null,
      ]);
    },

    /**
     * Finds a participant's kernel enrollment, whose eCRF queries the record shows. Values and forms are kernel form
     * instances, read on the eCRF screen (ADR-2622).
     */
    async loadRecord(participant: SubjectSummary): Promise<boolean> {
      const studyId = this.studyCode;
      const subjectCode = participant.subject_id;
      if (!studyId || !subjectCode) return false;
      const request = ++recordRequest;
      this.record = {
        subjectCode,
        enrollmentId: null,
        loading: true,
        error: false,
      };
      try {
        const enrollment = await findKernelEnrollmentRef(
          studyId,
          participant.site_id ?? null,
          subjectCode
        );
        if (request !== recordRequest) return false;
        this.record.enrollmentId = enrollment?.id ?? null;
        return true;
      } catch (err) {
        if (request !== recordRequest) return false;
        this.record.error = true;
        useToastStore().apiError(`Couldn't load ${subjectCode}'s data`, err);
        return false;
      } finally {
        if (request === recordRequest) this.record.loading = false;
      }
    },
  },
});
