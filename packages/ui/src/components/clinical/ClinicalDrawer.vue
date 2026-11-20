<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";

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
  size: {
    type: String,
    default: "md",
  },
  role: {
    type: String,
    default: "dialog",
  },
  position: {
    type: String,
    default: "right",
  },
  dismissible: {
    type: Boolean,
    default: true,
  },
  closeOnBackdrop: {
    type: Boolean,
    default: true,
  },
  teleport: {
    type: [Boolean, String],
    default: "body",
  },
  initialFocus: {
    type: [String, Object, Function],
    default: null,
  },
  restoreFocus: {
    type: [Boolean, Function],
    default: true,
  },
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
        `position-${position}`,
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
          `position-${position}`,
          panelClass,
        ]"
        :role="role"
        aria-modal="true"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        tabindex="-1"
      >
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
              ✕
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

.clinical-drawer-panel {
  height: 100vh;
  max-width: 100vw;
  background: var(--color-surface, #ffffff);
  border-left: 1px solid var(--color-border, #e2e8f0);
  box-shadow: -8px 0 24px -4px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  outline: none;
  animation: clinical-drawer-slide-in 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

.clinical-drawer-panel.position-left {
  border-left: none;
  border-right: 1px solid var(--color-border, #e2e8f0);
  box-shadow: 8px 0 24px -4px rgba(0, 0, 0, 0.12);
  animation-name: clinical-drawer-slide-in-left;
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

@media (prefers-reduced-motion: reduce) {
  .clinical-drawer-panel {
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
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface-muted, #f8fafc);
}

.clinical-drawer-titles {
  flex: 1;
  min-width: 0;
}

.clinical-drawer-title {
  margin: 0;
  font-size: var(--font-size-md, 16px);
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.clinical-drawer-subtitle {
  margin: 4px 0 0 0;
  font-size: var(--font-size-xs, 12px);
  color: var(--color-text-muted, #475569);
}

.clinical-drawer-close-btn {
  background: transparent;
  border: none;
  font-size: 16px;
  color: var(--color-text-muted, #475569);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
  line-height: 1.2;
}

.clinical-drawer-close-btn:hover:not(:disabled) {
  background: #e2e8f0;
  color: var(--color-text, #0f172a);
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
  border-top: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface-muted, #f8fafc);
}

@media (max-width: 640px) {
  .clinical-drawer-panel {
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
