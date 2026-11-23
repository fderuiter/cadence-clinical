import { sectionsForStage } from "../sections/meta";
import type { Stage } from "./types";

export const CONDUCT_STAGE: Stage = {
  id: "conduct",
  order: 30,
  label: "Conduct",
  scope: "Participants, sites, visits, safety, supply, monitoring",
  sections: sectionsForStage("conduct"),
};

export default CONDUCT_STAGE;
