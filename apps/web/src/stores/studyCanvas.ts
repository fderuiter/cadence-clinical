import { defineStore } from "pinia";
import { executionApi, type SubjectSummary } from "../api/executionApi";
import {
  findKernelEnrollmentRef,
  findStudyScheduleOptions,
  type StudyScheduleOptions,
} from "../canvas/kernelStudy";
import { PARTICIPANT_STATES, participantStateLabel } from "../canvas/lifecycle";
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
let recordRequest = 0;
let scheduleRequest = 0;
const inFlightSchedules = new WeakMap<
  object,
  {
    studyCode: string;
    request: number;
    promise: Promise<StudyScheduleOptions | null>;
  }
>();

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
    /** Cached Schedule of Activities options per study code so form launchers avoid re-fetching. */
    scheduleOptionsByStudy: {} as Record<string, StudyScheduleOptions | null>,
    scheduleOptions: null as StudyScheduleOptions | null,
    scheduleLoading: false,
    scheduleError: null as string | null,
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
  }),
  getters: {
    hasMoreParticipants: (state) =>
      state.participants.length < state.participantsTotal,
  },
  actions: {
    /** Switches the canvas to a study, clearing everything read for the previous one while keeping cached schedules per study. */
    openStudy(studyCode: string) {
      if (studyCode === this.studyCode) return;
      const cachedSchedules = { ...this.scheduleOptionsByStudy };
      participantsRequest++;
      pulseRequest++;
      recordRequest++;
      scheduleRequest++;
      inFlightSchedules.delete(this);
      this.$reset();
      this.studyCode = studyCode;
      this.scheduleOptionsByStudy = cachedSchedules;
      if (
        studyCode &&
        Object.prototype.hasOwnProperty.call(cachedSchedules, studyCode)
      ) {
        this.scheduleOptions = cachedSchedules[studyCode];
      }
    },

    /** Loads and caches structured Schedule of Activities options for `studyCode` (defaults to the active study). */
    async loadScheduleOptions(
      studyCode?: string,
      { force = false } = {}
    ): Promise<StudyScheduleOptions | null> {
      const target = (studyCode ?? this.studyCode).trim();
      if (!target) {
        this.scheduleOptions = null;
        this.scheduleLoading = false;
        this.scheduleError = null;
        return null;
      }
      if (
        !force &&
        Object.prototype.hasOwnProperty.call(
          this.scheduleOptionsByStudy,
          target
        )
      ) {
        this.scheduleOptions = this.scheduleOptionsByStudy[target];
        this.scheduleLoading = false;
        this.scheduleError = null;
        return this.scheduleOptions;
      }
      const existing = inFlightSchedules.get(this);
      if (!force && existing && existing.studyCode === target) {
        return existing.promise;
      }
      const request = ++scheduleRequest;
      this.scheduleLoading = true;
      this.scheduleError = null;
      const promise = (async () => {
        try {
          const loaded = await findStudyScheduleOptions(target);
          if (request !== scheduleRequest) return null;
          this.scheduleOptionsByStudy[target] = loaded;
          this.scheduleOptions = loaded;
          return loaded;
        } catch (err) {
          if (request !== scheduleRequest) return null;
          this.scheduleOptions = null;
          this.scheduleError = err instanceof Error ? err.message : String(err);
          return null;
        } finally {
          if (inFlightSchedules.get(this)?.request === request) {
            inFlightSchedules.delete(this);
          }
          if (request === scheduleRequest) {
            this.scheduleLoading = false;
          }
        }
      })();
      inFlightSchedules.set(this, { studyCode: target, request, promise });
      return promise;
    },

    /** Retries loading Schedule of Activities options after a failure. */
    async retryScheduleOptions(
      studyCode?: string
    ): Promise<StudyScheduleOptions | null> {
      return this.loadScheduleOptions(studyCode ?? this.studyCode, {
        force: true,
      });
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
      return true;
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
