import { sectionsForStage } from "../sections/meta";
import type { Stage } from "./types";

export const SUBMIT_STAGE: Stage = {
  id: "submit",
  order: 60,
  label: "Submit",
  scope: "Datasets, Define-XML, reviewer guides, package",
  sections: sectionsForStage("submit"),
};

export default SUBMIT_STAGE;
