import { Teleport, h } from "vue";

export const OptionalTeleport = (props, { slots }) => {
  const children = slots.default ? slots.default() : null;
  if (!props.enabled) {
    return children;
  }
  return h(Teleport, { to: props.to || "body" }, children);
};

OptionalTeleport.props = {
  enabled: {
    type: Boolean,
    default: true,
  },
  to: {
    type: String,
    default: "body",
  },
};
