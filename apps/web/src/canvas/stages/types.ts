export type StageId =
  "design" | "start" | "conduct" | "data" | "analyze" | "submit";

export type KnownSectionId =
  | "protocol-versions"
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

export type SectionId = KnownSectionId | (string & {});

export interface Section {
  id: SectionId;
  label: string;
  description: string;
}

export interface Stage {
  id: StageId;
  order?: number;
  label: string;
  /** What the stage covers once all of its sections exist (§2.2). */
  scope: string;
  sections: Section[];
}
