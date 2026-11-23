import {
  executionApi,
  type RandomizationResponse,
  type SubjectSummary,
} from "../api/executionApi";
import { participantStateLabel } from "../canvas/lifecycle";
import {
  detailToSummary,
  type CanvasParticipant,
  type RecordParticipantConsentInput,
} from "./studyCanvas";
import { useToastStore } from "./toasts.js";

const toCount = (value: number | string): number => Number(value) || 0;

export interface StudyCanvasMutationStore {
  studyCode: string;
  participants: CanvasParticipant[];
  knownParticipants: Record<string, CanvasParticipant>;
  paletteParticipants: CanvasParticipant[];
  paletteLoaded: boolean;
  paletteTotal: number;
  unfilteredLoadedCount: number;
  record: {
    subjectCode: string;
    enrollmentId: string | null;
    loading: boolean;
    error: boolean;
  };
  rememberParticipants(items: CanvasParticipant[]): void;
  refreshAfterChange(options?: { list?: boolean }): Promise<void>;
  loadRecord(participant: SubjectSummary): Promise<boolean>;
}

export async function refreshParticipantDetailMutation(
  store: StudyCanvasMutationStore,
  participantId: string
): Promise<CanvasParticipant | null> {
  const studyId = store.studyCode;
  if (
    !studyId ||
    !participantId ||
    typeof executionApi.getSubjectDetail !== "function"
  ) {
    return store.knownParticipants[participantId] ?? null;
  }
  try {
    const detail = await executionApi.getSubjectDetail(participantId);
    if (!detail || store.studyCode !== studyId) {
      return store.knownParticipants[participantId] ?? null;
    }
    if (detail.study_id && detail.study_id !== studyId) {
      return store.knownParticipants[participantId] ?? null;
    }
    const summary = detailToSummary(detail);
    if (!summary) return store.knownParticipants[participantId] ?? null;
    store.rememberParticipants([summary]);
    const merged = store.knownParticipants[summary.id] ?? summary;
    const index = store.participants.findIndex((p) => p.id === summary.id);
    if (index >= 0) store.participants.splice(index, 1, merged);
    return merged;
  } catch {
    return store.knownParticipants[participantId] ?? null;
  }
}

export async function findNewestParticipantMutation(
  store: StudyCanvasMutationStore,
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
    if (studyId !== store.studyCode || newest?.subject_id !== subjectCode) {
      return null;
    }
    store.rememberParticipants([newest]);
    if (store.paletteLoaded) {
      store.paletteTotal = Math.max(store.paletteTotal, toCount(total));
      store.unfilteredLoadedCount = Math.max(
        store.unfilteredLoadedCount,
        store.paletteParticipants.length
      );
    }
    return newest;
  } catch {
    return null;
  }
}

export async function addParticipantMutation(
  store: StudyCanvasMutationStore,
  subjectCode: string,
  siteId: string,
  reason: string
): Promise<{ added: boolean; participant: SubjectSummary | null }> {
  const studyId = store.studyCode;
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
    findNewestParticipantMutation(store, studyId, subjectCode),
    store.refreshAfterChange({ list: true }),
  ]);
  return { added: true, participant };
}

export async function changeParticipantStateMutation(
  store: StudyCanvasMutationStore,
  participant: SubjectSummary,
  state: string,
  reason: string,
  sigToken?: string
): Promise<boolean> {
  const toasts = useToastStore();
  const label = participantStateLabel(state);
  try {
    const updated = sigToken
      ? await executionApi.changeSubjectState(
          participant.id,
          state,
          reason,
          sigToken
        )
      : await executionApi.changeSubjectState(participant.id, state, reason);
    const changed = { ...participant, status: updated.status };
    store.rememberParticipants([changed]);
    const index = store.participants.findIndex((p) => p.id === participant.id);
    if (index >= 0) store.participants.splice(index, 1, changed);
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
  await store.refreshAfterChange();
  if (state === "UNBLINDED") {
    await refreshParticipantDetailMutation(store, participant.id);
  }
  return true;
}

export async function assignParticipantSiteMutation(
  store: StudyCanvasMutationStore,
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
    store.rememberParticipants([changed]);
    const index = store.participants.findIndex((p) => p.id === participant.id);
    if (index >= 0) store.participants.splice(index, 1, changed);
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
    store.refreshAfterChange(),
    store.record.subjectCode === participant.subject_id
      ? store.loadRecord(changed)
      : null,
  ]);
  return true;
}

export async function recordParticipantConsentMutation(
  store: StudyCanvasMutationStore,
  participant: SubjectSummary,
  input: RecordParticipantConsentInput
): Promise<boolean> {
  const studyId = (participant.study_id || store.studyCode).trim();
  const toasts = useToastStore();
  if (!studyId) return false;
  try {
    const saved = await executionApi.recordSubjectConsent(
      participant.id,
      {
        protocol_version: {
          study_id: studyId,
          version_tag: input.versionTag,
          version_index: input.versionIndex,
        },
        icf_signed: !input.isPaperOverride || Boolean(input.icfSignedDate),
        icf_signed_date: input.icfSignedDate,
        requires_reconsent: Boolean(input.requiresReconsent),
        is_paper_override: Boolean(input.isPaperOverride),
      },
      input.reason
    );
    const updated: CanvasParticipant = {
      ...(store.knownParticipants[participant.id] ?? participant),
      active_protocol_version:
        saved.version_tag ||
        input.versionTag ||
        participant.active_protocol_version,
    };
    store.rememberParticipants([updated]);
    const index = store.participants.findIndex((p) => p.id === participant.id);
    if (index >= 0) {
      store.participants.splice(
        index,
        1,
        store.knownParticipants[participant.id] ?? updated
      );
    }
    toasts.push({
      tone: "success",
      title: `Consent recorded for ${participant.subject_id}`,
      message: `Protocol version ${saved.version_tag || input.versionTag}`,
    });
  } catch (err) {
    toasts.apiError(
      `Consent was not recorded for ${participant.subject_id}`,
      err
    );
    return false;
  }
  await store.refreshAfterChange();
  return true;
}

export async function randomizeParticipantMutation(
  store: StudyCanvasMutationStore,
  participant: SubjectSummary,
  stratificationFactors: Record<string, string> | null,
  reason: string,
  sigToken: string
): Promise<RandomizationResponse | null> {
  const toasts = useToastStore();
  const normalizedFactors =
    stratificationFactors && Object.keys(stratificationFactors).length > 0
      ? stratificationFactors
      : null;
  let randomized: RandomizationResponse;
  try {
    randomized = await executionApi.randomizeSubject(
      participant.id,
      { stratification_factors: normalizedFactors },
      reason,
      sigToken
    );
    const updated: CanvasParticipant = {
      ...(store.knownParticipants[participant.id] ?? participant),
      status: randomized.status || "RANDOMIZED",
      kit_number: randomized.kit_number ?? null,
    };
    store.rememberParticipants([updated]);
    const index = store.participants.findIndex((p) => p.id === participant.id);
    if (index >= 0) {
      store.participants.splice(
        index,
        1,
        store.knownParticipants[participant.id] ?? updated
      );
    }
    toasts.push({
      tone: "success",
      title: `${participant.subject_id} randomized`,
      message: randomized.kit_number
        ? `Assigned kit ${randomized.kit_number}.`
        : "",
    });
  } catch (err) {
    toasts.apiError(`${participant.subject_id} was not randomized`, err);
    return null;
  }
  await Promise.all([
    store.refreshAfterChange(),
    refreshParticipantDetailMutation(store, participant.id),
  ]);
  return randomized;
}
