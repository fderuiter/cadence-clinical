import { sectionsForStage } from "../sections/meta";
import type { Stage } from "./types";

export const START_STAGE: Stage = {
  id: "start",
  order: 20,
  label: "Start",
  scope: "Countries, site activation, essential documents, training",
  sections: sectionsForStage("start"),
};

export default START_STAGE;
