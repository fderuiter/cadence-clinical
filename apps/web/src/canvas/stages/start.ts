import type { Stage } from "./types";

export const START_STAGE: Stage = {
  id: "start",
  label: "Start",
  scope: "Countries, site activation, essential documents, training",
  sections: [
    {
      id: "site-activation",
      label: "Site activation",
      description:
        "A site's activation documents and activating the site once they are complete",
    },
    {
      id: "startup-greenlight",
      label: "Startup readiness",
      description:
        "Six-facet country and site readiness gates and Part 11 certification",
    },
    {
      id: "documents",
      label: "Documents",
      description:
        "Study and site essential document binder, intelligence staging, and clinical QC approval",
    },
    {
      id: "training",
      label: "Training",
      description:
        "Protocol curricula, personnel training assignments, and site delegation logs",
    },
    {
      id: "site-milestones",
      label: "Site milestones",
      description:
        "Planned and achieved startup and operational milestones by site",
    },
    {
      id: "delegation",
      label: "Delegation log",
      description:
        "Delegated trial duties, Principal Investigator sign-off, and export",
    },
  ],
};
