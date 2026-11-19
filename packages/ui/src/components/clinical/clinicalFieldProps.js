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
