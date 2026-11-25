<script setup>
import {
  Teleport,
  computed,
  h,
  nextTick,
  onBeforeUnmount,
  ref,
  unref,
  watch,
} from "vue";
import { useEscapeClose } from "../../composables/useEscapeClose.js";
import {
  DEFAULT_VIEWPORT_MARGIN,
  useFloatingPosition,
} from "../../composables/useFloatingPosition.js";

const MaybeTeleport = (tpProps, { slots: tpSlots }) => {
  const target =
    typeof tpProps.to === "string" && tpProps.to.length > 0
      ? tpProps.to
      : tpProps.to === true
        ? "body"
        : null;
  return !target
    ? tpSlots.default?.()
    : h(Teleport, { to: target }, tpSlots.default?.());
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
    default: undefined,
  },
  open: {
    type: Boolean,
    default: undefined,
  },
  anchor: {
    type: [Object, Function],
    default: null,
  },
  placement: {
    type: String,
    default: "bottom",
  },
  align: {
    type: String,
    default: "start",
  },
  offset: {
    type: Number,
    default: 6,
  },
  viewportMargin: {
    type: Number,
    default: DEFAULT_VIEWPORT_MARGIN,
  },
  preferredWidth: {
    type: Number,
    default: null,
  },
  minHeight: {
    type: Number,
    default: 96,
  },
  flip: {
    type: Boolean,
    default: true,
  },
  teleport: {
    type: [Boolean, String],
    default: false,
  },
  id: {
    type: String,
    default: undefined,
  },
  role: {
    type: String,
    default: undefined,
  },
  ariaLabel: {
    type: String,
    default: undefined,
  },
  ariaLabelledby: {
    type: String,
    default: undefined,
  },
  closeOnEscape: {
    type: Boolean,
    default: true,
  },
  closeOnOutsidePointer: {
    type: Boolean,
    default: true,
  },
  restoreFocusOnEscape: {
    type: Boolean,
    default: true,
  },
  hoverBridge: {
    type: Boolean,
    default: false,
  },
  popoverClass: {
    type: [String, Array, Object],
    default: "",
  },
});

const emit = defineEmits([
  "update:modelValue",
  "close",
  "mouseenter",
  "mouseleave",
  "focusin",
  "focusout",
]);

const popoverRef = ref(null);
const scrollRef = ref(null);

const resolvedOpen = computed(() => {
  if (props.modelValue !== undefined) return Boolean(props.modelValue);
  if (props.isOpen !== undefined) return Boolean(props.isOpen);
  if (props.open !== undefined) return Boolean(props.open);
  return false;
});

function resolveAnchorElement() {
  const raw =
    typeof props.anchor === "function" ? props.anchor() : unref(props.anchor);
  if (!raw) return null;
  if (typeof Element !== "undefined" && raw instanceof Element) {
    return raw;
  }
  if (raw.$el && typeof Element !== "undefined" && raw.$el instanceof Element) {
    return raw.$el;
  }
  if (typeof raw.getBoundingClientRect === "function") {
    return raw;
  }
  return null;
}

const {
  floatingStyle,
  placement: activePlacement,
  updatePosition,
} = useFloatingPosition(resolveAnchorElement, popoverRef, {
  isOpen: resolvedOpen,
  placement: () => props.placement,
  align: () => props.align,
  offset: () => props.offset,
  margin: () => props.viewportMargin,
  flip: () => props.flip,
  preferredWidth: () => props.preferredWidth,
  minHeight: () => props.minHeight,
});

function requestClose(reason) {
  if (!resolvedOpen.value) return;
  const activeEl =
    typeof document !== "undefined" ? document.activeElement : null;
  const focusInsidePopover = Boolean(
    activeEl &&
    popoverRef.value &&
    typeof popoverRef.value.contains === "function" &&
    popoverRef.value.contains(activeEl)
  );

  emit("update:modelValue", false);
  emit("close", { reason });

  if (reason === "escape" && props.restoreFocusOnEscape && focusInsidePopover) {
    const anchorEl = resolveAnchorElement();
    if (anchorEl && typeof anchorEl.focus === "function") {
      nextTick(() => {
        anchorEl.focus();
      });
    }
  }
}

useEscapeClose(
  () => {
    requestClose("escape");
  },
  {
    isOpen: () => resolvedOpen.value && props.closeOnEscape,
  }
);

function isTargetInsidePopoverOrAnchor(target) {
  if (!target) return false;
  const popoverEl = popoverRef.value;
  if (
    popoverEl &&
    typeof popoverEl.contains === "function" &&
    popoverEl.contains(target)
  ) {
    return true;
  }
  const anchorEl = resolveAnchorElement();
  if (
    anchorEl &&
    typeof anchorEl.contains === "function" &&
    anchorEl.contains(target)
  ) {
    return true;
  }
  return false;
}

function handleDocumentOutsideEvent(event) {
  if (!resolvedOpen.value || !props.closeOnOutsidePointer) return;
  if (isTargetInsidePopoverOrAnchor(event?.target)) return;
  requestClose("outside");
}

let outsideListenersAttached = false;

function attachOutsideListeners() {
  if (outsideListenersAttached || typeof document === "undefined") return;
  outsideListenersAttached = true;
  document.addEventListener("pointerdown", handleDocumentOutsideEvent);
  document.addEventListener("click", handleDocumentOutsideEvent);
}

function detachOutsideListeners() {
  if (!outsideListenersAttached || typeof document === "undefined") return;
  outsideListenersAttached = false;
  document.removeEventListener("pointerdown", handleDocumentOutsideEvent);
  document.removeEventListener("click", handleDocumentOutsideEvent);
}

watch(
  () => resolvedOpen.value && props.closeOnOutsidePointer,
  (shouldListen) => {
    if (shouldListen) {
      attachOutsideListeners();
    } else {
      detachOutsideListeners();
    }
  },
  { immediate: true, flush: "post" }
);

const onPopoverMouseEnter = (event) => emit("mouseenter", event);
const onPopoverMouseLeave = (event) => emit("mouseleave", event);
const onPopoverFocusIn = (event) => emit("focusin", event);
const onPopoverFocusOut = (event) => emit("focusout", event);

watch(
  popoverRef,
  (nextEl, prevEl) => {
    if (prevEl && typeof prevEl.removeEventListener === "function") {
      prevEl.removeEventListener("mouseenter", onPopoverMouseEnter);
      prevEl.removeEventListener("mouseleave", onPopoverMouseLeave);
      prevEl.removeEventListener("focusin", onPopoverFocusIn);
      prevEl.removeEventListener("focusout", onPopoverFocusOut);
    }
    if (nextEl && typeof nextEl.addEventListener === "function") {
      nextEl.addEventListener("mouseenter", onPopoverMouseEnter);
      nextEl.addEventListener("mouseleave", onPopoverMouseLeave);
      nextEl.addEventListener("focusin", onPopoverFocusIn);
      nextEl.addEventListener("focusout", onPopoverFocusOut);
    }
  },
  { flush: "post" }
);

onBeforeUnmount(() => {
  if (
    popoverRef.value &&
    typeof popoverRef.value.removeEventListener === "function"
  ) {
    popoverRef.value.removeEventListener("mouseenter", onPopoverMouseEnter);
    popoverRef.value.removeEventListener("mouseleave", onPopoverMouseLeave);
    popoverRef.value.removeEventListener("focusin", onPopoverFocusIn);
    popoverRef.value.removeEventListener("focusout", onPopoverFocusOut);
  }
  detachOutsideListeners();
});

defineExpose({
  popoverRef,
  scrollRef,
  placement: activePlacement,
  updatePosition,
});
</script>

<template>
  <MaybeTeleport v-if="resolvedOpen" :to="teleport">
    <div
      :id="id"
      ref="popoverRef"
      v-bind="$attrs"
      class="clinical-popover"
      :class="[
        popoverClass,
        {
          'clinical-popover--hover-bridge': hoverBridge,
          'clinical-popover--scroll-host': !hoverBridge,
        },
      ]"
      :data-placement="activePlacement"
      :role="role"
      :aria-label="ariaLabel"
      :aria-labelledby="ariaLabelledby"
      :style="floatingStyle"
    >
      <div v-if="hoverBridge" ref="scrollRef" class="clinical-popover-scroll">
        <slot />
      </div>
      <slot v-else />
    </div>
  </MaybeTeleport>
</template>

<style scoped>
.clinical-popover {
  position: fixed;
  z-index: 1050;
  box-sizing: border-box;
  max-width: calc(100vw - 24px);
  max-height: calc(100dvh - 24px);
}

.clinical-popover--scroll-host {
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.clinical-popover--hover-bridge {
  display: flex;
  flex-direction: column;
  overflow: visible;
  pointer-events: auto;
}

.clinical-popover--hover-bridge::before {
  content: "";
  position: absolute;
  left: 0;
  right: 0;
  height: 10px;
  pointer-events: auto;
}

.clinical-popover--hover-bridge[data-placement="bottom"]::before {
  top: -10px;
}

.clinical-popover--hover-bridge[data-placement="top"]::before {
  bottom: -10px;
}

.clinical-popover-scroll {
  flex: 1 1 auto;
  min-height: 0;
  width: 100%;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  box-sizing: border-box;
}
</style>
