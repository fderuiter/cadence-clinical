import { defineStore } from "pinia";
import {
  executionApi,
  type ClinicalQuery,
  type FormSubmission,
  type Observation,
  type SubjectSummary,
} from "../api/executionApi";
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

export type QueryAction = "respond" | "close" | "reopen" | "cancel";

const QUERY_ACTIONS: Record<
  QueryAction,
  {
    run: (id: string, text: string) => Promise<ClinicalQuery>;
    done: string;
    failed: string;
  }
> = {
  respond: {
    run: (id, text) => executionApi.respondQuery(id, text),
    done: "Query answered",
    failed: "The query was not answered",
  },
  close: {
    run: (id, text) => executionApi.closeQuery(id, text),
    done: "Query closed",
    failed: "The query was not closed",
  },
  reopen: {
    run: (id, text) => executionApi.reopenQuery(id, text),
    done: "Query reopened",
    failed: "The query was not reopened",
  },
  cancel: {
    run: (id, text) => executionApi.cancelQuery(id, text),
    done: "Query cancelled",
    failed: "The query was not cancelled",
  },
};

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
    /** The open participant's record: what Execution holds for one participant, by subject code. */
    record: {
      subjectCode: "",
      observations: [] as Observation[],
      forms: [] as FormSubmission[],
      queries: [] as ClinicalQuery[],
      loading: false,
      error: false,
    },
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

    /** Loads a participant's observations, form submissions and queries together. */
    async loadRecord(subjectCode: string): Promise<boolean> {
      const studyId = this.studyCode;
      if (!studyId || !subjectCode) return false;
      const request = ++recordRequest;
      this.record = {
        subjectCode,
        observations: [],
        forms: [],
        queries: [],
        loading: true,
        error: false,
      };
      try {
        const [observations, forms, queries] = await Promise.all([
          executionApi.listObservations(studyId, subjectCode),
          executionApi.listFormSubmissions(studyId, subjectCode),
          executionApi.listQueries(studyId, subjectCode),
        ]);
        if (request !== recordRequest) return false;
        this.record.observations = observations;
        this.record.forms = forms;
        this.record.queries = queries;
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

    /** Records source data verification of one observation by the signed-in user. */
    async verifySdv(observation: Observation): Promise<boolean> {
      return this.updateObservation(
        () => executionApi.verifyObservationSdv(observation.id),
        `${observation.test_name} verified against source`,
        `${observation.test_name} was not verified`
      );
    },

    /** Flags one observation for re-verification with the reason given. */
    async flagSdv(observation: Observation, reason: string): Promise<boolean> {
      return this.updateObservation(
        () => executionApi.flagObservationSdv(observation.id, reason),
        `${observation.test_name} flagged for re-verification`,
        `${observation.test_name} was not flagged`
      );
    },

    /** Runs an observation command and shows Execution's result in place of the old observation. */
    async updateObservation(
      command: () => Promise<Observation>,
      success: string,
      failure: string
    ): Promise<boolean> {
      const toasts = useToastStore();
      try {
        const updated = await command();
        const list = this.record.observations;
        const index = list.findIndex((o) => o.id === updated.id);
        if (index >= 0) list.splice(index, 1, updated);
        toasts.push({ tone: "success", title: success, message: "" });
        return true;
      } catch (err) {
        toasts.apiError(failure, err);
        return false;
      }
    },

    /** Raises a query on an observation of the open participant. */
    async raiseQuery(
      observation: Observation,
      explanation: string
    ): Promise<boolean> {
      const toasts = useToastStore();
      try {
        const query = await executionApi.raiseQuery({
          studyId: this.studyCode,
          subjectCode: this.record.subjectCode,
          observation,
          explanation,
        });
        this.record.queries.push(query);
        toasts.push({
          tone: "success",
          title: `Query raised on ${observation.test_name}`,
          message: "",
        });
        return true;
      } catch (err) {
        toasts.apiError(
          `The query on ${observation.test_name} was not raised`,
          err
        );
        return false;
      }
    },

    /** Answers, closes, reopens or cancels a query, then shows Execution's result in its place. */
    async actOnQuery(
      query: ClinicalQuery,
      action: QueryAction,
      text: string
    ): Promise<boolean> {
      const toasts = useToastStore();
      const { run, done, failed } = QUERY_ACTIONS[action];
      try {
        const updated = await run(query.id, text);
        const list = this.record.queries;
        const index = list.findIndex((q) => q.id === updated.id);
        if (index >= 0) list.splice(index, 1, updated);
        toasts.push({ tone: "success", title: done, message: "" });
        return true;
      } catch (err) {
        toasts.apiError(failed, err);
        return false;
      }
    },

    /** Batch closes or cancels multiple queries, then updates Execution's results in place. */
    async batchActOnQueries(
      queryIds: string[],
      action: "close" | "cancel",
      reason: string
    ): Promise<boolean> {
      const toasts = useToastStore();
      const isClose = action === "close";
      const failedMsg = isClose
        ? "The queries were not closed"
        : "The queries were not cancelled";
      try {
        const response = isClose
          ? await executionApi.batchCloseQueries(queryIds, reason)
          : await executionApi.batchCancelQueries(queryIds, reason);

        const updatedQueries = response.queries || [];
        const count = response.count ?? updatedQueries.length;
        const list = this.record.queries;
        for (const updated of updatedQueries) {
          const index = list.findIndex((q) => q.id === updated.id);
          if (index >= 0) {
            list.splice(index, 1, updated);
          }
        }

        const title = isClose
          ? `${count} ${count === 1 ? "query" : "queries"} closed`
          : `${count} ${count === 1 ? "query" : "queries"} cancelled`;

        toasts.push({ tone: "success", title, message: "" });
        return true;
      } catch (err) {
        toasts.apiError(failedMsg, err);
        return false;
      }
    },
  },
});
