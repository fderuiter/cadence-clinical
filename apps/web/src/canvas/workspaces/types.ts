import type { Component } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfForm } from "../../api/ecrfFormsApi";
import type { SubjectSummary } from "../../api/executionApi";
import type { KernelObservation } from "../../api/kernelApi";
import type { useStudyCanvasStore } from "../../stores/studyCanvas";
import type {
  CanvasSectionContext,
  CanvasSectionDescriptor,
} from "../sections/types";
import type { Section, Stage } from "../stages/types";

export interface CanvasWorkspaceBranchMeta {
  key: string;
  groupKey: string;
  depth: number;
  targets: Array<string | (() => HTMLElement | null)>;
}

export interface CanvasWorkspaceRenderSpec {
  component: Component;
  key?: string;
  props?: Record<string, unknown>;
  listeners?: Record<string, (...args: never[]) => unknown>;
}

export interface CanvasWorkspaceContext {
  studyCode: string;
  reviewer: string;
  stage: Stage | undefined;
  section: Section | undefined;
  sectionDescriptor: CanvasSectionDescriptor | undefined;
  sectionContext: CanvasSectionContext;
  proposalRef: string;
  openObservationRef: string;
  zoomObservationLabel: string;
  observationReturnLocation: RouteLocationRaw;
  observationReturnLabel: string;
  hasOpenForm: boolean;
  isNewForm: boolean;
  openFormId: string | null;
  loadedFormLabel: string | undefined;
  zoomFormLabel: string;
  highlightItemQuery: string;
  initialSpecId: string;
  initialVisitId: string;
  recordRefId: string;
  participantCodeQuery: string;
  isParticipantRecordOpen: boolean;
  recordParticipant: SubjectSummary | null;
  isRecordParticipantLoading: boolean;
  activeFormParticipant: SubjectSummary | null;
  activeEnrollmentId: string | null;
  isResolvingConductSubject: boolean;
  canManage: boolean;
  canAnswer: boolean;
  canVerify: boolean;
  canManageSites: boolean;
  isDemo: boolean;
  canvas: ReturnType<typeof useStudyCanvasStore>;
  participantsLocation: RouteLocationRaw;
  formBackLocation: RouteLocationRaw;
  formBackLabel: string;
  formLocationById: (
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw;
  newFormLocationFor: (participant: SubjectSummary) => RouteLocationRaw;
  newFormCellLocationFor: (
    participant: SubjectSummary,
    specId: string,
    visitId?: string | null
  ) => RouteLocationRaw;
  ecrfHandoffLocationFor: (
    participant: SubjectSummary
  ) => RouteLocationRaw | null;
  observationLocationFromRecord: (
    observationId: string,
    formId?: string,
    itemId?: string | null
  ) => RouteLocationRaw;
  observationLocationFromForm: (
    observationId: string,
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw;
  sectionLocationForStage: (
    stageId: string,
    sectionId: string
  ) => RouteLocationRaw;
  onObservationLoaded: (obs: KernelObservation) => void;
  onFormDirtyChange: (dirty: boolean) => void;
  onFormLoaded: (form: EcrfForm) => void;
  onFormClosed: () => void;
  onRetryParticipantRecord: () => void;
  onDrillToParticipants: (status: string) => void;
  onOpenParticipantsForSite: (siteCode: string) => void;
}

export interface CanvasWorkspaceDescriptor {
  id: string;
  order: number;
  matches: (ctx: CanvasWorkspaceContext) => boolean;
  resolveLabel: (ctx: CanvasWorkspaceContext) => string;
  resolveBranch: (ctx: CanvasWorkspaceContext) => CanvasWorkspaceBranchMeta;
  loadOnEnter?: (ctx: CanvasWorkspaceContext) => void;
  resolveRender: (ctx: CanvasWorkspaceContext) => CanvasWorkspaceRenderSpec;
}
