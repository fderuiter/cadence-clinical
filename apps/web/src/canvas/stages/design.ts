import type { Stage } from "./types";

export const DESIGN_STAGE: Stage = {
  id: "design",
  label: "Design",
  scope:
    "Protocol, objectives and estimands, schedule of activities, collection specs, standards and mappings",
  sections: [
    {
      id: "protocol-versions",
      label: "Protocol versions",
      description:
        "The protocol's versions, and what each amendment changes and affects",
    },
  ],
};
