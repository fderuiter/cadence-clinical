<template>
  <div
    v-if="show"
    id="ecrf-hotkey-overlay"
    ref="modalRef"
    class="modal-overlay"
    style="display: flex; z-index: 9999"
    role="dialog"
    aria-modal="true"
    aria-labelledby="hotkey-overlay-title"
  >
    <div
      class="modal"
      style="
        max-width: 640px;
        width: 90%;
        border-radius: 12px;
        box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
        padding: 0;
        overflow: hidden;
      "
    >
      <div
        class="modal-header"
        style="
          background-color: var(--primary, #1e293b);
          color: white;
          padding: 16px 20px;
          display: flex;
          justify-content: space-between;
          align-items: center;
        "
      >
        <div
          id="hotkey-overlay-title"
          style="
            font-size: 1.15rem;
            font-weight: 700;
            display: flex;
            align-items: center;
            gap: 8px;
          "
        >
          <span>⌨️</span> eCRF Keyboard Shortcuts & Hotkeys
        </div>
        <button
          type="button"
          class="btn-close-hotkeys"
          style="
            background: transparent;
            border: none;
            color: white;
            font-size: 1.5rem;
            cursor: pointer;
            padding: 0 8px;
            line-height: 1;
          "
          aria-label="Close keyboard shortcuts"
          @click="onClose"
        >
          ×
        </button>
      </div>

      <div
        class="modal-body"
        style="
          padding: 20px;
          max-height: 70vh;
          overflow-y: auto;
          display: flex;
          flex-direction: column;
          gap: 20px;
        "
      >
        <!-- Section 1: Data Entry & Navigation -->
        <div>
          <h4
            style="
              font-size: 0.9rem;
              text-transform: uppercase;
              letter-spacing: 0.05em;
              color: var(--primary, #3b82f6);
              margin-bottom: 12px;
              border-bottom: 2px solid #e2e8f0;
              padding-bottom: 4px;
            "
          >
            📋 Clinical Data Transcription
          </h4>
          <div
            style="
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
              gap: 10px;
            "
          >
            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Advance to next visible field</span
              >
              <kbd
                style="
                  background: #ffffff;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  padding: 2px 8px;
                  font-family: monospace;
                  font-size: 0.8rem;
                  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                "
                >Enter</kbd
              >
            </div>

            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Save &amp; Submit eCRF form</span
              >
              <div style="display: flex; gap: 4px">
                <kbd
                  style="
                    background: #ffffff;
                    border: 1px solid #cbd5e1;
                    border-radius: 4px;
                    padding: 2px 6px;
                    font-family: monospace;
                    font-size: 0.8rem;
                    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                  "
                  >Ctrl+S</kbd
                >
                <kbd
                  style="
                    background: #ffffff;
                    border: 1px solid #cbd5e1;
                    border-radius: 4px;
                    padding: 2px 6px;
                    font-family: monospace;
                    font-size: 0.8rem;
                    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                  "
                  >Cmd+S</kbd
                >
              </div>
            </div>
          </div>
        </div>

        <!-- Section 2: Discrepancy & Query Management -->
        <div>
          <h4
            style="
              font-size: 0.9rem;
              text-transform: uppercase;
              letter-spacing: 0.05em;
              color: var(--primary, #3b82f6);
              margin-bottom: 12px;
              border-bottom: 2px solid #e2e8f0;
              padding-bottom: 4px;
            "
          >
            💬 Query Management &amp; Discrepancies
          </h4>
          <div
            style="
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
              gap: 10px;
            "
          >
            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Toggle query creation / drawer</span
              >
              <kbd
                style="
                  background: #ffffff;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  padding: 2px 6px;
                  font-family: monospace;
                  font-size: 0.8rem;
                  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                "
                >Alt+Q</kbd
              >
            </div>

            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Jump focus to next active query</span
              >
              <kbd
                style="
                  background: #ffffff;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  padding: 2px 6px;
                  font-family: monospace;
                  font-size: 0.8rem;
                  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                "
                >Alt+N</kbd
              >
            </div>

            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Jump focus to previous query</span
              >
              <kbd
                style="
                  background: #ffffff;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  padding: 2px 6px;
                  font-family: monospace;
                  font-size: 0.8rem;
                  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                "
                >Alt+P</kbd
              >
            </div>
          </div>
        </div>

        <!-- Section 3: Monitoring & Verification (CRA Roles) -->
        <div>
          <h4
            style="
              font-size: 0.9rem;
              text-transform: uppercase;
              letter-spacing: 0.05em;
              color: var(--primary, #3b82f6);
              margin-bottom: 12px;
              border-bottom: 2px solid #e2e8f0;
              padding-bottom: 4px;
            "
          >
            🔎 CRA Monitor Verification
          </h4>
          <div
            style="
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
              gap: 10px;
            "
          >
            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f0fdf4;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px dashed #bbf7d0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 600; color: #166534"
                >Toggle SDV status (CRA Monitors)</span
              >
              <kbd
                style="
                  background: #ffffff;
                  border: 1px solid #86efac;
                  border-radius: 4px;
                  padding: 2px 6px;
                  font-family: monospace;
                  font-size: 0.8rem;
                  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                "
                >Alt+V</kbd
              >
            </div>
          </div>
        </div>

        <!-- Section 4: General Overlay Controls -->
        <div>
          <h4
            style="
              font-size: 0.9rem;
              text-transform: uppercase;
              letter-spacing: 0.05em;
              color: var(--primary, #3b82f6);
              margin-bottom: 12px;
              border-bottom: 2px solid #e2e8f0;
              padding-bottom: 4px;
            "
          >
            ❓ Help &amp; Overlay Controls
          </h4>
          <div
            style="
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
              gap: 10px;
            "
          >
            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Toggle hotkey cheat sheet</span
              >
              <div style="display: flex; gap: 4px">
                <kbd
                  style="
                    background: #ffffff;
                    border: 1px solid #cbd5e1;
                    border-radius: 4px;
                    padding: 2px 6px;
                    font-family: monospace;
                    font-size: 0.8rem;
                    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                  "
                  >?</kbd
                >
                <kbd
                  style="
                    background: #ffffff;
                    border: 1px solid #cbd5e1;
                    border-radius: 4px;
                    padding: 2px 6px;
                    font-family: monospace;
                    font-size: 0.8rem;
                    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                  "
                  >Ctrl+/</kbd
                >
              </div>
            </div>

            <div
              class="shortcut-row"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 6px;
                border: 1px solid #e2e8f0;
              "
            >
              <span style="font-size: 0.85rem; font-weight: 500"
                >Close modal overlay</span
              >
              <kbd
                style="
                  background: #ffffff;
                  border: 1px solid #cbd5e1;
                  border-radius: 4px;
                  padding: 2px 6px;
                  font-family: monospace;
                  font-size: 0.8rem;
                  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
                "
                >Esc</kbd
              >
            </div>
          </div>
        </div>
      </div>

      <div
        class="modal-footer"
        style="
          padding: 12px 20px;
          background-color: #f8fafc;
          border-top: 1px solid #e2e8f0;
          display: flex;
          justify-content: flex-end;
        "
      >
        <button
          type="button"
          class="btn btn-primary"
          style="
            padding: 8px 16px;
            font-size: 0.85rem;
            font-weight: 600;
            border-radius: 6px;
            cursor: pointer;
          "
          @click="onClose"
        >
          Close (Esc)
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useFocusTrap } from "../composables/useFocusTrap";
import { useEscapeClose } from "../composables/useEscapeClose";

defineProps({
  show: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["close"]);

const modalRef = ref(null);
useFocusTrap(modalRef);
useEscapeClose(() => emit("close"));

function onClose() {
  emit("close");
}
</script>
