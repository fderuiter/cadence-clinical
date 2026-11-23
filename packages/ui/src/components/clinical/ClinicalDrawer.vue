<script setup>
import { X } from "lucide-vue-next";
import {
  Teleport,
  computed,
  h,
  onBeforeUnmount,
  onMounted,
  ref,
  useAttrs,
  useId,
  useSlots,
  watch,
} from "vue";
import {
  isDesktopDockedViewport,
  useFocusTrap,
} from "../../composables/useFocusTrap.js";
import { clinicalOverlayProps } from "./sharedProps.js";

const OptionalTeleport = (tpProps, { slots: tpSlots }) => {
  const children = tpSlots.default?.() || [];
  return tpProps.enabled
    ? h(Teleport, { to: tpProps.to }, children)
    : (children[0] ?? null);
};

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
  ariaDescribedby: {
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
  closeButtonId: {
    type: String,
    default: undefined,
  },
  overlayClass: {
    type: [String, Array, Object],
    default: "",
  },
  backdropClass: {
    type: [String, Array, Object],
    default: "",
  },
  panelClass: {
    type: [String, Array, Object],
    default: "",
  },
  headerClass: {
    type: [String, Array, Object],
    default: "",
  },
  bodyClass: {
    type: [String, Array, Object],
    default: "",
  },
  footerClass: {
    type: [String, Array, Object],
    default: "",
  },
  position: {
    type: String,
    default: "right",
  },
  desktopMode: {
    type: String,
    default: "overlay",
    validator: (val) => ["overlay", "docked"].includes(val),
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
  showCloseButton: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:isOpen",
  "update:open",
  "open",
  "close",
]);

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

const isDesktopViewport = ref(isDesktopDockedViewport());
let desktopMql = null;

const syncDesktopViewport = () => {
  isDesktopViewport.value = isDesktopDockedViewport();
};

onMounted(() => {
  syncDesktopViewport();
  if (typeof window !== "undefined") {
    window.addEventListener("resize", syncDesktopViewport);
    if (typeof window.matchMedia === "function") {
      desktopMql = window.matchMedia("(min-width: 1280px)");
      if (desktopMql && typeof desktopMql.addEventListener === "function") {
        desktopMql.addEventListener("change", syncDesktopViewport);
      }
    }
  }
});

onBeforeUnmount(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("resize", syncDesktopViewport);
  }
  if (desktopMql && typeof desktopMql.removeEventListener === "function") {
    desktopMql.removeEventListener("change", syncDesktopViewport);
  }
  desktopMql = null;
});

// Emits aria-modal="true" when open in modal overlay mode, and omits aria-modal when closed or docked at 1280px+ (UX-3).
const isDockedDesktop = computed(
  () => props.desktopMode === "docked" && isDesktopViewport.value
);

watch(drawerOpen, (open, prevOpen) => {
  syncDesktopViewport();
  if (open && !prevOpen) {
    emit("open");
  }
});

const teleportTarget = computed(() =>
  typeof props.teleport === "string" ? props.teleport : "body"
);

const computedLabelledby = computed(() => {
  if (props.ariaLabelledby) return props.ariaLabelledby;
  if (props.ariaLabel || attrs["aria-label"]) return undefined;
  if (props.showHeader && (props.title || slots.header)) return headingId.value;
  return undefined;
});

const computedAriaLabel = computed(() => {
  if (props.ariaLabel) return props.ariaLabel;
  if (attrs["aria-label"]) return String(attrs["aria-label"]);
  if (!computedLabelledby.value) return props.title || "Clinical Drawer";
  return undefined;
});

const computedAriaDescribedby = computed(() => {
  if (props.ariaDescribedby) return props.ariaDescribedby;
  if (attrs["aria-describedby"]) return String(attrs["aria-describedby"]);
  return undefined;
});

const closeDrawer = () => {
  if (!props.dismissible) return;
  emit("update:modelValue", false);
  emit("update:isOpen", false);
  emit("update:open", false);
  emit("close");
};

const onBackdropClick = () => {
  if (isDockedDesktop.value) return;
  if (!props.dismissible || !props.closeOnBackdrop) return;
  closeDrawer();
};

useFocusTrap(drawerRef, {
  isOpen: drawerOpen,
  desktopMode: () => props.desktopMode,
  modal: () => !isDockedDesktop.value,
  escapeDeactivates: () => props.dismissible,
  initialFocus: () => props.initialFocus,
  restoreFocus: props.restoreFocus,
  onEscape: closeDrawer,
});
</script>

<template>
  <OptionalTeleport :enabled="Boolean(teleport)" :to="teleportTarget">
    <div
      v-if="keepMounted || drawerOpen"
      :id="overlayId"
      :class="[
        'clinical-drawer-backdrop',
        `position-${position}`,
        `desktop-mode-${desktopMode}`,
        {
          active: drawerOpen,
          'is-closed': !drawerOpen,
          'is-docked-desktop': isDockedDesktop,
        },
        overlayClass,
        backdropClass,
      ]"
      :inert="!drawerOpen || undefined"
      :aria-hidden="!drawerOpen ? 'true' : undefined"
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
          `desktop-mode-${desktopMode}`,
          {
            active: drawerOpen,
            'is-closed': !drawerOpen,
            'is-docked-desktop': isDockedDesktop,
          },
          panelClass,
        ]"
        :role="role"
        :aria-modal="drawerOpen && !isDockedDesktop ? 'true' : undefined"
        :aria-hidden="!drawerOpen ? 'true' : undefined"
        :inert="!drawerOpen || undefined"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        :aria-describedby="computedAriaDescribedby"
        tabindex="-1"
      >
        <slot
          v-if="showHeader"
          name="header"
          :close="closeDrawer"
          :title-id="headingId"
        >
          <header :class="['clinical-drawer-header', headerClass]">
            <div class="clinical-drawer-titles">
              <h2 :id="headingId" class="clinical-drawer-title">
                {{ title }}
              </h2>
              <p v-if="subtitle" class="clinical-drawer-subtitle">
                {{ subtitle }}
              </p>
            </div>
            <button
              v-if="showCloseButton"
              :id="closeButtonId"
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
          <slot :close="closeDrawer" :title-id="headingId" />
        </div>

        <footer
          v-if="$slots.footer"
          :class="['clinical-drawer-footer', footerClass]"
        >
          <slot name="footer" :close="closeDrawer" />
        </footer>
      </aside>

      <slot name="after-panel" />
    </div>
  </OptionalTeleport>
</template>

<style scoped>
.clinical-drawer-backdrop {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: var(--overlay-viewport-height-fallback, 100vh);
  height: var(--overlay-viewport-height, 100dvh);
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
  box-sizing: border-box;
}

.clinical-drawer-backdrop.is-closed {
  pointer-events: none;
  visibility: hidden;
  opacity: 0;
}

.clinical-drawer-backdrop.position-left {
  justify-content: flex-start;
}

.clinical-drawer-panel {
  height: var(--overlay-viewport-height-fallback, 100vh);
  height: var(--overlay-viewport-height, 100dvh);
  max-width: 100vw;
  min-height: 0;
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: -8px 0 24px -4px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  outline: none;
  box-sizing: border-box;
  padding-top: var(--safe-area-inset-top, env(safe-area-inset-top, 0px));
  padding-bottom: var(
    --safe-area-inset-bottom,
    env(safe-area-inset-bottom, 0px)
  );
  animation: clinical-drawer-slide-in 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

.clinical-drawer-panel.is-closed {
  pointer-events: none;
  visibility: hidden;
  animation: none;
}

.clinical-drawer-panel.position-left {
  border-left: none;
  border-right: 1px solid var(--color-border);
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

.size-full {
  width: min(100vw, 1080px);
}

/* UX-3 Desktop Docking (1280px+): non-modal inspector leaving workspace interactive */
@media (min-width: 1280px) {
  .clinical-drawer-backdrop.desktop-mode-docked {
    pointer-events: none;
    background: transparent;
    backdrop-filter: none;
    z-index: 900;
  }

  .clinical-drawer-panel.desktop-mode-docked {
    pointer-events: auto;
    box-shadow: -4px 0 16px -4px rgba(15, 23, 42, 0.08);
  }

  .clinical-drawer-panel.desktop-mode-docked.position-left {
    box-shadow: 4px 0 16px -4px rgba(15, 23, 42, 0.08);
  }
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

@keyframes clinical-drawer-sheet-up {
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
  padding: 16px 24px;
  flex-shrink: 0;
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
  min-width: var(--touch-target-staff-min, 44px);
  min-height: var(--touch-target-staff-min, 44px);
  background: transparent;
  border: none;
  font-size: 16px;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 6px;
  border-radius: 4px;
  line-height: 1.2;
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
  flex: 1 1 auto;
  min-height: 0;
  padding: 20px 24px;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
  overscroll-behavior: contain;
}

.clinical-drawer-footer {
  padding: 14px 24px;
  flex-shrink: 0;
  border-top: 1px solid var(--color-border);
  background: var(--color-surface-muted);
}

/* Tablet narrow slide-over (481px to 640px): full width 100dvh */
@media (min-width: 481px) and (max-width: 640px) {
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

/* Mobile (320px to 480px): 90dvh safe-area bottom sheet */
@media (max-width: 480px) {
  .clinical-drawer-backdrop,
  .clinical-drawer-backdrop.position-left {
    align-items: flex-end;
    justify-content: stretch;
    padding: var(--safe-area-inset-top, env(safe-area-inset-top, 0px)) 0 0 0;
  }

  .clinical-drawer-panel,
  .clinical-drawer-panel.position-left {
    width: 100%;
    max-width: 100%;
    height: auto;
    max-height: var(--overlay-sheet-max-height-fallback, 90vh);
    max-height: var(--overlay-sheet-max-height, 90dvh);
    border-left: none;
    border-right: none;
    border-top: 1px solid var(--color-border);
    border-radius: var(--radius-xl, 16px) var(--radius-xl, 16px) 0 0;
    box-shadow: 0 -8px 24px -4px rgba(0, 0, 0, 0.16);
    padding-top: 0;
    padding-bottom: var(
      --safe-area-inset-bottom,
      env(safe-area-inset-bottom, 0px)
    );
    animation-name: clinical-drawer-sheet-up;
  }

  .clinical-drawer-header,
  .clinical-drawer-body,
  .clinical-drawer-footer {
    padding-left: 16px;
    padding-right: 16px;
  }
}
</style>
