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
  hideLabel: {
    type: Boolean,
    default: false,
  },
  showQueryFlag: {
    type: Boolean,
    default: true,
  },
};
