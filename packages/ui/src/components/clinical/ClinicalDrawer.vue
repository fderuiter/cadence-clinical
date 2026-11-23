<script setup>
import {
  computed,
  onBeforeUnmount,
  onMounted,
  ref,
  useAttrs,
  useId,
  useSlots,
  watch,
} from "vue";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { OptionalTeleport } from "./OptionalTeleport.js";
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
  overlayClass: {
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
  showClose: {
    type: Boolean,
    default: true,
  },
  dockable: {
    type: Boolean,
    default: false,
  },
  dockMinWidth: {
    type: Number,
    default: 1280,
  },
  modal: {
    type: Boolean,
    default: undefined,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:isOpen",
  "update:open",
  "close",
]);

const attrs = useAttrs();
const slots = useSlots();
const drawerRef = ref(null);
const instanceId = useId();
const headingId = computed(
  () => props.ariaLabelledby || `clinical-drawer-title-${instanceId}`
);

const checkWideDesktop = () => {
  if (typeof window === "undefined") return false;
  if (typeof window.innerWidth === "number" && window.innerWidth > 0) {
    return window.innerWidth >= props.dockMinWidth;
  }
  if (typeof window.matchMedia === "function") {
    return Boolean(
      window.matchMedia(`(min-width: ${props.dockMinWidth}px)`)?.matches
    );
  }
  return false;
};

const isWideDesktop = ref(checkWideDesktop());
let mediaQueryList = null;

const syncWideDesktop = () => {
  isWideDesktop.value = checkWideDesktop();
};

const bindMediaQuery = () => {
  if (mediaQueryList) {
    mediaQueryList.removeEventListener?.("change", syncWideDesktop);
    mediaQueryList = null;
  }
  if (
    typeof window !== "undefined" &&
    typeof window.matchMedia === "function"
  ) {
    mediaQueryList =
      window.matchMedia(`(min-width: ${props.dockMinWidth}px)`) || null;
    mediaQueryList?.addEventListener?.("change", syncWideDesktop);
  }
  syncWideDesktop();
};

onMounted(() => {
  bindMediaQuery();
  if (
    typeof window !== "undefined" &&
    typeof window.addEventListener === "function"
  ) {
    window.addEventListener("resize", syncWideDesktop);
  }
});

onBeforeUnmount(() => {
  if (mediaQueryList) {
    mediaQueryList.removeEventListener?.("change", syncWideDesktop);
    mediaQueryList = null;
  }
  if (
    typeof window !== "undefined" &&
    typeof window.removeEventListener === "function"
  ) {
    window.removeEventListener("resize", syncWideDesktop);
  }
});

watch(
  () => props.dockMinWidth,
  () => bindMediaQuery()
);

const drawerOpen = computed(() => {
  if (props.modelValue !== undefined) return props.modelValue;
  if (props.open !== undefined) return props.open;
  return props.isOpen;
});

watch(drawerOpen, (open) => {
  if (open) syncWideDesktop();
});

const shouldRender = computed(() => props.keepMounted || drawerOpen.value);

const isDocked = computed(() => Boolean(props.dockable && isWideDesktop.value));

const isModal = computed(() => {
  if (props.modal !== undefined) return Boolean(props.modal);
  if (attrs["aria-modal"] === "true") return true;
  if (attrs["aria-modal"] === "false") return false;
  return !isDocked.value;
});

const computedAriaModal = computed(() =>
  drawerOpen.value && isModal.value ? "true" : undefined
);

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
  if (!computedLabelledby.value) return "Clinical Drawer";
  return undefined;
});

const computedDescribedby = computed(() => {
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
  if (!isModal.value || !props.dismissible || !props.closeOnBackdrop) return;
  closeDrawer();
};

useFocusTrap(drawerRef, {
  isOpen: drawerOpen,
  modal: isModal,
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

defineExpose({
  drawerEl: drawerRef,
  isDocked,
  close: closeDrawer,
});
</script>

<template>
  <OptionalTeleport :to="teleportTarget" :enabled="Boolean(teleport)">
    <div
      v-if="shouldRender"
      :id="overlayId"
      :class="[
        'clinical-drawer-backdrop',
        `position-${position}`,
        {
          'is-docked': isDocked && drawerOpen,
          'is-closed': keepMounted && !drawerOpen,
          active: drawerOpen,
          'mobile-bottom-sheet': position === 'bottom' || mobileBottomSheet,
        },
        overlayClass,
      ]"
      :inert="keepMounted && !drawerOpen ? '' : undefined"
      :aria-hidden="keepMounted && !drawerOpen ? 'true' : undefined"
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
          {
            'is-docked': isDocked && drawerOpen,
            'is-closed': keepMounted && !drawerOpen,
            active: drawerOpen,
            open: drawerOpen,
          },
          position === 'bottom' || mobileBottomSheet
            ? 'drawer-mobile-bottom-sheet'
            : 'drawer-panel-mobile-full',
          panelClass,
        ]"
        :role="role"
        :aria-modal="computedAriaModal"
        :aria-hidden="keepMounted && !drawerOpen ? 'true' : undefined"
        :inert="keepMounted && !drawerOpen ? '' : undefined"
        :aria-labelledby="computedLabelledby"
        :aria-label="computedAriaLabel"
        :aria-describedby="computedDescribedby"
        tabindex="-1"
      >
        <div
          v-if="position === 'bottom' || mobileBottomSheet"
          class="clinical-drawer-sheet-handle"
          aria-hidden="true"
        ></div>
        <template v-if="showHeader">
          <header
            v-if="$slots.header && headerClass"
            :class="['clinical-drawer-header', headerClass]"
          >
            <slot name="header" :close="closeDrawer" :title-id="headingId" />
          </header>
          <slot
            v-else-if="$slots.header"
            name="header"
            :close="closeDrawer"
            :title-id="headingId"
          />
          <header v-else :class="['clinical-drawer-header', headerClass]">
            <div class="clinical-drawer-titles">
              <h2 :id="headingId" class="clinical-drawer-title">
                {{ title }}
              </h2>
              <p v-if="subtitle" class="clinical-drawer-subtitle">
                {{ subtitle }}
              </p>
            </div>
            <button
              v-if="showClose"
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
                focusable="false"
              >
                <path d="M18 6 6 18" />
                <path d="m6 6 12 12" />
              </svg>
            </button>
          </header>
        </template>

        <slot name="subheader" />

        <div :class="['clinical-drawer-body', bodyClass]">
          <slot />
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
  height: 100vh;
  height: 100dvh;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
  box-sizing: border-box;
  overflow-x: hidden;
}

.clinical-drawer-backdrop.position-left {
  justify-content: flex-start;
}

.clinical-drawer-backdrop.position-bottom {
  justify-content: center;
  align-items: flex-end;
}

.clinical-drawer-backdrop.is-closed {
  opacity: 0;
  visibility: hidden;
  pointer-events: none;
}

.clinical-drawer-backdrop.is-docked {
  left: auto;
  right: 0;
  width: auto;
  background: transparent;
  backdrop-filter: none;
  pointer-events: none;
}

.clinical-drawer-backdrop.is-docked.position-left {
  left: 0;
  right: auto;
}

.clinical-drawer-panel {
  height: 100vh;
  height: 100dvh;
  max-width: 100vw;
  min-width: 0;
  box-sizing: border-box;
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: -8px 0 24px -4px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  overflow-x: hidden;
  pointer-events: auto;
  animation: clinical-drawer-slide-in 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

.clinical-drawer-panel.is-closed {
  visibility: hidden;
  pointer-events: none;
  animation: none;
}

.clinical-drawer-panel.is-docked {
  box-shadow: none;
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

.clinical-drawer-panel.position-left.is-docked {
  box-shadow: none;
}

.clinical-drawer-panel.position-bottom {
  width: min(100vw, 680px);
  height: auto;
  max-height: 90vh;
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
  flex-shrink: 0;
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
  overflow-wrap: anywhere;
}

.clinical-drawer-subtitle {
  margin: 4px 0 0 0;
  font-size: var(--font-size-xs, 12px);
  color: var(--color-text-muted);
}

.clinical-drawer-close-btn {
  flex-shrink: 0;
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
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  padding: 20px 24px;
  overflow-x: hidden;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.clinical-drawer-footer {
  flex-shrink: 0;
  padding: 16px 24px;
  border-top: 1px solid var(--color-border);
  background: var(--color-surface-muted);
}

@media (max-width: 1279px) {
  .clinical-drawer-close-btn,
  .clinical-drawer-panel :deep(.btn-close),
  .clinical-drawer-panel :deep(.btn-close-drawer) {
    min-width: 44px;
    min-height: 44px;
  }

  .clinical-drawer-footer :deep(.btn) {
    min-height: 44px;
  }
}

@media (max-width: 1024px), (pointer: coarse) {
  .clinical-drawer-panel
    :deep(
      input:not([type="checkbox"]):not([type="radio"]):not([type="range"]):not(
          [type="file"]
        )
    ),
  .clinical-drawer-panel :deep(select),
  .clinical-drawer-panel :deep(textarea) {
    font-size: max(1rem, 16px);
  }
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

@media (max-width: 480px) {
  .clinical-drawer-backdrop {
    align-items: flex-end;
    justify-content: stretch;
  }

  .clinical-drawer-panel,
  .clinical-drawer-panel.position-left {
    width: 100%;
    max-width: 100%;
    height: auto;
    max-height: 90vh;
    max-height: 90dvh;
    border-left: none;
    border-right: none;
    border-top: 1px solid var(--color-border);
    border-radius: var(--radius-xl, 16px) var(--radius-xl, 16px) 0 0;
    box-shadow: 0 -8px 24px -4px rgba(0, 0, 0, 0.16);
    padding-bottom: env(safe-area-inset-bottom, 0px);
    animation-name: clinical-drawer-sheet-up;
  }

  .clinical-drawer-footer {
    padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px));
  }
}
</style>
