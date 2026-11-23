import { describe, expect, it } from "vitest";
import {
  INSPECTOR_DESCRIPTORS,
  findInspectorForKind,
  resolveActiveInspector,
} from "../../src/canvas/inspectors/registry";
import type { CanvasInspectorContext } from "../../src/canvas/inspectors/types";
import { STAGES, findSection, findStage } from "../../src/canvas/lifecycle";
import {
  SECTION_DESCRIPTORS,
  findSectionDescriptor,
  sectionsForStage,
} from "../../src/canvas/sections/registry";
import {
  WORKSPACE_DESCRIPTORS,
  resolveActiveWorkspace,
} from "../../src/canvas/workspaces/registry";
import type { CanvasWorkspaceContext } from "../../src/canvas/workspaces/types";
import {
  AVAILABLE_SCREENS,
  isScreenAvailable,
} from "../../src/router/availability";
import {
  DATA_COLLECTION_HUB_DESCRIPTOR,
  PALETTE_DESCRIPTORS,
  SITE_READINESS_DESCRIPTORS,
  resolveSopEscalationCategory,
  webRouteDescriptors,
} from "../../src/router/registry";

const SAMPLE_UUID = "0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e";

function baseInspectorContext(
  overrides: Partial<CanvasInspectorContext> = {}
): CanvasInspectorContext {
  return {
    studyCode: "CADENCE-101",
    nodeRef: "",
    participant: null,
    missing: false,
    protocolVersion: null,
    protocolVersionError: "",
    genericNodeRef: "",
    genericNode: null,
    genericNodeLoading: false,
    genericDisplay: null,
    recordLocation: null,
    dataEntryLocation: null,
    handoffLocation: null,
    canManageParticipants: false,
    canUnblindParticipants: false,
    ...overrides,
  };
}

describe("Glob-discovered Clinical Canvas & Router registries", () => {
  it("orders lifecycle stages deterministically by explicit order key", () => {
    expect(STAGES.map((stage) => stage.id)).toEqual([
      "design",
      "start",
      "conduct",
      "data",
      "analyze",
      "submit",
    ]);
    for (let i = 1; i < STAGES.length; i++) {
      expect(STAGES[i - 1].order ?? 0).toBeLessThanOrEqual(
        STAGES[i].order ?? 0
      );
    }
  });

  it("discovers stage sections via import.meta.glob in deterministic order within each stage", () => {
    expect(SECTION_DESCRIPTORS.length).toBeGreaterThan(0);

    for (const stage of STAGES) {
      const stageDescriptors = SECTION_DESCRIPTORS.filter(
        (d) => d.stageId === stage.id
      );
      expect(sectionsForStage(stage.id).map((s) => s.id)).toEqual(
        stageDescriptors.map((d) => d.id)
      );
      for (let i = 1; i < stageDescriptors.length; i++) {
        const prev = stageDescriptors[i - 1];
        const curr = stageDescriptors[i];
        const cmp =
          prev.order !== curr.order
            ? prev.order - curr.order
            : prev.id.localeCompare(curr.id);
        expect(cmp).toBeLessThanOrEqual(0);
      }
    }

    expect(findSectionDescriptor("participants")?.stageId).toBe("conduct");
    expect(findSectionDescriptor("protocol-versions")?.stageId).toBe("design");
    expect(findSectionDescriptor("queries")?.isDataWorklist).toBe(true);
    expect(findSectionDescriptor("unknown-section")).toBeUndefined();
  });

  it("discovers workspace descriptors in deterministic priority order and resolves zoom levels", () => {
    expect(WORKSPACE_DESCRIPTORS.map((d) => d.id)).toEqual([
      "proposal",
      "observation",
      "form",
      "participant-record",
      "resolving-subject",
      "study-home",
      "section",
      "stage-overview",
    ]);

    const conductStage = findStage("conduct")!;
    const participantsMatch = findSection("participants")!;
    const participantsDescriptor = findSectionDescriptor("participants")!;

    const minimalCtx = {
      studyCode: "CADENCE-101",
      reviewer: "user-1",
      stage: undefined,
      section: undefined,
      sectionDescriptor: undefined,
      sectionContext: {} as CanvasWorkspaceContext["sectionContext"],
      proposalRef: "",
      openObservationRef: "",
      zoomObservationLabel: "Observation",
      observationReturnLocation: { name: "study" },
      observationReturnLabel: "Back",
      hasOpenForm: false,
      isNewForm: false,
      openFormId: null,
      loadedFormLabel: undefined,
      zoomFormLabel: "Forms",
      highlightItemQuery: "",
      initialSpecId: "",
      initialVisitId: "",
      recordRefId: "",
      participantCodeQuery: "",
      isParticipantRecordOpen: false,
      recordParticipant: null,
      isRecordParticipantLoading: false,
      activeFormParticipant: null,
      activeEnrollmentId: null,
      isResolvingConductSubject: false,
      canManage: false,
      canAnswer: false,
      canVerify: false,
      canManageSites: false,
      isDemo: false,
      canvas: {} as CanvasWorkspaceContext["canvas"],
      participantsLocation: { name: "study" },
      formBackLocation: { name: "study" },
      formBackLabel: "Back",
      formLocationById: () => ({ name: "study" }),
      newFormLocationFor: () => ({ name: "study" }),
      newFormCellLocationFor: () => ({ name: "study" }),
      ecrfHandoffLocationFor: () => null,
      observationLocationFromRecord: () => ({ name: "study" }),
      observationLocationFromForm: () => ({ name: "study" }),
      sectionLocationForStage: () => ({ name: "study" }),
      onObservationLoaded: () => {},
      onFormDirtyChange: () => {},
      onFormLoaded: () => {},
      onFormClosed: () => {},
      onRetryParticipantRecord: () => {},
      onDrillToParticipants: () => {},
      onOpenParticipantsForSite: () => {},
    } satisfies CanvasWorkspaceContext;

    expect(resolveActiveWorkspace(minimalCtx)?.id).toBe("study-home");
    expect(
      resolveActiveWorkspace({
        ...minimalCtx,
        stage: conductStage,
      })?.id
    ).toBe("stage-overview");
    expect(
      resolveActiveWorkspace({
        ...minimalCtx,
        stage: conductStage,
        section: participantsMatch.section,
        sectionDescriptor: participantsDescriptor,
      })?.id
    ).toBe("section");
    expect(
      resolveActiveWorkspace({
        ...minimalCtx,
        stage: conductStage,
        isParticipantRecordOpen: true,
        recordRefId: SAMPLE_UUID,
      })?.id
    ).toBe("participant-record");
    expect(
      resolveActiveWorkspace({
        ...minimalCtx,
        stage: conductStage,
        hasOpenForm: true,
        isNewForm: true,
      })?.id
    ).toBe("form");
    expect(
      resolveActiveWorkspace({
        ...minimalCtx,
        openObservationRef: `observation:${SAMPLE_UUID}`,
      })?.id
    ).toBe("observation");
    expect(
      resolveActiveWorkspace({
        ...minimalCtx,
        proposalRef: `proposal:${SAMPLE_UUID}`,
      })?.id
    ).toBe("proposal");
  });

  it("discovers NodeRef inspector descriptors and resolves participant, protocol_version, and fallback panes", () => {
    expect(INSPECTOR_DESCRIPTORS.map((d) => d.id)).toEqual([
      "participant",
      "protocol-version",
      "generic-node",
    ]);

    expect(findInspectorForKind("participant")?.id).toBe("participant");
    expect(findInspectorForKind("protocol_version")?.id).toBe(
      "protocol-version"
    );
    expect(findInspectorForKind("study_site")?.id).toBe("generic-node");

    const participantCtx = baseInspectorContext({
      nodeRef: `participant:${SAMPLE_UUID}`,
      participant: {
        id: SAMPLE_UUID,
        subject_id: "SUBJ-001",
        study_id: "CADENCE-101",
        site_id: "SITE-01",
        status: "ACTIVE",
        enrollment_index: 1,
      } as CanvasInspectorContext["participant"],
    });
    const resolvedParticipant = resolveActiveInspector(participantCtx);
    expect(resolvedParticipant?.id).toBe("participant");
    expect(resolvedParticipant?.resolveHeader(participantCtx)).toMatchObject({
      kindLabel: "Participant",
      title: "SUBJ-001",
      canSimulate: false,
      impactLineageMode: "impact",
    });

    const versionCtx = baseInspectorContext({
      protocolVersion: {
        ref: `protocol_version:${SAMPLE_UUID}`,
        number: 2,
        version: "2.0",
        status: "APPROVED",
        parent_version: `protocol_version:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6f`,
      } as CanvasInspectorContext["protocolVersion"],
    });
    const resolvedVersion = resolveActiveInspector(versionCtx);
    expect(resolvedVersion?.id).toBe("protocol-version");
    expect(resolvedVersion?.resolveHeader(versionCtx)).toMatchObject({
      kindLabel: "Protocol version",
      title: "Version 2",
      canSimulate: true,
      impactLineageMode: "amendment",
    });
  });

  it("discovers web route descriptors in deterministic order and keeps AVAILABLE_SCREENS mutable", () => {
    expect(webRouteDescriptors.length).toBeGreaterThan(0);
    for (let i = 1; i < webRouteDescriptors.length; i++) {
      const prev = webRouteDescriptors[i - 1];
      const curr = webRouteDescriptors[i];
      const cmp =
        (prev.order ?? 9999) !== (curr.order ?? 9999)
          ? (prev.order ?? 9999) - (curr.order ?? 9999)
          : String(prev.route.name).localeCompare(String(curr.route.name));
      expect(cmp).toBeLessThanOrEqual(0);
    }

    expect(DATA_COLLECTION_HUB_DESCRIPTOR?.route.name).toBe("ecrf");
    expect(SITE_READINESS_DESCRIPTORS.map((d) => d.route.name)).toEqual(
      expect.arrayContaining(["ctms", "ssu"])
    );
    expect(PALETTE_DESCRIPTORS.length).toBeGreaterThan(0);
    expect(resolveSopEscalationCategory("/ecrf/form-1")).toBe(
      "PROTOCOL_DEVIATION"
    );
    expect(resolveSopEscalationCategory("/ctms")).toBe("MONITORING_FINDING");

    const syntheticName = "__test_dynamic_route__";
    expect(isScreenAvailable(syntheticName)).toBe(false);
    AVAILABLE_SCREENS.add(syntheticName);
    try {
      expect(isScreenAvailable(syntheticName)).toBe(true);
    } finally {
      AVAILABLE_SCREENS.delete(syntheticName);
    }
    expect(isScreenAvailable(syntheticName)).toBe(false);
  });
});
