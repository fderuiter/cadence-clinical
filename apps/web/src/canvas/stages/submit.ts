import type { Stage } from "./types";

export const SUBMIT_STAGE: Stage = {
  id: "submit",
  label: "Submit",
  scope: "Datasets, Define-XML, reviewer guides, package",
  sections: [
    {
      id: "exports",
      label: "Exports",
      description:
        "Streaming CSV, CDISC ODM v1.3.2 XML, and Dataset-JSON snapshots at a lock or timestamp",
    },
  ],
};
