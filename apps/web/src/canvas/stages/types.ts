export type StageId =
  "design" | "start" | "conduct" | "data" | "analyze" | "submit";

export type SectionId =
  | "protocol-versions"
  | "site-activation"
  | "startup-greenlight"
  | "documents"
  | "training"
  | "site-milestones"
  | "delegation"
  | "participants"
  | "sites"
  | "monitoring-visits"
  | "supply"
  | "queries"
  | "locks"
  | "validation"
  | "exports";

export interface Section {
  id: SectionId;
  label: string;
  description: string;
}

export interface Stage {
  id: StageId;
  label: string;
  /** What the stage covers once all of its sections exist (§2.2). */
  scope: string;
  sections: Section[];
}
