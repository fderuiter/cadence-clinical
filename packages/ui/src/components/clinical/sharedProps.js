import { Teleport, h } from "vue";

export const MaybeTeleport = (tpProps, { slots: tpSlots }) =>
  tpProps.disabled
    ? tpSlots.default?.()
    : h(Teleport, { to: tpProps.to }, tpSlots.default?.());

export const clinicalOverlayOpenProps = {
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
  id: {
    type: String,
    default: undefined,
  },
  overlayId: {
    type: String,
    default: undefined,
  },
  bodyClass: {
    type: [String, Array, Object],
    default: "",
  },
  showHeader: {
    type: Boolean,
    default: true,
  },
};

export const validateClinicalOverlayRole = (val) =>
  ["dialog", "alertdialog"].includes(val);

export const clinicalOverlayProps = {
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

export function resolveClinicalOverlayOpen(props) {
  if (props.modelValue !== undefined) return props.modelValue;
  if (props.open !== undefined) return props.open;
  return props.isOpen;
}

export function resolveClinicalTeleportTarget(props) {
  return typeof props.teleport === "string" ? props.teleport : "body";
}

export function resolveClinicalOverlayAria({
  props,
  attrs,
  headingId,
  canUseHeading,
  fallbackLabel,
}) {
  let labelledby;
  if (props.ariaLabelledby) {
    labelledby = props.ariaLabelledby;
  } else if (!props.ariaLabel && !attrs["aria-label"] && canUseHeading) {
    labelledby = headingId;
  }

  let ariaLabel;
  if (props.ariaLabel) {
    ariaLabel = props.ariaLabel;
  } else if (attrs["aria-label"]) {
    ariaLabel = String(attrs["aria-label"]);
  } else if (!labelledby) {
    ariaLabel = fallbackLabel;
  }

  return { labelledby, ariaLabel };
}

export function buildClinicalOverlayFocusTrapOptions(
  props,
  { isOpen, onEscape }
) {
  return {
    isOpen,
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
    onEscape,
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
    default: null,
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
  density: {
    type: String,
    default: "comfortable",
  },
  required: {
    type: Boolean,
    default: false,
  },
  hint: {
    type: String,
    default: "",
  },
  hideLabel: {
    type: Boolean,
    default: false,
  },
  showQueryFlag: {
    type: Boolean,
    default: true,
  },
};
