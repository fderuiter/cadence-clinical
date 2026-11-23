import type { Stage } from "./types";

export const DATA_STAGE: Stage = {
  id: "data",
  label: "Data",
  scope:
    "Collection, queries, external data, reconciliation, standards, validation",
  sections: [
    {
      id: "queries",
      label: "Queries",
      description: "Data queries across visible enrollments in this study",
    },
    {
      id: "locks",
      label: "Locks",
      description:
        "Frozen and locked scopes across the study, with signed apply and release",
    },
    {
      id: "validation",
      label: "Validation",
      description:
        "Edit check rule packs, pre-activation previews, study pinning, and validation findings",
    },
  ],
};
