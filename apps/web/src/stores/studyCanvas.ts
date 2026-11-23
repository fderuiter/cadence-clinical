import { defineStore, getActivePinia } from "pinia";
import { registerSessionCacheClearHandler } from "../auth/sessionCoordinator.js";
import {
  executionApi,
  type SubjectDetail,
  type SubjectList,
  type SubjectSummary,
} from "../api/executionApi";
import {
  clearKernelStudyCache,
  findKernelEnrollmentRef,
  findStudyScheduleOptions,
  resetKernelStudySession,
  type StudyScheduleOptions,
} from "../canvas/kernelStudy";
import { PARTICIPANT_STATES, participantStateLabel } from "../canvas/lifecycle";
import {
  clearNodeResolutionCache,
  resetNodeResolutionSession,
  seedNodeSummary,
} from "../canvas/nodeResolver";
import { getSyncedStudyScope, syncActiveStudyScope } from "./studyScopeBridge";
import { useToastStore } from "./toasts.js";

function seedParticipantNode(participant: SubjectSummary): void {
  seedNodeSummary(`participant:${participant.id}`, {
    kind: "participant",
    code: participant.subject_id,
    title: participant.subject_id,
    status: participant.status,
  });
}

/** Participants shown per page in the Participants section. */
export const PARTICIPANT_PAGE_SIZE = 50;

/** Maximum page size accepted by Execution's ListSubjects endpoint (SubjectApplicationService.MaxListLimit). */
export const PARTICIPANT_LOOKUP_PAGE_SIZE = 200;

export interface ParticipantFilter {
  status?: string;
  siteId?: string;
  q?: string;
}

export interface PulseCount {
  code: string;
  label: string;
  count: number;
}

// Execution may send int32 values as strings (its OpenAPI allows both).
const toCount = (value: number | string): number => Number(value) || 0;

const sortByEnrollment = (
  known: Record<string, SubjectSummary>
): SubjectSummary[] =>
  Object.values(known).sort(
    (a, b) => toCount(a.enrollment_index) - toCount(b.enrollment_index)
  );

function isNotFoundError(err: unknown): boolean {
  return Boolean(
    err &&
    typeof err === "object" &&
    "status" in err &&
    Number((err as { status?: unknown }).status) === 404
  );
}

function detailToSummary(
  detail: SubjectDetail | null | undefined
): SubjectSummary | null {
  if (
    !detail ||
    typeof detail !== "object" ||
    typeof detail.id !== "string" ||
    !detail.id
  ) {
    return null;
  }
  return {
    id: detail.id,
    subject_id: detail.subject_id,
    study_id: detail.study_id,
    site_id: detail.site_id ?? null,
    status: detail.status,
    active_protocol_version: detail.active_protocol_version ?? "",
    enrollment_index: detail.enrollment_index ?? 0,
  };
}

let participantsRequest = 0;
let pulseRequest = 0;
let recordRequest = 0;
let scheduleRequest = 0;
let lookupEpoch = 0;
const inFlightPages = new Map<string, Promise<SubjectList>>();
const inFlightById = new Map<string, Promise<SubjectSummary | null>>();
const inFlightByCode = new Map<string, Promise<SubjectSummary | null>>();
const inFlightSchedules = new WeakMap<
  object,
  {
    studyCode: string;
    request: number;
    promise: Promise<StudyScheduleOptions | null>;
  }
>();
export interface ExplainRequestOptions {
  fromShortcut?: boolean;
  targetNodeRef?: string | null;
}
export type ExplainCoordinatorHandler = (
  options?: ExplainRequestOptions
) => boolean;
const explainCoordinators = new WeakMap<
  object,
  Set<ExplainCoordinatorHandler>
>();
let palettePromise: Promise<boolean> | null = null;
let palettePromiseKey = "";

export function isEditableShortcutTarget(target: EventTarget | null): boolean {
  if (!target || typeof target !== "object") return false;
  const el = target as HTMLElement;
  if (el.isContentEditable) return true;
  const tagName =
    typeof el.tagName === "string" ? el.tagName.toUpperCase() : "";
  if (tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT") {
    return true;
  }
  if (
    typeof el.closest === "function" &&
    el.closest('[contenteditable="true"], [contenteditable=""]')
  ) {
    return true;
  }
  return false;
}

function fetchStudyPage(
  studyId: string,
  epoch: number,
  offset: number
): Promise<SubjectList> {
  const key = `${epoch}:${studyId}:${offset}`;
  const existing = inFlightPages.get(key);
  if (existing) return existing;
  const req = executionApi
    .listSubjects({
      studyId,
      limit: PARTICIPANT_LOOKUP_PAGE_SIZE,
      offset,
    })
    .finally(() => {
      inFlightPages.delete(key);
    });
  inFlightPages.set(key, req);
  return req;
}

/**
 * Data behind the Clinical Canvas for one study, read from Execution (#4833). The canvas shows only what
 * this store has read; anything a service does not serve yet is absent (docs/ux/CLINICAL_CANVAS.md §6.1).
 * A response for a study or filter the user has since left is dropped rather than shown.
 */
export const useStudyCanvasStore = defineStore("studyCanvas", {
  state: () => ({
    studyCode: getSyncedStudyScope(),
    participants: [] as SubjectSummary[],
    /** Study participants available to the ⌘K command palette across all views. */
    paletteParticipants: [] as SubjectSummary[],
    paletteTotal: 0,
    paletteLoaded: false,
    paletteLoading: false,
    paletteError: null as string | null,
    unfilteredLoadedCount: 0,
    /** Every participant read for this study by id, so a selection survives changing the list's filter. */
    knownParticipants: {} as Record<string, SubjectSummary>,
    /** Participant UUIDs currently being hydrated for cold links or inspector selections. */
    participantLookups: {} as Record<string, boolean>,
    /** Participant subject codes currently being resolved for cold `?participant=<code>` links. */
    participantCodeLookups: {} as Record<string, boolean>,
    /** Participant UUIDs confirmed absent or inaccessible for the active study. */
    failedParticipantIds: {} as Record<string, boolean>,
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
    /** Switches the canvas to a study, clearing everything read for the previous one. */
    openStudy(studyCode: string) {
      if (studyCode === this.studyCode) return;
      participantsRequest++;
      pulseRequest++;
      recordRequest++;
      scheduleRequest++;
      inFlightSchedules.delete(this);
      lookupEpoch++;
      inFlightPages.clear();
      inFlightById.clear();
      inFlightByCode.clear();
      palettePromise = null;
      palettePromiseKey = "";
      this.$reset();
      this.studyCode = studyCode;
      if (typeof clearKernelStudyCache === "function") {
        clearKernelStudyCache();
      }
      clearNodeResolutionCache();
      resetNodeResolutionSession(studyCode);
      if (typeof resetKernelStudySession === "function") {
        resetKernelStudySession(studyCode);
      }
      if (studyCode.trim()) {
        syncActiveStudyScope(studyCode);
      }
    },

    /** Clears per-study cached schedules and kernel lookup caches across auth, tenant, account, or persona transitions. */
    clearSessionCaches() {
      scheduleRequest++;
      inFlightSchedules.delete(this);
      this.scheduleOptionsByStudy = {};
      this.scheduleOptions = null;
      this.scheduleLoading = false;
      this.scheduleError = null;
      if (typeof clearKernelStudyCache === "function") {
        clearKernelStudyCache();
      }
      clearNodeResolutionCache();
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
        const cached = this.scheduleOptionsByStudy[target];
        if (!this.studyCode || this.studyCode === target) {
          this.scheduleOptions = cached;
          this.scheduleLoading = false;
          this.scheduleError = null;
        }
        return cached;
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
          if (loaded) {
            this.scheduleOptionsByStudy[target] = loaded;
          }
          if (
            request !== scheduleRequest ||
            (this.studyCode && this.studyCode !== target)
          ) {
            return null;
          }
          this.scheduleOptions = loaded;
          return loaded;
        } catch (err) {
          if (
            request !== scheduleRequest ||
            (this.studyCode && this.studyCode !== target)
          ) {
            return null;
          }
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

    /** Records participants for the active study and keeps `paletteParticipants` ordered. */
    rememberParticipants(items: SubjectSummary[]) {
      const studyId = this.studyCode;
      let changed = false;
      for (const participant of items) {
        if (participant.study_id && participant.study_id !== studyId) continue;
        this.knownParticipants[participant.id] = participant;
        seedParticipantNode(participant);
        delete this.failedParticipantIds[participant.id];
        changed = true;
      }
      if (changed) {
        this.paletteParticipants = sortByEnrollment(this.knownParticipants);
      }
    },

    /** Loads up to 200 study participants for the ⌘K command palette, once per open study. */
    async ensurePaletteParticipants(studyCode?: string): Promise<boolean> {
      const studyId = (studyCode ?? this.studyCode).trim();
      if (!studyId) return false;
      if (studyId !== this.studyCode) {
        this.openStudy(studyId);
      }
      if (this.paletteLoaded) return true;

      const epoch = lookupEpoch;
      const key = `${epoch}:${studyId}`;
      if (palettePromise && palettePromiseKey === key) {
        return palettePromise;
      }

      this.paletteLoading = true;
      this.paletteError = null;
      const run = (async () => {
        try {
          const page = await fetchStudyPage(studyId, epoch, 0);
          if (epoch !== lookupEpoch || this.studyCode !== studyId) {
            return false;
          }
          this.rememberParticipants(page.items);
          this.paletteTotal = toCount(page.total);
          this.unfilteredLoadedCount = Math.max(
            this.unfilteredLoadedCount,
            page.items.length
          );
          this.paletteLoaded = true;
          this.paletteError = null;
          return true;
        } catch (err) {
          if (
            epoch === lookupEpoch &&
            this.studyCode === studyId &&
            !isNotFoundError(err)
          ) {
            this.paletteError =
              err instanceof Error ? err.message : String(err);
          }
          return false;
        } finally {
          if (epoch === lookupEpoch && this.studyCode === studyId) {
            this.paletteLoading = false;
          }
          if (palettePromiseKey === key) {
            palettePromise = null;
            palettePromiseKey = "";
          }
        }
      })();
      palettePromise = run;
      palettePromiseKey = key;
      return run;
    },

    /**
     * Resolves a participant UUID for cold record, form, or inspector links via GetSubjectDetail in O(1),
     * falling back to paging Execution's 200-row subject list only when GetSubjectDetail is unavailable.
     */
    async ensureParticipantById(
      participantId: string
    ): Promise<SubjectSummary | null> {
      const studyId = this.studyCode;
      if (!studyId || !participantId) return null;
      if (this.knownParticipants[participantId]) {
        return this.knownParticipants[participantId];
      }
      if (this.failedParticipantIds[participantId]) {
        return null;
      }
      if (
        this.paletteLoaded &&
        this.unfilteredLoadedCount >= this.paletteTotal
      ) {
        this.failedParticipantIds[participantId] = true;
        return null;
      }

      const epoch = lookupEpoch;
      const key = `${epoch}:${studyId}:${participantId}`;
      const existing = inFlightById.get(key);
      if (existing) return existing;

      this.participantLookups[participantId] = true;
      const run = (async (): Promise<SubjectSummary | null> => {
        let offset = this.paletteLoaded ? this.unfilteredLoadedCount : 0;
        try {
          if (typeof executionApi.getSubjectDetail === "function") {
            const detail = await executionApi.getSubjectDetail(participantId);
            if (epoch !== lookupEpoch || this.studyCode !== studyId) {
              return null;
            }
            if (detail === null) {
              this.failedParticipantIds[participantId] = true;
              return null;
            }
            if (detail && typeof detail === "object") {
              if (detail.study_id && detail.study_id !== studyId) {
                this.failedParticipantIds[participantId] = true;
                return null;
              }
              const summary = detailToSummary(detail);
              if (summary) {
                this.rememberParticipants([summary]);
                return this.knownParticipants[summary.id] ?? summary;
              }
            }
          }

          while (true) {
            const page = await fetchStudyPage(studyId, epoch, offset);
            if (epoch !== lookupEpoch || this.studyCode !== studyId) {
              return null;
            }
            this.rememberParticipants(page.items);
            const total = toCount(page.total);
            this.paletteTotal = total;
            const nextOffset = offset + page.items.length;
            if (offset === 0 || this.paletteLoaded) {
              this.paletteLoaded = true;
              this.unfilteredLoadedCount = Math.max(
                this.unfilteredLoadedCount,
                nextOffset
              );
            }
            if (this.knownParticipants[participantId]) {
              return this.knownParticipants[participantId];
            }
            offset = nextOffset;
            if (page.items.length === 0 || offset >= total) {
              this.unfilteredLoadedCount = Math.max(
                this.unfilteredLoadedCount,
                total
              );
              this.failedParticipantIds[participantId] = true;
              return null;
            }
          }
        } catch {
          if (epoch === lookupEpoch && this.studyCode === studyId) {
            this.failedParticipantIds[participantId] = true;
          }
          return null;
        } finally {
          inFlightById.delete(key);
          if (epoch === lookupEpoch && this.studyCode === studyId) {
            delete this.participantLookups[participantId];
          }
        }
      })();
      inFlightById.set(key, run);
      return run;
    },

    /**
     * Resolves a participant by subject code (for cold observation lineage or exact ⌘K palette queries beyond
     * 200 rows), checking GetSubjectDetail first when available and paging listSubjects for its summary UUID.
     */
    async resolveParticipantByCode(
      subjectCode: string,
      studyCode?: string
    ): Promise<SubjectSummary | null> {
      const studyId = (studyCode ?? this.studyCode).trim();
      const code = subjectCode.trim();
      if (!studyId || !code) return null;
      if (studyId !== this.studyCode) {
        this.openStudy(studyId);
      }

      const findKnown = () =>
        Object.values(this.knownParticipants).find(
          (participant) =>
            participant.subject_id.toLowerCase() === code.toLowerCase()
        ) ?? null;

      const known = findKnown();
      if (known) return known;

      const epoch = lookupEpoch;
      const key = `${epoch}:${studyId}:${code.toLowerCase()}`;
      const existing = inFlightByCode.get(key);
      if (existing) return existing;

      const run = (async (): Promise<SubjectSummary | null> => {
        this.participantCodeLookups[code] = true;
        try {
          if (palettePromise && palettePromiseKey === `${epoch}:${studyId}`) {
            await palettePromise;
            if (epoch !== lookupEpoch || this.studyCode !== studyId) {
              return null;
            }
            const afterPalette = findKnown();
            if (afterPalette) return afterPalette;
          }

          if (
            this.paletteLoaded &&
            this.unfilteredLoadedCount >= this.paletteTotal
          ) {
            return null;
          }

          let filterSiteId: string | undefined;
          let filterStatus: string | undefined;
          if (typeof executionApi.getSubjectDetail === "function") {
            try {
              const detail = await executionApi.getSubjectDetail(code);
              if (epoch !== lookupEpoch || this.studyCode !== studyId) {
                return null;
              }
              if (detail === null) return null;
              if (detail && typeof detail === "object") {
                if (detail.study_id && detail.study_id !== studyId) {
                  return null;
                }
                const summary = detailToSummary(detail);
                if (summary) {
                  this.rememberParticipants([summary]);
                  return this.knownParticipants[summary.id] ?? summary;
                }
                filterSiteId = detail.site_id || undefined;
                filterStatus = detail.status || undefined;
              }
            } catch (err) {
              if (
                epoch === lookupEpoch &&
                this.studyCode === studyId &&
                !isNotFoundError(err)
              ) {
                this.paletteError =
                  err instanceof Error ? err.message : String(err);
              }
              return null;
            }
          }

          if (filterSiteId || filterStatus) {
            let filteredOffset = 0;
            while (true) {
              const page = await executionApi.listSubjects({
                studyId,
                siteId: filterSiteId,
                status: filterStatus,
                limit: PARTICIPANT_LOOKUP_PAGE_SIZE,
                offset: filteredOffset,
              });
              if (epoch !== lookupEpoch || this.studyCode !== studyId) {
                return null;
              }
              this.rememberParticipants(page.items);
              const matched = findKnown();
              if (matched) return matched;
              filteredOffset += page.items.length;
              const filteredTotal = toCount(page.total);
              if (page.items.length === 0 || filteredOffset >= filteredTotal) {
                break;
              }
            }
          }

          let offset = this.paletteLoaded ? this.unfilteredLoadedCount : 0;
          while (true) {
            const page = await fetchStudyPage(studyId, epoch, offset);
            if (epoch !== lookupEpoch || this.studyCode !== studyId) {
              return null;
            }
            this.rememberParticipants(page.items);
            const total = toCount(page.total);
            this.paletteTotal = total;
            const nextOffset = offset + page.items.length;
            this.paletteLoaded = true;
            this.unfilteredLoadedCount = Math.max(
              this.unfilteredLoadedCount,
              nextOffset
            );
            const matched = findKnown();
            if (matched) return matched;
            offset = nextOffset;
            if (page.items.length === 0 || offset >= total) {
              this.unfilteredLoadedCount = Math.max(
                this.unfilteredLoadedCount,
                total
              );
              return null;
            }
          }
        } catch (err) {
          if (
            epoch === lookupEpoch &&
            this.studyCode === studyId &&
            !isNotFoundError(err)
          ) {
            this.paletteError =
              err instanceof Error ? err.message : String(err);
          }
          return null;
        } finally {
          inFlightByCode.delete(key);
          if (epoch === lookupEpoch && this.studyCode === studyId) {
            delete this.participantCodeLookups[code];
          }
        }
      })();

      inFlightByCode.set(key, run);
      return run;
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
          ...(filter.q ? { q: filter.q } : {}),
          limit: PARTICIPANT_PAGE_SIZE,
          offset,
        });
        if (request !== participantsRequest) return false;
        this.participants = append
          ? [...this.participants, ...page.items]
          : page.items;
        const total = toCount(page.total);
        this.participantsTotal = total;
        this.rememberParticipants(page.items);
        if (!filter.status && !filter.siteId && !filter.q) {
          this.unfilteredLoadedCount = Math.max(
            this.unfilteredLoadedCount,
            this.participants.length
          );
          if (this.participants.length >= total) {
            this.paletteTotal = total;
            this.paletteLoaded = true;
          }
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
        const summary = await executionApi.getSubjectCounts({ studyId });
        if (request !== pulseRequest) return false;
        const counts = summary.counts ?? {};
        this.pulse = {
          total: toCount(summary.total),
          byState: PARTICIPANT_STATES.map((state) => ({
            code: state.code,
            label: state.label,
            count: toCount(
              counts[state.code] ?? counts[state.code.toLowerCase()] ?? 0
            ),
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
        this.rememberParticipants([newest]);
        if (this.paletteLoaded) {
          this.paletteTotal = Math.max(this.paletteTotal, toCount(total));
          this.unfilteredLoadedCount = Math.max(
            this.unfilteredLoadedCount,
            this.paletteParticipants.length
          );
        }
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
        this.rememberParticipants([changed]);
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

    /**
     * Assigns a clinical site to a siteless participant with an audited reason, synchronizing its kernel
     * screening enrollment and refreshing the open participant record so casebooks unlock immediately.
     */
    async assignParticipantSite(
      participant: SubjectSummary,
      siteId: string,
      reason: string
    ): Promise<boolean> {
      const toasts = useToastStore();
      const normalizedSite = siteId.trim();
      const normalizedReason = reason.trim();
      if (!normalizedSite || !normalizedReason) return false;
      let changed: SubjectSummary;
      try {
        const updated = await executionApi.assignSubjectSite(
          participant.id,
          normalizedSite,
          normalizedReason
        );
        changed = {
          ...participant,
          site_id: updated.site_id ?? normalizedSite,
        };
        this.rememberParticipants([changed]);
        const index = this.participants.findIndex(
          (p) => p.id === participant.id
        );
        if (index >= 0) this.participants.splice(index, 1, changed);
        toasts.push({
          tone: "success",
          title: `Site ${changed.site_id} assigned to ${participant.subject_id}`,
          message: "",
        });
      } catch (err) {
        toasts.apiError(
          `Site ${normalizedSite} was not assigned to ${participant.subject_id}`,
          err
        );
        return false;
      }
      await Promise.all([
        this.refreshAfterChange(),
        this.record.subjectCode === participant.subject_id
          ? this.loadRecord(changed)
          : null,
      ]);
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
        if (enrollment?.id) {
          seedNodeSummary(`enrollment:${enrollment.id}`, {
            kind: "enrollment",
            code: subjectCode,
            title: `Enrollment ${subjectCode}`,
          });
        }
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

    /**
     * Registers a mounted Clinical Canvas `Explain` coordinator (e.g. `CanvasInspector` or `StudyCanvasView`).
     * Stored outside Pinia `$state` so switching studies (`openStudy` -> `$reset`) preserves active listeners.
     */
    registerExplainCoordinator(handler: ExplainCoordinatorHandler): () => void {
      let set = explainCoordinators.get(this);
      if (!set) {
        set = new Set();
        explainCoordinators.set(this, set);
      }
      set.add(handler);
      return () => {
        this.unregisterExplainCoordinator(handler);
      };
    },

    unregisterExplainCoordinator(handler: ExplainCoordinatorHandler): void {
      explainCoordinators.get(this)?.delete(handler);
    },

    /**
     * Requests opening the inspector `Explain` verb and focusing `#kernel-explain`.
     * Returns `true` when an active study selection or explanation handled the request.
     */
    requestExplain(options?: ExplainRequestOptions): boolean {
      const set = explainCoordinators.get(this);
      if (!set || set.size === 0) return false;
      // Invoke in reverse registration order so the innermost mounted CanvasInspector handles first,
      // falling back to StudyCanvasView when a study-level panel passes a targetNodeRef with no current selection.
      const handlers = [...set].reverse();
      for (const handler of handlers) {
        if (handler(options)) {
          return true;
        }
      }
      return false;
    },

    /**
     * Coordinates `Cmd+E` / `Ctrl+E` across `AppShell` and standalone `StudyCanvasView`.
     * Ignores editable inputs and no-ops when no study selection or explanation exists.
     */
    handleExplainShortcut(event: KeyboardEvent): boolean {
      if (event.defaultPrevented || event.altKey || event.shiftKey) {
        return false;
      }
      if (
        !(event.metaKey || event.ctrlKey) ||
        typeof event.key !== "string" ||
        event.key.toLowerCase() !== "e"
      ) {
        return false;
      }
      if (
        isEditableShortcutTarget(event.target) ||
        (typeof document !== "undefined" &&
          isEditableShortcutTarget(document.activeElement))
      ) {
        return false;
      }
      const handled = this.requestExplain({ fromShortcut: true });
      if (handled) {
        event.preventDefault();
      }
      return handled;
    },
  },
});

registerSessionCacheClearHandler((context?: { reason?: string }) => {
  const pinia = getActivePinia();
  if (!pinia || !("studyCanvas" in pinia.state.value)) return;
  const store = useStudyCanvasStore(pinia);
  if (context?.reason === "logout") {
    store.$reset();
  }
  store.clearSessionCaches();
});
