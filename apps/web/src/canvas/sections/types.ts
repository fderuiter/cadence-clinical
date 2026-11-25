import type { Component } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import type { KernelProtocolVersion } from "../../api/kernelApi";
import type { useStudyCanvasStore } from "../../stores/studyCanvas";
import type { SectionId, StageId } from "../stages/types";

export interface StudyQueriesFilterState {
  status: string;
  owner: string;
  origin: string;
  priority: string;
  escalated: string;
  assignee: string;
  site: string;
  participant: string;
  changed: string;
}

export interface CanvasSectionContext {
  studyCode: string;
  siteFilter: string;
  activeSite: string;
  activeParticipantScopeParam: string;
  activeInspectorParticipantNodeParam: string;
  statusFilter: string;
  participantSearchFilter: string;
  selectedParticipantOrRecordId: string;
  selectedVersionText: string;
  canChangeParticipants: boolean;
  canManageSites: boolean;
  canReadDocuments: boolean;
  canManage: boolean;
  canAnswer: boolean;
  canManageRules: boolean;
  queryStatusFilter: string;
  queryOwnerFilter: string;
  queryOriginFilter: string;
  queryPriorityFilter: string;
  queryEscalatedFilter: string;
  queryAssigneeFilter: string;
  queryParticipantFilter: string;
  queryChangedFilter: string;
  canvas: ReturnType<typeof useStudyCanvasStore>;
  onSelectParticipant: (participant: SubjectSummary) => void;
  onApplyParticipantFilter: (filter: {
    status: string;
    siteId: string;
    q?: string;
  }) => void;
  onSelectVersion: (version: KernelProtocolVersion) => void;
  onOpenParticipantsForSite: (siteCode: string) => void;
  onOpenActivationForSite: (siteCode: string) => void;
  onApplyStudyQueryFilter: (filter: StudyQueriesFilterState) => void;
}

export interface CanvasSectionRenderSpec {
  component: Component;
  key?: string;
  props?: Record<string, unknown>;
  listeners?: Record<string, (...args: never[]) => unknown>;
}

export interface CanvasSectionDescriptor {
  id: SectionId;
  stageId: StageId;
  order: number;
  label: string;
  description: string;
  focusTargets?: string[];
  isDataWorklist?: boolean;
  loadOnEnter?: (ctx: CanvasSectionContext) => void;
  resolveRender: (ctx: CanvasSectionContext) => CanvasSectionRenderSpec;
}
