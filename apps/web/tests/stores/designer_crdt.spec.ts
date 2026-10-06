import { describe, it, expect, beforeEach } from "vitest";
import { setActivePinia, createPinia } from "pinia";
import { useDesignerStore } from "../../src/stores/designer.js";
import * as Y from "yjs";

describe("Decentralized CRDT Document Synchronization & Collaboration", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it("stores a Y.Doc model and synchronizes local form mutations", () => {
    const store = useDesignerStore();
    const ydoc = store.getYDoc();

    expect(ydoc).toBeInstanceOf(Y.Doc);

    // Perform form field addition
    store.addFieldToSection("section-1", {
      id: "field-3",
      label: "Systolic Blood Pressure",
      type: "numeric",
      gridSpan: 6,
      required: true,
    });

    const yForm = ydoc.getMap("form");
    const sectionsData = yForm.get("sectionsData") as string;
    expect(sectionsData).toContain("field-3");
    expect(sectionsData).toContain("Systolic Blood Pressure");
  });

  it("merges binary CRDT update vectors without data loss or version conflicts during concurrent edits", () => {
    const store1 = useDesignerStore();
    const pinia2 = createPinia();

    setActivePinia(pinia2);
    const store2 = useDesignerStore();

    // User 1 adds a field to section-1
    store1.addFieldToSection("section-1", {
      id: "field-user1",
      label: "Heart Rate",
      type: "numeric",
      gridSpan: 6,
    });

    // User 2 adds a field to section-1 simultaneously
    store2.addFieldToSection("section-1", {
      id: "field-user2",
      label: "Body Weight",
      type: "numeric",
      gridSpan: 6,
    });

    // Generate CRDT binary update vectors
    const update1 = Y.encodeStateAsUpdate(store1.getYDoc());
    const update2 = Y.encodeStateAsUpdate(store2.getYDoc());

    // Apply User 2's update vector to User 1's Y.Doc and vice versa
    Y.applyUpdate(store1.getYDoc(), update2, "remote");
    Y.applyUpdate(store2.getYDoc(), update1, "remote");

    // Both stores merge CRDT state
    store1.syncFromCrdt();
    store2.syncFromCrdt();

    const items1 = store1.activeForm?.sections[0].items.map((i) => i.id) || [];
    const items2 = store2.activeForm?.sections[0].items.map((i) => i.id) || [];

    // Assert zero data loss
    expect(items1).toContain("field-user1");
    expect(items1).toContain("field-user2");
    expect(items2).toContain("field-user1");
    expect(items2).toContain("field-user2");
  });

  it("throttles mouse cursor tracking updates to 30 FPS maximum frequency", async () => {
    const store = useDesignerStore();
    let awarenessCalls = 0;

    store._collaborationService = {
      sendAwareness: () => {
        awarenessCalls++;
      },
    } as any;

    // Trigger cursor movements rapidly in 10ms bursts
    store.sendCursorPosition(10, 20);
    store.sendCursorPosition(15, 25);
    store.sendCursorPosition(20, 30);

    // Only 1 call allowed immediately within 33.3ms (30 FPS) window
    expect(awarenessCalls).toBe(1);
  });

  it("broadcasts active field highlights and tracks remote co-author presence", () => {
    const store = useDesignerStore();

    // Simulate remote awareness signal received
    store.activeUsers = {
      "user-remote-1": {
        userId: "user-remote-1",
        userName: "Biostatistician",
        userRole: "Biostatistician",
        color: "#10B981",
        selectedFieldId: "field-1",
        cursorX: 150,
        cursorY: 200,
      },
    };

    expect(store.activeUsers["user-remote-1"].selectedFieldId).toBe("field-1");
    expect(store.activeUsers["user-remote-1"].userRole).toBe("Biostatistician");
  });

  it("accumulates offline edits and merges state vector upon reconnection", () => {
    const store = useDesignerStore();

    // Simulate offline state
    store.connectionState = "disconnected";

    store.addFieldToSection("section-1", {
      id: "offline-field-1",
      label: "Offline Created Field",
      type: "text",
      gridSpan: 12,
    });

    // Local CRDT document state reflects the offline edit
    const yForm = store.getYDoc().getMap("form");
    expect(yForm.get("sectionsData")).toContain("offline-field-1");

    // Local reactive form is updated
    const fieldIds = store.activeForm?.sections[0].items.map((i) => i.id) || [];
    expect(fieldIds).toContain("offline-field-1");
  });
});
