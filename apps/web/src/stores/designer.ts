/**
 * useDesignerStore Pinia Store
 *
 * Manages the client-side state for the eCRF designer canvas, including
 * the active form schema, CRDT Y.Doc state synchronization, real-time presence/awareness signals,
 * viewport layout simulation mode, and compiler quality gating rules.
 */
import { defineStore } from "pinia";
import * as Y from "yjs";
import {
  EcrfCollaborationService,
  AwarenessState,
  ConnectionState,
  SnapshotSaveResult,
} from "../services/EcrfCollaborationService.js";

export interface DesignerField {
  id: string;
  label: string;
  type: string;
  cdash?: string;
  gridSpan: number;
  required?: boolean;
  [key: string]: any;
}

export interface DesignerSection {
  id: string;
  name: string;
  isCollapsed?: boolean;
  items: DesignerField[];
  [key: string]: any;
}

export interface DesignerForm {
  id: string;
  name: string;
  sections: DesignerSection[];
  layoutJustification?: string;
  [key: string]: any;
}

export interface DesignerState {
  activeForm: DesignerForm | null;
  selectedFieldId: string | null;
  viewport: string;
  dismissedWarnings: boolean;
  activeUsers: Record<string, AwarenessState>;
  currentUser: AwarenessState;
  connectionState: ConnectionState;
  lastSnapshotResult: SnapshotSaveResult | null;
}

// Throttle helper for 30 FPS (33.3ms) cursor tracking
let lastCursorSentTime = 0;
const THROTTLE_INTERVAL_MS = 33;

export const useDesignerStore = defineStore("designer", {
  state: (): DesignerState => ({
    activeForm: {
      id: "form-1",
      name: "eCRF Draft Form",
      sections: [
        {
          id: "section-1",
          name: "Demographics",
          isCollapsed: false,
          items: [
            {
              id: "field-1",
              label: "Subject Initials",
              type: "text",
              cdash: "DM.SUBJINIT",
              gridSpan: 6,
              required: true,
            },
            {
              id: "field-2",
              label: "Date of Birth",
              type: "date",
              cdash: "DM.BRTHDT",
              gridSpan: 6,
              required: true,
            },
          ],
        },
      ],
      layoutJustification: "",
    },
    selectedFieldId: null,
    viewport: "desktop",
    dismissedWarnings: false,
    activeUsers: {},
    currentUser: {
      userName: "Clinical Data Manager",
      userRole: "Data Manager",
      color: "#2563EB",
    },
    connectionState: "disconnected",
    lastSnapshotResult: null,
  }),
  actions: {
    // Non-reactive references
    _ydoc: null as Y.Doc | null,
    _collaborationService: null as EcrfCollaborationService | null,

    getYDoc(): Y.Doc {
      if (!this._ydoc) {
        this._ydoc = new Y.Doc();
        this.syncToCrdt();
      }
      return this._ydoc;
    },

    syncToCrdt() {
      const ydoc = this.getYDoc();
      if (!this.activeForm) return;

      ydoc.transact(() => {
        const yForm = ydoc.getMap("form");
        yForm.set("id", this.activeForm!.id);
        yForm.set("name", this.activeForm!.name);
        yForm.set("layoutJustification", this.activeForm!.layoutJustification || "");
        yForm.set("sectionsData", JSON.stringify(this.activeForm!.sections));

        const yItems = ydoc.getMap("items");
        for (const section of this.activeForm!.sections) {
          for (const item of section.items || []) {
            yItems.set(item.id, JSON.stringify(item));
          }
        }
      }, "local");
    },

    syncFromCrdt() {
      const ydoc = this.getYDoc();
      const yForm = ydoc.getMap("form");
      const sectionsData = yForm.get("sectionsData") as string;
      const id = yForm.get("id") as string;
      const name = yForm.get("name") as string;
      const layoutJustification = yForm.get("layoutJustification") as string;

      if (sectionsData) {
        try {
          const sections: DesignerSection[] = JSON.parse(sectionsData);
          const yItems = ydoc.getMap("items");
          if (yItems.size > 0 && sections.length > 0) {
            const knownItemIds = new Set(sections.flatMap((s) => (s.items || []).map((i) => i.id)));
            const extraItems: DesignerField[] = [];
            yItems.forEach((itemJson, itemId) => {
              if (!knownItemIds.has(itemId as string)) {
                try {
                  extraItems.push(JSON.parse(itemJson as string));
                } catch {
                  // Ignore invalid JSON
                }
              }
            });
            if (extraItems.length > 0) {
              sections[0].items = [...(sections[0].items || []), ...extraItems];
            }
          }

          this.activeForm = {
            id: id || this.activeForm?.id || "form-1",
            name: name || this.activeForm?.name || "eCRF Draft Form",
            sections,
            layoutJustification: layoutJustification ?? this.activeForm?.layoutJustification ?? "",
          };
        } catch (err) {
          console.error("Failed to parse sectionsData from Y.Doc", err);
        }
      }
    },

    async initCollaboration(formId?: string, userOverlay?: Partial<AwarenessState>, hubUrl?: string) {
      const targetFormId = formId || this.activeForm?.id || "form-1";

      if (userOverlay) {
        this.currentUser = { ...this.currentUser, ...userOverlay };
      }

      const ydoc = this.getYDoc();

      if (!this._collaborationService) {
        this._collaborationService = new EcrfCollaborationService(hubUrl || "/hubs/ecrf-collaboration");

        this._collaborationService.onUpdateReceived = (update) => {
          Y.applyUpdate(ydoc, update, "remote");
        };

        this._collaborationService.onStateChunksReceived = (chunks) => {
          chunks.forEach((chunk) => {
            Y.applyUpdate(ydoc, chunk, "remote");
          });
        };

        this._collaborationService.onAwarenessReceived = (userId, state) => {
          this.activeUsers[userId] = state;
        };

        this._collaborationService.onActiveUsersReceived = (users) => {
          const newMap: Record<string, AwarenessState> = {};
          users.forEach((u) => {
            if (u.userId) {
              newMap[u.userId] = u;
            }
          });
          this.activeUsers = newMap;
        };

        this._collaborationService.onUserLeft = (connectionId) => {
          delete this.activeUsers[connectionId];
        };

        this._collaborationService.onSnapshotSaved = (result) => {
          this.lastSnapshotResult = result;
        };

        this._collaborationService.onConnectionStateChanged = (state) => {
          this.connectionState = state;
        };

        ydoc.on("update", (update: Uint8Array, origin: any) => {
          if (origin === "remote") {
            this.syncFromCrdt();
          } else {
            this._collaborationService?.sendUpdate(update);
          }
        });
      }

      await this._collaborationService.connect();
      await this._collaborationService.joinSession(targetFormId);
    },

    sendCursorPosition(x: number, y: number, sectionId?: string | null, fieldId?: string | null) {
      const now = Date.now();
      if (now - lastCursorSentTime < THROTTLE_INTERVAL_MS) {
        return;
      }
      lastCursorSentTime = now;

      const awarenessState: AwarenessState = {
        ...this.currentUser,
        cursorX: Math.round(x),
        cursorY: Math.round(y),
        activeSectionId: sectionId !== undefined ? sectionId : null,
        selectedFieldId: fieldId !== undefined ? fieldId : this.selectedFieldId,
        timestamp: now,
      };

      this._collaborationService?.sendAwareness(awarenessState);
    },

    sendActiveFieldHighlight(fieldId: string | null, sectionId?: string | null) {
      this.selectedFieldId = fieldId;
      const now = Date.now();
      const awarenessState: AwarenessState = {
        ...this.currentUser,
        selectedFieldId: fieldId,
        activeSectionId: sectionId !== undefined ? sectionId : null,
        timestamp: now,
      };
      this._collaborationService?.sendAwareness(awarenessState);
    },

    async saveSnapshotToServer() {
      if (!this.activeForm) return;
      const usdmJson = JSON.stringify({
        study: {
          id: this.activeForm.id,
          name: this.activeForm.name,
          version: "1.0",
        },
        items: this.activeForm.sections.flatMap((s) => s.items || []),
      });

      if (this._collaborationService) {
        await this._collaborationService.saveSnapshot(usdmJson);
      }
    },

    setViewport(viewport: string) {
      this.viewport = viewport;
    },
    setDismissedWarnings(dismissed: boolean) {
      this.dismissedWarnings = dismissed;
    },
    setLayoutJustification(justification: string) {
      if (this.activeForm) {
        this.activeForm.layoutJustification = justification;
        this.syncToCrdt();
      }
    },
    setSelectedFieldId(id: string | null) {
      this.selectedFieldId = id;
      this.sendActiveFieldHighlight(id);
    },
    updateActiveForm(form: DesignerForm) {
      this.activeForm = form;
      this.syncToCrdt();
    },
    updateSections(sections: DesignerSection[]) {
      if (this.activeForm) {
        this.activeForm.sections = sections;
        this.syncToCrdt();
      }
    },
    addFieldToSection(sectionId: string, field: DesignerField) {
      if (!this.activeForm) return;
      const section = this.activeForm.sections.find((s) => s.id === sectionId);
      if (section) {
        if (!section.items) {
          section.items = [];
        }
        section.items.push(field);
        this.syncToCrdt();
      }
    },
    deleteField(fieldId: string) {
      if (!this.activeForm) return;
      for (const section of this.activeForm.sections) {
        const idx = section.items.findIndex((item) => item.id === fieldId);
        if (idx !== -1) {
          section.items.splice(idx, 1);
          if (this.selectedFieldId === fieldId) {
            this.selectedFieldId = null;
          }
          this.syncToCrdt();
          break;
        }
      }
    },
    duplicateField(fieldId: string) {
      if (!this.activeForm) return;
      for (const section of this.activeForm.sections) {
        const idx = section.items.findIndex((item) => item.id === fieldId);
        if (idx !== -1) {
          const original = section.items[idx];
          const newId = `${original.id}-copy-${Date.now()}`;
          const copy: DesignerField = {
            ...original,
            id: newId,
            label: `${original.label} (Copy)`,
          };
          section.items.splice(idx + 1, 0, copy);
          this.selectedFieldId = newId;
          this.syncToCrdt();
          break;
        }
      }
    },
  },
});
