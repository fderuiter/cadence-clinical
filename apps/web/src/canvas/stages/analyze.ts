import { sectionsForStage } from "../sections/meta";
import type { Stage } from "./types";

export const ANALYZE_STAGE: Stage = {
  id: "analyze",
  order: 50,
  label: "Analyze",
  scope: "ADaM, derivations, analysis, TLFs, traceability",
  sections: sectionsForStage("analyze"),
};

export default ANALYZE_STAGE;
