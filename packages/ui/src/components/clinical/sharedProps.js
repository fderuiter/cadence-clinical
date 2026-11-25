/**
 * Shared Vue prop definitions and attribute helpers for clinical form components.
 * Extracted to eliminate duplication across ClinicalFieldLayout, ClinicalInput,
 * ClinicalLookupInput, ClinicalRadioGroup, and ClinicalFormField (ADR-2679).
 */
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
  unit: {
    type: String,
    default: "",
  },
  range: {
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

const DATA_ATTR_RE = /^data-/i;
const CONTAINER_DATA_KEYS = new Set(["data-cdash"]);
const NUMERIC_TYPES = new Set([
  "number",
  "date",
  "datetime-local",
  "time",
  "month",
  "week",
  "tel",
  "range",
]);
const NUMERIC_MODES = new Set(["numeric", "decimal"]);

export function pickDataOnlyAttributes(source) {
  if (!source || typeof source !== "object") return {};
  const result = {};
  for (const [key, val] of Object.entries(source)) {
    if (
      DATA_ATTR_RE.test(key) &&
      val !== undefined &&
      val !== null &&
      val !== false
    ) {
      result[key] = val;
    }
  }
  return result;
}

export function splitContainerAndControlAttributes(
  attributes,
  extraAttrs,
  strippedKeys
) {
  const container = {};
  const control = {};
  for (const src of [attributes, extraAttrs]) {
    if (!src || typeof src !== "object") continue;
    for (const [key, val] of Object.entries(src)) {
      if (val === undefined || val === null) continue;
      if (key.startsWith("on") && key.length > 2) continue;
      if (CONTAINER_DATA_KEYS.has(key)) {
        if (val !== false) container[key] = val;
        continue;
      }
      if (strippedKeys.has(key)) continue;
      control[key] = val;
    }
  }
  return { container, control };
}

export function firstDefinedAttr(...values) {
  for (const val of values) {
    if (val !== undefined && val !== null && val !== "") return val;
  }
  return undefined;
}

export function resolveBooleanAttr(...values) {
  for (const val of values) {
    if (val === true || val === "" || val === "true") return true;
    if (val === false || val === "false") return false;
  }
  return false;
}

export function mergeDescribedByIds(...sources) {
  const tokens = new Set();
  for (const src of sources) {
    if (typeof src !== "string") continue;
    for (const token of src.trim().split(/\s+/)) {
      if (token) tokens.add(token);
    }
  }
  return tokens.size > 0 ? Array.from(tokens).join(" ") : undefined;
}

export function resolveAriaInvalidValue(hasError, ...candidates) {
  if (hasError) return "true";
  for (const raw of candidates) {
    if (raw === true || raw === "true") return "true";
    if (raw === "false") return "false";
    if (raw === "grammar" || raw === "spelling") return raw;
  }
  return undefined;
}

export function resolveTabularNumsAttr(
  explicit,
  type,
  inputMode,
  min,
  max,
  step
) {
  if (explicit !== undefined) return explicit ? "true" : undefined;
  if (
    NUMERIC_TYPES.has(type) ||
    NUMERIC_MODES.has(inputMode) ||
    min !== undefined ||
    max !== undefined ||
    step !== undefined
  ) {
    return "true";
  }
  return undefined;
}

export function resolveEffectiveRequiredAndAria(props, attrs) {
  const required = resolveBooleanAttr(
    props.required ? true : undefined,
    attrs.required,
    props.attributes?.required,
    attrs["aria-required"],
    props.attributes?.["aria-required"]
  );
  const raw = firstDefinedAttr(
    attrs["aria-required"],
    props.attributes?.["aria-required"]
  );
  const ariaRequired =
    raw === true || raw === "true"
      ? "true"
      : raw === "false"
        ? "false"
        : undefined;
  return { required, ariaRequired };
}

export function buildFieldLayoutProps(
  props,
  attrs,
  containerAttributes,
  effectiveRequired
) {
  return {
    id: props.id,
    class: attrs.class,
    style: attrs.style,
    label: props.label,
    query: props.query,
    gridSpan: props.gridSpan,
    error: props.error ?? undefined,
    attributes: containerAttributes,
    canManageQueries: props.canManageQueries,
    queryLabel: props.queryLabel,
    density: props.density,
    required: effectiveRequired,
    hint: props.hint,
    unit: props.unit,
    range: props.range,
    hideLabel: props.hideLabel,
    showQueryFlag: props.showQueryFlag,
  };
}
