<script setup>
import { computed, ref, useAttrs, useId, useSlots } from "vue";
import { X } from "lucide-vue-next";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import {
  MaybeTeleport,
  buildClinicalOverlayFocusTrapOptions,
  clinicalOverlayOpenProps,
  clinicalOverlayProps,
  resolveClinicalOverlayAria,
  resolveClinicalOverlayOpen,
  resolveClinicalTeleportTarget,
  validateClinicalOverlayRole,
} from "./sharedProps.js";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  ...clinicalOverlayOpenProps,
  role: {
    type: String,
    default: "dialog",
    validator: validateClinicalOverlayRole,
  },
  keepMounted: {
    type: Boolean,
    default: false,
  },
  title: {
    type: String,
    default: "",
  },
  subtitle: {
    type: String,
    default: "",
  },
  overlayClass: {
    type: [String, Array, Object],
    default: "",
  },
  panelClass: {
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
  ...clinicalOverlayProps,
  closeLabel: {
    type: String,
    default: "Close drawer",
  },
  docked: {
    type: Boolean,
    default: false,
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

const drawerOpen = computed(() => resolveClinicalOverlayOpen(props));
const teleportTarget = computed(() => resolveClinicalTeleportTarget(props));

const overlayAria = computed(() =>
  resolveClinicalOverlayAria({
    props,
    attrs,
    headingId: headingId.value,
    canUseHeading: Boolean(props.title || slots.header),
    fallbackLabel: "Clinical Drawer",
  })
);
const computedLabelledby = computed(() => overlayAria.value.labelledby);
const computedAriaLabel = computed(() => overlayAria.value.ariaLabel);

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

const panelTag = computed(() =>
  props.role === "dialog" || props.role === "alertdialog" ? "div" : "aside"
);

const modalStateAttrs = computed(() =>
  props.keepMounted && !drawerOpen.value
    ? { "aria-modal": undefined, "aria-hidden": "true", inert: "" }
    : props.docked
      ? { "aria-modal": undefined }
      : {}
);

const trapActive = computed(() => drawerOpen.value && !props.docked);

useFocusTrap(
  drawerRef,
  buildClinicalOverlayFocusTrapOptions(props, {
    isOpen: trapActive,
    onEscape: closeDrawer,
  })
);

defineExpose({
  drawerRef,
});
</script>

<template>
  <MaybeTeleport :to="teleportTarget" :disabled="!teleport">
    <div
      v-if="drawerOpen || keepMounted"
      :id="overlayId"
      :class="[
        'clinical-drawer-backdrop',
        `position-${position}`,
        {
          'is-docked': docked,
          'is-closed': !drawerOpen,
          'mobile-bottom-sheet': position === 'bottom' || mobileBottomSheet,
        },
        overlayClass,
      ]"
      role="presentation"
      :aria-hidden="!drawerOpen ? 'true' : undefined"
      :inert="!drawerOpen || undefined"
      @click.self="onBackdropClick"
    >
      <component
        :is="panelTag"
        :id="id"
        ref="drawerRef"
        :class="[
          'clinical-drawer-panel',
          position === 'bottom' ? 'drawer-bottom-sheet' : `size-${size}`,
          `position-${position}`,
          { 'is-docked': docked, open: drawerOpen, 'is-closed': !drawerOpen },
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
        v-bind="{ ...attrs, ...modalStateAttrs }"
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
              <X
                :size="16"
                :stroke-width="1.75"
                aria-hidden="true"
                focusable="false"
              />
            </button>
          </header>
        </slot>

        <div :class="['clinical-drawer-body', bodyClass]">
          <slot />
        </div>

        <footer v-if="$slots.footer" class="clinical-drawer-footer">
          <slot name="footer" :close="closeDrawer" />
        </footer>
      </component>

      <slot name="after-panel" />
    </div>
  </MaybeTeleport>
</template>

<style scoped>
.clinical-drawer-backdrop {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100vh;
  height: 100dvh;
  box-sizing: border-box;
  background: var(--semantic-color-backdrop-soft);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
}

.clinical-drawer-backdrop.is-closed {
  display: none;
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
  max-height: 100vh;
  max-height: 100dvh;
  max-width: 100vw;
  min-width: 0;
  box-sizing: border-box;
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: var(--shadow-drawer-left);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  animation: clinical-drawer-slide-in 0.22s cubic-bezier(0.16, 1, 0.3, 1);
}

.clinical-drawer-panel:focus:not(:focus-visible) {
  outline: none;
}

.clinical-drawer-panel.position-left {
  border-left: none;
  border-right: 1px solid var(--color-border);
  box-shadow: var(--shadow-drawer-right);
  animation-name: clinical-drawer-slide-in-left;
}

.clinical-drawer-panel.position-bottom {
  width: min(100vw, 680px);
  height: auto;
  max-height: 90dvh;
  border-left: none;
  border-top: 1px solid var(--color-border);
  border-radius: var(--radius-xl, 16px) var(--radius-xl, 16px) 0 0;
  box-shadow: var(--shadow-drawer-bottom);
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

.drawer-mobile-bottom-sheet:not(.position-bottom)
  .clinical-drawer-sheet-handle {
  display: none;
}

.size-sm {
  width: var(
    --semantic-drawer-width-sm,
    var(--drawer-width-sm, min(100vw, 440px))
  );
}

.size-md {
  width: var(
    --semantic-drawer-width-md,
    var(--drawer-width-md, min(100vw, 480px))
  );
}

.size-lg {
  width: var(
    --semantic-drawer-width-lg,
    var(--drawer-width-lg, min(100vw, 640px))
  );
}

.size-xl {
  width: var(
    --semantic-drawer-width-xl,
    var(--drawer-width-xl, min(100vw, 920px))
  );
}

@keyframes clinical-drawer-slide-in {
  from {
    transform: translateX(100%);
  }
  to {
    transform: none;
  }
}

@keyframes clinical-drawer-slide-in-left {
  from {
    transform: translateX(-100%);
  }
  to {
    transform: none;
  }
}

@keyframes clinical-drawer-slide-in-bottom {
  from {
    transform: translateY(100%);
  }
  to {
    transform: none;
  }
}

@keyframes clinical-drawer-slide-up {
  from {
    transform: translateY(100%);
  }
  to {
    transform: none;
  }
}

.clinical-drawer-header {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 20px;
  min-height: 48px;
  box-sizing: border-box;
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
  line-height: 1.25;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.clinical-drawer-subtitle {
  margin: 2px 0 0 0;
  font-size: var(--font-size-xs, 12px);
  color: var(--color-text-muted);
  line-height: 1.25;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.clinical-drawer-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  min-width: 44px;
  min-height: 44px;
  background: transparent;
  border: none;
  font-size: 16px;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 6px;
  border-radius: var(--radius-md, 6px);
  line-height: 1;
  box-sizing: border-box;
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
  padding: 16px 24px;
  overflow-y: auto;
  overflow-x: hidden;
  overscroll-behavior: contain;
  -webkit-overflow-scrolling: touch;
  box-sizing: border-box;
}

.clinical-drawer-footer {
  flex-shrink: 0;
  padding: 12px 24px;
  border-top: 1px solid var(--color-border);
  background: var(--color-surface-muted);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
  flex-wrap: wrap;
  box-sizing: border-box;
}

@media (max-width: 1024px), (pointer: coarse) {
  .clinical-drawer-panel.clinical-drawer-panel
    :deep(
      input:not([type="checkbox"]):not([type="radio"]):not([type="range"]):not(
          [type="file"]
        )
    ),
  .clinical-drawer-panel.clinical-drawer-panel :deep(select),
  .clinical-drawer-panel.clinical-drawer-panel :deep(textarea) {
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
    box-shadow: var(--shadow-drawer-bottom);
    animation-name: clinical-drawer-slide-in-bottom;
    padding-bottom: env(safe-area-inset-bottom, 0px);
  }

  .drawer-mobile-bottom-sheet:not(.position-bottom)
    .clinical-drawer-sheet-handle {
    display: block;
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
  .clinical-drawer-backdrop,
  .clinical-drawer-backdrop.position-left {
    align-items: flex-end;
    justify-content: stretch;
  }

  .clinical-drawer-panel,
  .clinical-drawer-panel.position-left {
    width: 100vw;
    max-width: 100vw;
    height: 90vh;
    height: 90dvh;
    max-height: 90vh;
    max-height: 90dvh;
    border-left: none;
    border-right: none;
    border-top: 1px solid var(--color-border);
    border-radius: var(--radius-lg, 12px) var(--radius-lg, 12px) 0 0;
    box-shadow: var(--shadow-drawer-bottom);
    padding-bottom: env(safe-area-inset-bottom, 0px);
    animation-name: clinical-drawer-slide-up;
  }
}

@media (max-height: 540px) {
  .clinical-drawer-panel {
    overflow-y: auto;
    overscroll-behavior: contain;
    -webkit-overflow-scrolling: touch;
  }

  .clinical-drawer-header,
  .clinical-drawer-panel.clinical-drawer-panel > :deep(header:first-child),
  .clinical-drawer-panel.clinical-drawer-panel
    > :deep(.drawer-header:first-child),
  .clinical-drawer-panel.clinical-drawer-panel
    > :deep(.drawer-header-bar:first-child) {
    padding-top: 4px;
    padding-bottom: 4px;
    min-height: 44px;
    max-height: 52px;
    box-sizing: border-box;
    align-items: center;
    overflow: hidden;
  }

  .clinical-drawer-subtitle {
    display: none;
  }

  .clinical-drawer-body {
    flex: 1 0 auto;
    min-height: 140px;
    overflow-y: visible;
    padding-top: 12px;
    padding-bottom: 12px;
  }

  .clinical-drawer-footer {
    padding-top: 8px;
    padding-bottom: 8px;
  }
}

@media (min-width: 1280px) {
  .clinical-drawer-backdrop.is-docked {
    background: transparent;
    backdrop-filter: none;
    pointer-events: none;
  }

  .clinical-drawer-panel.is-docked {
    pointer-events: auto;
  }
}

@media (prefers-reduced-motion: reduce) {
  .clinical-drawer-backdrop,
  .clinical-drawer-panel,
  .clinical-drawer-panel.position-left,
  .clinical-drawer-panel.position-bottom,
  .clinical-drawer-close-btn {
    animation: none;
    transition: none;
  }
}
</style>
