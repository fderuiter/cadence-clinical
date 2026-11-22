import { describe, it, expect, vi, beforeEach } from "vitest";
import { kernelApi } from "../../src/api/kernelApi";
import {
  MAX_CONCURRENT_NODE_LOOKUPS,
  clearNodeResolutionCache,
  ensureNodesResolved,
  formatCommandTitle,
  formatFieldPath,
  formatKindTitle,
  formatMaskedNode,
  getNodeDisplay,
  normalizeLookupNodeRef,
  parseLookupNodeRef,
  resetNodeResolutionSession,
  resolveNode,
  seedNodeSummary,
} from "../../src/canvas/nodeResolver";

vi.mock("../../src/api/kernelApi", () => ({
  kernelApi: {
    getNode: vi.fn(),
  },
}));

const PARTICIPANT_REF = "participant:0192f3c4-5e6a-7b8c-9d0e-000000000001@4";
const SITE_REF = "study_site:0192f3c4-5e6a-7b8c-9d0e-00000000000c@2";

describe("Client node resolution and plain clinical formatting", () => {
  beforeEach(() => {
    clearNodeResolutionCache();
    vi.mocked(kernelApi.getNode).mockReset();
  });

  it("resolves human-readable codes and titles on demand and caches them per study session", async () => {
    resetNodeResolutionSession("CAD-101");
    vi.mocked(kernelApi.getNode).mockResolvedValue({
      ref: SITE_REF,
      kind: "study_site",
      code: "017",
      title: "Boston General",
      status: "Active",
      mask: null,
    });

    const first = await resolveNode(SITE_REF);
    const second = await resolveNode(SITE_REF);

    expect(first?.code).toBe("017");
    expect(second).toEqual(first);
    expect(kernelApi.getNode).toHaveBeenCalledTimes(1);
    expect(getNodeDisplay(SITE_REF).label).toBe("Site 017 · Boston General");

    // Calling resetNodeResolutionSession with the same study preserves the warm session cache
    resetNodeResolutionSession("CAD-101");
    await resolveNode(SITE_REF);
    expect(kernelApi.getNode).toHaveBeenCalledTimes(1);

    // Switching studies clears the previous session's cache
    resetNodeResolutionSession("ONC-401");
    await resolveNode(SITE_REF);
    expect(kernelApi.getNode).toHaveBeenCalledTimes(2);
  });

  it("deduplicates concurrent lookups for the same NodeRef and bounds concurrent requests", async () => {
    resetNodeResolutionSession("CAD-101");
    let inFlightCount = 0;
    let maxObservedInFlight = 0;
    const resolvers: Array<() => void> = [];

    vi.mocked(kernelApi.getNode).mockImplementation(
      (ref) =>
        new Promise((resolve) => {
          inFlightCount += 1;
          maxObservedInFlight = Math.max(maxObservedInFlight, inFlightCount);
          resolvers.push(() => {
            inFlightCount -= 1;
            resolve({
              ref,
              kind: "observation",
              code: null,
              title: `Observation ${resolvers.length}`,
              status: "Recorded",
              mask: null,
            });
          });
        })
    );

    // Duplicate concurrent requests for the same ref share one in-flight call
    const dupA = resolveNode(PARTICIPANT_REF);
    const dupB = resolveNode(PARTICIPANT_REF);
    expect(kernelApi.getNode).toHaveBeenCalledTimes(1);
    resolvers.shift()?.();
    await Promise.all([dupA, dupB]);

    // Many distinct refs are bounded to MAX_CONCURRENT_NODE_LOOKUPS
    inFlightCount = 0;
    maxObservedInFlight = 0;
    const refs = Array.from(
      { length: 10 },
      (_, idx) =>
        `observation:0192f3c4-5e6a-7b8c-9d0e-${String(idx + 10).padStart(12, "0")}`
    );
    ensureNodesResolved(refs);

    await Promise.resolve();
    expect(maxObservedInFlight).toBeLessThanOrEqual(
      MAX_CONCURRENT_NODE_LOOKUPS
    );

    while (resolvers.length > 0) {
      resolvers.shift()?.();
      await Promise.resolve();
      await Promise.resolve();
    }

    expect(maxObservedInFlight).toBe(MAX_CONCURRENT_NODE_LOOKUPS);
    expect(kernelApi.getNode).toHaveBeenCalledTimes(1 + refs.length);
  });

  it("preserves masked placeholders and uses seeded local hints without exposing raw UUIDs", async () => {
    resetNodeResolutionSession("CAD-101");
    seedNodeSummary("participant:0192f3c4-5e6a-7b8c-9d0e-000000000099", {
      kind: "participant",
      code: "017-0099",
      title: "017-0099",
    });

    expect(
      getNodeDisplay("participant:0192f3c4-5e6a-7b8c-9d0e-000000000099@2").label
    ).toBe("017-0099");

    vi.mocked(kernelApi.getNode).mockResolvedValueOnce({
      ref: null,
      kind: "participant",
      code: null,
      title: null,
      status: null,
      mask: "OutsideScope",
    });
    await resolveNode(PARTICIPANT_REF);
    const maskedSummary = getNodeDisplay(PARTICIPANT_REF);
    expect(maskedSummary.masked).toBe(true);
    expect(maskedSummary.label).toBe("Hidden participant (outside your site)");
    expect(formatMaskedNode("consent", "Blinded")).toBe(
      "Hidden consent (blinded)"
    );
    expect(
      formatKindTitle("subscription:01960000-0000-7000-8000-000000000009")
    ).toBe("Subscription");
    expect(
      formatKindTitle(
        "product_entitlement:01960000-0000-7000-8000-00000000000a"
      )
    ).toBe("Product entitlement");
    expect(formatKindTitle(null)).toBe("Record");
  });

  it("formats internal command identifiers and JSON pointer field paths into readable clinical copy", () => {
    expect(formatCommandTitle("ChangeParticipantState")).toBe(
      "Change participant state"
    );
    expect(formatCommandTitle("WithdrawParticipant")).toBe(
      "Withdraw participant"
    );
    expect(formatFieldPath("/window/upper")).toBe("Window · Upper");
    expect(formatFieldPath("/epochs/0/name")).toBe("Epochs #1 · Name");
  });

  it("normalizes bare <uuid>@<version> concept references to concept:<uuid>@<version> while leaving non-UUID concept codes untouched", async () => {
    resetNodeResolutionSession("CAD-101");
    const bareConceptRef = "0192f3c4-5e6a-7b8c-9d0e-00000000c001@2";
    const canonicalConceptRef = `concept:${bareConceptRef}`;

    expect(normalizeLookupNodeRef(bareConceptRef)).toBe(canonicalConceptRef);
    expect(parseLookupNodeRef(bareConceptRef)).toEqual({
      kind: "concept",
      id: "0192f3c4-5e6a-7b8c-9d0e-00000000c001",
      version: 2,
    });
    expect(normalizeLookupNodeRef("concept:systolic@1")).toBeNull();
    expect(normalizeLookupNodeRef("dm@sponsor.test")).toBeNull();

    vi.mocked(kernelApi.getNode).mockResolvedValueOnce({
      ref: canonicalConceptRef,
      kind: "concept",
      code: "SYSBP",
      title: "Systolic Blood Pressure",
      status: "Active",
      mask: null,
    });

    await resolveNode(bareConceptRef);
    expect(kernelApi.getNode).toHaveBeenCalledWith(canonicalConceptRef);
    expect(getNodeDisplay(bareConceptRef).label).toBe(
      "SYSBP · Systolic Blood Pressure"
    );
  });
});
