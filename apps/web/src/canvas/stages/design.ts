import { sectionsForStage } from "../sections/meta";
import type { Stage } from "./types";

export const DESIGN_STAGE: Stage = {
  id: "design",
  order: 10,
  label: "Design",
  scope:
    "Protocol, objectives and estimands, schedule of activities, collection specs, standards and mappings",
  sections: sectionsForStage("design"),
};

export default DESIGN_STAGE;
