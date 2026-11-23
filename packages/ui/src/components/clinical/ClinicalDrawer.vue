<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { X } from "lucide-vue-next";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { useWorkspaceTier } from "../../composables/useWorkspaceTier.js";
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
    validator: (val) => ["right", "left", "bottom"].includes(val),
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
const { isMobile } = useWorkspaceTier();

const headingId = computed(
  () => props.ariaLabelledby || `clinical-drawer-title-${instanceId}`
);

const drawerOpen = computed(() => {
  if (props.modelValue !== undefined) return props.modelValue;
  if (props.open !== undefined) return props.open;
  return props.isOpen;
});

const effectivePosition = computed(() => {
  if (props.position === "bottom") return "bottom";
  if (props.mobileBottomSheet && isMobile.value) return "bottom";
  return props.position === "left" ? "left" : "right";
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
  restoreFocus: props.restoreFocus,
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
        `position-${effectivePosition}`,
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
          `size-${size}`,
          `position-${effectivePosition}`,
          panelClass,
        ]"
        :role="role"
        aria-modal="true"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        tabindex="-1"
      >
        <div
          v-if="effectivePosition === 'bottom'"
          class="clinical-drawer-grabber"
          aria-hidden="true"
        >
          <span class="clinical-drawer-grabber-bar"></span>
        </div>

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
              <X :size="18" :stroke-width="1.75" aria-hidden="true" />
            </button>
          </header>
        </slot>

        <slot name="subheader" />

        <div :class="['clinical-drawer-body', bodyClass]">
          <slot :close="closeDrawer" />
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
  height: 100dvh;
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
  height: 100dvh;
  max-width: 100vw;
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: -8px 0 24px -4px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  outline: none;
  animation: clinical-drawer-slide-in 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

.clinical-drawer-panel.position-left {
  border-left: none;
  border-right: 1px solid var(--color-border);
  box-shadow: 8px 0 24px -4px rgba(0, 0, 0, 0.12);
  animation-name: clinical-drawer-slide-in-left;
}

.clinical-drawer-panel.position-bottom {
  width: 100vw;
  max-width: 100vw;
  height: auto;
  max-height: var(--drawer-bottom-max-height, 90dvh);
  border-left: none;
  border-right: none;
  border-top: 1px solid var(--color-border);
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  box-shadow: 0 -10px 25px -5px rgba(15, 23, 42, 0.2);
  padding-bottom: env(safe-area-inset-bottom, 0px);
  animation-name: clinical-drawer-slide-up;
}

.clinical-drawer-grabber {
  display: flex;
  justify-content: center;
  padding: 8px 0 2px;
  background: var(--color-surface-muted);
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  flex-shrink: 0;
}

.clinical-drawer-grabber-bar {
  width: 36px;
  height: 4px;
  border-radius: var(--radius-full);
  background: var(--color-border-control);
  opacity: 0.6;
}

.size-sm {
  width: min(100vw, 360px);
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

.clinical-drawer-panel.position-bottom.size-sm,
.clinical-drawer-panel.position-bottom.size-md,
.clinical-drawer-panel.position-bottom.size-lg,
.clinical-drawer-panel.position-bottom.size-xl {
  width: 100vw;
  max-width: 100vw;
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

@keyframes clinical-drawer-slide-up {
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
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 20px;
  border-bottom: 1px solid var(--color-border);
  background: var(--color-surface-muted);
  flex-shrink: 0;
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
  background: transparent;
  border: 1px solid transparent;
  color: var(--color-text-muted);
  cursor: pointer;
  min-width: var(--touch-hit-area-min, 44px);
  min-height: var(--touch-hit-area-min, 44px);
  padding: 6px;
  border-radius: var(--radius-sm);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
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
  min-height: 0;
  padding: 16px 20px;
  overflow-y: auto;
}

.clinical-drawer-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--color-border);
  background: var(--color-surface-muted);
  flex-shrink: 0;
}

@media (max-width: 640px) {
  .clinical-drawer-panel:not(.position-bottom) {
    width: 100vw;
    max-width: 100vw;
  }

  .clinical-drawer-header,
  .clinical-drawer-body,
  .clinical-drawer-footer {
    padding-left: 16px;
    padding-right: 16px;
  }
}
</style>
