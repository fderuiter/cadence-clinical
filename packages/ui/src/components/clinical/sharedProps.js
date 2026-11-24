import { computed } from "vue";

export const clinicalOverlayProps = {
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
  role: {
    type: String,
    default: "dialog",
  },
  showHeader: {
    type: Boolean,
    default: true,
  },
  showClose: {
    type: Boolean,
    default: true,
  },
  size: {
    type: String,
    default: "md",
  },
  dismissible: {
    type: Boolean,
    default: true,
  },
  closeOnBackdrop: {
    type: Boolean,
    default: true,
  },
  keepMounted: {
    type: Boolean,
    default: false,
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
    type: [Boolean, String, Object, Function],
    default: true,
  },
  fallbackFocus: {
    type: [String, Object, Function],
    default: null,
  },
};

export function useClinicalOverlayState({
  props,
  attrs,
  slots,
  emit,
  headingId,
  defaultAriaLabel,
  resolveModal,
}) {
  const isOpen = computed(() => {
    if (props.modelValue !== undefined) return props.modelValue;
    if (props.open !== undefined) return props.open;
    return props.isOpen;
  });

  const shouldRender = computed(() => props.keepMounted || isOpen.value);

  const isModal = computed(() => {
    if (attrs["aria-modal"] === "false") return false;
    if (attrs["aria-modal"] === "true") return true;
    return resolveModal ? resolveModal() : Boolean(props.modal);
  });

  const computedAriaModal = computed(() =>
    isOpen.value && isModal.value ? "true" : undefined
  );

  const teleportTarget = computed(() =>
    typeof props.teleport === "string" ? props.teleport : "body"
  );

  const computedLabelledby = computed(() => {
    if (props.ariaLabelledby) return props.ariaLabelledby;
    if (props.ariaLabel || attrs["aria-label"]) return undefined;
    if (props.showHeader && (props.title || slots.header)) {
      return headingId.value;
    }
    return undefined;
  });

  const computedAriaLabel = computed(() => {
    if (props.ariaLabel) return props.ariaLabel;
    if (attrs["aria-label"]) return String(attrs["aria-label"]);
    if (!computedLabelledby.value) {
      return typeof defaultAriaLabel === "function"
        ? defaultAriaLabel()
        : defaultAriaLabel;
    }
    return undefined;
  });

  const computedDescribedby = computed(() => {
    if (props.ariaDescribedby) return props.ariaDescribedby;
    if (attrs["aria-describedby"]) return String(attrs["aria-describedby"]);
    return undefined;
  });

  const closeOverlay = () => {
    if (!props.dismissible) return;
    emit("update:modelValue", false);
    emit("update:isOpen", false);
    emit("update:open", false);
    emit("close");
  };

  const onBackdropClick = () => {
    if (!isModal.value || !props.dismissible || !props.closeOnBackdrop) return;
    closeOverlay();
  };

  const trapOptions = {
    isOpen,
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
    onEscape: closeOverlay,
  };

  return {
    isOpen,
    shouldRender,
    isModal,
    computedAriaModal,
    teleportTarget,
    computedLabelledby,
    computedAriaLabel,
    computedDescribedby,
    closeOverlay,
    onBackdropClick,
    trapOptions,
  };
}

export const clinicalFieldBaseProps = {
  id: {
    type: String,
    required: true,
  },
  label: {
    type: String,
    required: true,
  },
  query: {
    type: Object,
    default: null,
  },
  gridSpan: {
    type: [Number, String],
    default: 12,
  },
  error: {
    type: String,
    default: null,
  },
  attributes: {
    type: Object,
    default: () => ({}),
  },
  canManageQueries: {
    type: Boolean,
    default: false,
  },
  queryLabel: {
    type: String,
    default: "",
  },
};
