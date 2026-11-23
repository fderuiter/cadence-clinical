import {
  DEMO_SITES,
  DEMO_STUDY,
  getDemoPersonaByWorkflow,
} from "demo-fixtures";
import { isDemoBuild } from "../../utils/demoMode.js";
import type { ClinicalState } from "../clinicalScope";

export function createClinicalCtmsState(
  demo: boolean = isDemoBuild()
): Pick<ClinicalState, "currentCtmsData"> {
  const monitorUsername =
    (demo && getDemoPersonaByWorkflow("monitor")?.username) || "cra.monitor";
  const siteCodes =
    demo && DEMO_SITES.length > 0
      ? DEMO_SITES.map((s) => s.code)
      : ["SITE-101", "SITE-102"];
  const demoStudyCode = (demo && DEMO_STUDY.code) || "CADENCE-101";

  return {
    currentCtmsData: {
      milestones: [
        {
          id: "M1",
          type: "SITE_SELECTION",
          plannedDate: "2026-01-05", // deid-ignore
          actualDate: "2026-01-05", // deid-ignore
          status: "ACHIEVED",
        },
        {
          id: "M2",
          type: "INITIATION_VISIT",
          plannedDate: "2026-01-08", // deid-ignore
          actualDate: "2026-01-09", // deid-ignore
          status: "ACHIEVED",
        },
        {
          id: "M3",
          type: "SITE_ACTIVATION",
          plannedDate: "2026-01-11", // deid-ignore
          actualDate: "",
          status: "PLANNED",
        },
        {
          id: "M4",
          type: "FIRST_SUBJECT_ENROLLED",
          plannedDate: "2026-01-12", // deid-ignore
          actualDate: "",
          status: "PLANNED",
        },
      ],
      visits: [
        {
          id: "V1",
          type: "SIV",
          scheduledDate: "2026-01-08", // deid-ignore
          actualDate: "2026-01-09", // deid-ignore
          status: "SIGNED_OFF",
          cra: monitorUsername,
        },
        {
          id: "V2",
          type: "IMV",
          scheduledDate: "2026-01-25", // deid-ignore
          actualDate: "",
          status: "SCHEDULED",
          cra: monitorUsername,
        },
      ],
      allocations: [
        {
          cra: monitorUsername,
          activeAllocations: siteCodes.length,
          sites: siteCodes,
          studies: [demoStudyCode],
        },
      ],
      recruitment: [
        {
          siteId: siteCodes[0] || "SITE-101",
          screened: 2,
          enrolled: 2,
          target: 20,
        },
        {
          siteId: siteCodes[1] || "SITE-102",
          screened: 1,
          enrolled: 1,
          target: 15,
        },
      ],
    },
  };
}
