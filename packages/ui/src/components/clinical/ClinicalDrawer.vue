<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { clinicalOverlayProps } from "./sharedProps.js";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: undefined,
  },
  isOpen: {
    type: Boolean,
    default: false,
  },
  open: {
    type: Boolean,
    default: undefined,
  },
  title: {
    type: String,
    default: "",
  },
  subtitle: {
    type: String,
    default: "",
  },
  ariaLabel: {
    type: String,
    default: undefined,
  },
  ariaLabelledby: {
    type: String,
    default: undefined,
  },
  id: {
    type: String,
    default: undefined,
  },
  overlayId: {
    type: String,
    default: undefined,
  },
  overlayClass: {
    type: [String, Array, Object],
    default: "",
  },
  panelClass: {
    type: [String, Array, Object],
    default: "",
  },
  bodyClass: {
    type: [String, Array, Object],
    default: "",
  },
  position: {
    type: String,
    default: "right",
  },
  mobileBottomSheet: {
    type: Boolean,
    default: false,
  },
  role: {
    type: String,
    default: "dialog",
  },
  ...clinicalOverlayProps,
  closeLabel: {
    type: String,
    default: "Close drawer",
  },
  showHeader: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["update:modelValue", "update:isOpen", "close"]);

const attrs = useAttrs();
const slots = useSlots();
const drawerRef = ref(null);
const instanceId = useId();
const headingId = computed(
  () => props.ariaLabelledby || `clinical-drawer-title-${instanceId}`
);

const drawerOpen = computed(() => {
  if (props.modelValue !== undefined) return props.modelValue;
  if (props.open !== undefined) return props.open;
  return props.isOpen;
});

const teleportTarget = computed(() =>
  typeof props.teleport === "string" ? props.teleport : "body"
);

const computedLabelledby = computed(() => {
  if (props.ariaLabelledby) return props.ariaLabelledby;
  if (props.ariaLabel || attrs["aria-label"]) return undefined;
  if (props.title || slots.header) return headingId.value;
  return undefined;
});

const computedAriaLabel = computed(() => {
  if (props.ariaLabel) return props.ariaLabel;
  if (attrs["aria-label"]) return String(attrs["aria-label"]);
  if (!computedLabelledby.value) return "Clinical Drawer";
  return undefined;
});

const closeDrawer = () => {
  if (!props.dismissible) return;
  emit("update:modelValue", false);
  emit("update:isOpen", false);
  emit("close");
};

const onBackdropClick = () => {
  if (!props.dismissible || !props.closeOnBackdrop) return;
  closeDrawer();
};

useFocusTrap(drawerRef, {
  isOpen: drawerOpen,
  escapeDeactivates: () => props.dismissible,
  initialFocus: () => props.initialFocus,
  restoreFocus:
    typeof props.restoreFocus === "boolean"
      ? props.restoreFocus
      : () =>
          typeof props.restoreFocus === "function"
            ? props.restoreFocus()
            : props.restoreFocus,
  fallbackFocus: () =>
    typeof props.fallbackFocus === "function"
      ? props.fallbackFocus()
      : props.fallbackFocus,
  onEscape: closeDrawer,
});
</script>

<template>
  <Teleport :to="teleportTarget" :disabled="!teleport">
    <div
      v-if="drawerOpen"
      :id="overlayId"
      :class="[
        'clinical-drawer-backdrop',
        `position-${position}`,
        { 'mobile-bottom-sheet': position === 'bottom' || mobileBottomSheet },
        overlayClass,
      ]"
      role="presentation"
      @click.self="onBackdropClick"
    >
      <aside
        :id="id"
        ref="drawerRef"
        v-bind="attrs"
        :class="[
          'clinical-drawer-panel',
          position === 'bottom' ? 'drawer-bottom-sheet' : `size-${size}`,
          `position-${position}`,
          position === 'bottom' || mobileBottomSheet
            ? 'drawer-mobile-bottom-sheet'
            : 'drawer-panel-mobile-full',
          panelClass,
        ]"
        :role="role"
        aria-modal="true"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        tabindex="-1"
      >
        <div
          v-if="position === 'bottom' || mobileBottomSheet"
          class="clinical-drawer-sheet-handle"
          aria-hidden="true"
        ></div>
        <slot
          v-if="showHeader"
          name="header"
          :close="closeDrawer"
          :title-id="headingId"
        >
          <header class="clinical-drawer-header">
            <div class="clinical-drawer-titles">
              <h2 :id="headingId" class="clinical-drawer-title">
                {{ title }}
              </h2>
              <p v-if="subtitle" class="clinical-drawer-subtitle">
                {{ subtitle }}
              </p>
            </div>
            <button
              type="button"
              class="clinical-drawer-close-btn btn-close"
              :aria-label="closeLabel"
              :disabled="!dismissible"
              @click="closeDrawer"
            >
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.75"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="M18 6 6 18" />
                <path d="m6 6 12 12" />
              </svg>
            </button>
          </header>
        </slot>

        <slot name="subheader" />

        <div :class="['clinical-drawer-body', bodyClass]">
          <slot />
        </div>

        <footer v-if="$slots.footer" class="clinical-drawer-footer">
          <slot name="footer" :close="closeDrawer" />
        </footer>
      </aside>

      <slot name="after-panel" />
    </div>
  </Teleport>
</template>

<style scoped>
.clinical-drawer-backdrop {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
}

.clinical-drawer-backdrop.position-left {
  justify-content: flex-start;
}

.clinical-drawer-backdrop.position-bottom {
  justify-content: center;
  align-items: flex-end;
}

.clinical-drawer-panel {
  height: 100vh;
  max-width: 100vw;
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: -8px 0 24px -4px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  animation: clinical-drawer-slide-in 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

.clinical-drawer-panel:focus:not(:focus-visible) {
  outline: none;
}

.clinical-drawer-panel.position-left {
  border-left: none;
  border-right: 1px solid var(--color-border);
  box-shadow: 8px 0 24px -4px rgba(0, 0, 0, 0.12);
  animation-name: clinical-drawer-slide-in-left;
}

.clinical-drawer-panel.position-bottom {
  width: min(100vw, 680px);
  height: auto;
  max-height: 90dvh;
  border-left: none;
  border-top: 1px solid var(--color-border);
  border-radius: var(--radius-xl, 16px) var(--radius-xl, 16px) 0 0;
  box-shadow: 0 -8px 24px -4px rgba(0, 0, 0, 0.16);
  animation-name: clinical-drawer-slide-in-bottom;
  padding-bottom: env(safe-area-inset-bottom, 0px);
}

.clinical-drawer-sheet-handle {
  width: 40px;
  height: 4px;
  border-radius: var(--radius-full, 9999px);
  background: var(--color-border-control);
  margin: 10px auto 2px;
  flex-shrink: 0;
}

.size-sm {
  width: min(100vw, 400px);
}

.size-md {
  width: min(100vw, 480px);
}

.size-lg {
  width: var(
    --semantic-drawer-width-lg,
    var(--drawer-width-lg, min(100vw, 640px))
  );
}

.size-xl {
  width: min(100vw, 820px);
}

@keyframes clinical-drawer-slide-in {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

@keyframes clinical-drawer-slide-in-left {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: translateX(0);
  }
}

@keyframes clinical-drawer-slide-in-bottom {
  from {
    transform: translateY(100%);
  }
  to {
    transform: translateY(0);
  }
}

@media (prefers-reduced-motion: reduce) {
  .clinical-drawer-backdrop,
  .clinical-drawer-panel,
  .clinical-drawer-close-btn {
    animation: none;
    transition: none;
  }
}

.clinical-drawer-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 18px 24px;
  border-bottom: 1px solid var(--color-border);
  background: var(--color-surface-muted);
}

.clinical-drawer-titles {
  flex: 1;
  min-width: 0;
}

.clinical-drawer-title {
  margin: 0;
  font-size: var(--font-size-md, 16px);
  font-weight: 600;
  color: var(--color-text);
}

.clinical-drawer-subtitle {
  margin: 4px 0 0 0;
  font-size: var(--font-size-xs, 12px);
  color: var(--color-text-muted);
}

.clinical-drawer-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 36px;
  min-height: 36px;
  background: transparent;
  border: none;
  font-size: 16px;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 6px;
  line-height: 1.2;
}

.position-bottom .clinical-drawer-close-btn,
.drawer-mobile-bottom-sheet .clinical-drawer-close-btn {
  min-width: var(--touch-target-min, 48px);
  min-height: var(--touch-target-min, 48px);
}

.clinical-drawer-close-btn:hover:not(:disabled) {
  background: var(--color-border);
  color: var(--color-text);
}

.clinical-drawer-close-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.clinical-drawer-body {
  flex: 1;
  padding: 20px 24px;
  overflow-y: auto;
}

.clinical-drawer-footer {
  padding: 16px 24px;
  border-top: 1px solid var(--color-border);
  background: var(--color-surface-muted);
}

@media (max-width: 640px) {
  .clinical-drawer-backdrop.mobile-bottom-sheet {
    justify-content: center;
    align-items: flex-end;
  }

  .clinical-drawer-panel {
    width: 100vw;
    max-width: 100vw;
  }

  .clinical-drawer-panel.drawer-mobile-bottom-sheet {
    height: auto;
    max-height: 90dvh;
    border-left: none;
    border-right: none;
    border-top: 1px solid var(--color-border);
    border-radius: var(--radius-xl, 16px) var(--radius-xl, 16px) 0 0;
    animation-name: clinical-drawer-slide-in-bottom;
    padding-bottom: env(safe-area-inset-bottom, 0px);
  }

  .clinical-drawer-close-btn {
    min-width: var(--touch-target-min, 48px);
    min-height: var(--touch-target-min, 48px);
  }

  .clinical-drawer-header,
  .clinical-drawer-body,
  .clinical-drawer-footer {
    padding-left: 16px;
    padding-right: 16px;
  }

  .clinical-drawer-footer {
    padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px));
  }
}
</style>
