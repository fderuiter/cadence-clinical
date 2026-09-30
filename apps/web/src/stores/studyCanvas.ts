import { defineStore } from "pinia";
import { executionApi, type SubjectSummary } from "../api/executionApi";
import { PARTICIPANT_STATES } from "../canvas/lifecycle";
import { useToastStore } from "./toasts.js";

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
  },
});
