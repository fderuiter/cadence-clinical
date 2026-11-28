export type StageId =
  "design" | "start" | "conduct" | "data" | "analyze" | "submit";

export type SectionId =
  | "protocol-versions"
  | "schedule-of-activities"
  | "sites"
  | "site-activation"
  | "startup-greenlight"
  | "documents"
  | "training"
  | "site-milestones"
  | "delegation"
  | "participants"
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
  /** True when the stage's canvas sections are served without missing-service placeholders. */
  complete?: boolean;
  sections: Section[];
}
