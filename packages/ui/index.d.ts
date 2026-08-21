import { DefineComponent } from "vue";

// Export generated CDASH types
export * from "./src/types/cdash.js";

// Vue Components
export const ClinicalFormField: DefineComponent<
  {
    field: any;
    modelValue?: string | number;
    query?: any;
    error?: string | null;
    lookupStatus?: {
      status: "none" | "loading" | "valid" | "invalid" | "degraded";
      message?: string;
    } | null;
  },
  {},
  any
>;

export const ClinicalInput: DefineComponent<
  {
    id: string;
    label: string;
    modelValue?: string | number;
    query?: any;
    gridSpan?: number | string;
    error?: string | null;
    attributes?: Record<string, any>;
  },
  {},
  any
>;

export const ClinicalRadioGroup: DefineComponent<
  {
    id: string;
    label: string;
    options?: any[];
    modelValue?: string | number;
    query?: any;
    gridSpan?: number | string;
    error?: string | null;
  },
  {},
  any
>;

export const ClinicalLookupInput: DefineComponent<
  {
    id: string;
    label: string;
    modelValue?: string | number;
    status?: "none" | "loading" | "valid" | "invalid" | "degraded";
    statusMessage?: string;
    gridSpan?: number | string;
    error?: string | null;
    query?: any;
    attributes?: Record<string, any>;
  },
  {},
  any
>;

export const ClinicalQueryFlag: DefineComponent<
  {
    id: string;
    query?: any;
  },
  {},
  any
>;

export const ClinicalQueryPanel: DefineComponent<
  {
    id: string;
    query?: any;
    label: string;
  },
  {},
  any
>;

export const MediaPlayer: DefineComponent<
  {
    src: string;
    mimeType?: string;
    title?: string;
    isWatermarked?: boolean;
    watermarkText?: string;
    autoplay?: boolean;
    initialZoom?: number;
  },
  {},
  any
>;

export const FileUploadModal: DefineComponent<
  {
    modelValue: boolean;
    studyId: string;
    siteId?: string | null;
    title?: string;
    maxSizeBytes?: number;
    multipartThresholdBytes?: number;
    apiEndpoint?: string;
  },
  {},
  any
>;

// Utility functions
export function debounce<T extends (...args: any[]) => any>(
  func: T,
  wait: number
): (...args: Parameters<T>) => void;
export function createClinicalLookupInput(
  id: string,
  label: string,
  value?: string,
  status?: "none" | "loading" | "valid" | "invalid" | "degraded",
  statusMessage?: string
): string;
export function createConditionRowHTML(
  index: number,
  forms?: any[],
  fields?: any[],
  initialData?: any
): string;
export function createRuleEditorHTML(
  forms?: any[],
  fields?: any[],
  options?: any
): string;
export function serializeConditionsTree(
  conditions?: any[],
  matchOperator?: string
): any;
export function deserializeConditionsTree(tree: any): {
  conditions: any[];
  matchOperator: string;
};
export function createSoaBuilderMatrix(soaData: any): string;
export function createClinicalVisitMatrix(
  visitsOrSoa: any,
  forms?: any[]
): string;
export function initHoverDetection(): void;

export function normalizeApprovedConsent(approved: any): any;
export function shapeComprehensionAnswers(answers: any): any;
export function interpretComprehensionResult(result: any): any;
export function toBeAccessible(received: any): any;

export function canonicalSerialize(obj: any): string;
export function generateCanonicalSignature(obj: any, key: any): string;
export function verifyCanonicalSignature(
  obj: any,
  sig: string,
  key: any
): boolean;
export function generateGatewaySignature(payload: any, key: any): string;
export function verifyGatewaySignature(
  payload: any,
  sig: string,
  key: any
): boolean;
export function generateJwtHS256(payload: any, secret: string): string;
export function sha256(message: string): Promise<string>;
export function validateField(value: any, rules: any): any;
export function buildLedgerBlock(
  index: number,
  timestamp: string,
  action: string,
  details: any,
  reason: string,
  prevHash: string
): Promise<any>;
export function encryptAESGCM(
  plaintext: string,
  key: Uint8Array
): Promise<{ ciphertext: string; iv: string; tag: string }>;
export function decryptAESGCM(
  ciphertext: string,
  iv: string,
  tag: string,
  key: Uint8Array
): Promise<string>;
export function deriveSessionKey(
  pin: string,
  salt: string
): Promise<Uint8Array>;
export function deriveKeyFromPIN(
  pin: string,
  salt: string
): Promise<Uint8Array>;
