import { sectionsForStage } from "../sections/meta";
import type { Stage } from "./types";

export const DATA_STAGE: Stage = {
  id: "data",
  order: 40,
  label: "Data",
  scope:
    "Collection, queries, external data, reconciliation, standards, validation",
  sections: sectionsForStage("data"),
};

export default DATA_STAGE;
