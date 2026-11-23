import type { Stage } from "./types";

export const CONDUCT_STAGE: Stage = {
  id: "conduct",
  label: "Conduct",
  scope: "Participants, sites, visits, safety, supply, monitoring",
  sections: [
    {
      id: "participants",
      label: "Participants",
      description: "The study's participants in enrollment order",
    },
    {
      id: "sites",
      label: "Sites",
      description:
        "Clinical sites registered in your workspace and linked to this study",
    },
    {
      id: "monitoring-visits",
      label: "Monitoring visits",
      description:
        "Site monitoring visit schedules, trip findings, compiled letters, and supervisory sign-off",
    },
    {
      id: "supply",
      label: "Investigational supply",
      description:
        "Chain-of-custody trace, site kit accountability, and randomization schemes",
    },
  ],
};
