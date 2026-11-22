import type AxeBuilder from "@axe-core/playwright";
import type { AxeResults, Result } from "axe-core";

export declare const WCAG_22_AA_TAGS: string[];
export declare const DEFAULT_WATERMARK_EXCLUSIONS: string[];
export declare const DEFAULT_CONTROL_BORDER_SELECTOR: string;

export interface Wcag22AuditOptions {
  include?: string | string[];
  exclude?: string | string[];
  includeWatermark?: boolean;
  disableRules?: string | string[];
  tags?: string[];
  checkControlBorders?: boolean;
  controlSelector?: string;
  minContrastRatio?: number;
}

export interface ControlBorderViolation {
  id: string;
  impact: string;
  target: string[];
  ratio: number;
  borderColor: string;
  html: string;
}

export interface Wcag22AuditResults extends AxeResults {
  borderViolations: ControlBorderViolation[];
}

export declare function createWcag22AxeBuilder(
  page: any,
  options?: Wcag22AuditOptions
): AxeBuilder;

export declare function getControlBorderContrast(
  page: any,
  selector: string
): Promise<number>;

export declare function auditInteractiveControlBorders(
  page: any,
  options?: Wcag22AuditOptions
): Promise<ControlBorderViolation[]>;

export declare function formatAccessibilityViolations(
  violations?: Result[],
  borderViolations?: ControlBorderViolation[]
): string;

export declare function runWcag22Audit(
  page: any,
  options?: Wcag22AuditOptions
): Promise<Wcag22AuditResults>;
