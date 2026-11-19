import { DefineComponent } from "vue";

// Export generated CDASH types
export * from "./src/types/cdash.js";

// Vue Components
export const AiActionButton: DefineComponent<
  {
    label?: string;
    icon?: string;
    loading?: boolean;
    loadingText?: string;
    disabled?: boolean;
    variant?: "primary" | "secondary" | "subtle" | "outline";
    size?: "sm" | "md" | "lg";
    requiredRoles?: string[] | string;
    userRoles?: string[] | string;
    unauthorizedTooltip?: string;
    ariaLabel?: string;
  },
  {},
  any
>;

export const ConfidenceBadge: DefineComponent<
  {
    score: number;
    thresholds?: { high?: number; medium?: number };
    showLabel?: boolean;
    showScore?: boolean;
    size?: "sm" | "md" | "lg";
  },
  {},
  any
>;

export const AiSuggestionDrawer: DefineComponent<
  {
    modelValue?: boolean;
    isOpen?: boolean;
    title?: string;
    originalValue?: any;
    suggestedValue: any;
    confidenceScore?: number | null;
    modelIdentifier?: string;
    promptSummary?: string;
    entityName?: string;
    fieldLabel?: string;
    requireReason?: boolean;
    isApproving?: boolean;
    isRejecting?: boolean;
  },
  {},
  any
>;

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

export const ModalErrorAlert: DefineComponent<
  {
    error?: any;
    errorMessage?: string;
    title?: string;
    retryLabel?: string;
    dismissLabel?: string;
    showRetry?: boolean;
    showDismiss?: boolean;
    onRetry?: (...args: any[]) => void;
    onDismiss?: (...args: any[]) => void;
    id?: string;
  },
  {},
  any
>;

export const ClinicalModal: DefineComponent<
  {
    modelValue?: boolean;
    title?: string;
    size?: "sm" | "md" | "lg" | "xl" | string;
    role?: "dialog" | "alertdialog" | string;
  },
  {},
  any
>;

export interface FocusTrapOptions {
  isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  onEscape?: (event: KeyboardEvent) => void;
  escapeDeactivates?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  initialFocus?:
    import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | string;
  restoreFocus?: boolean | (() => HTMLElement | null);
}

export function useFocusTrap(
  targetRef: import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null),
  optionsOrIsOpen?:
    FocusTrapOptions | import("vue").Ref<boolean> | (() => boolean) | boolean,
  options?: Omit<FocusTrapOptions, "isOpen">
): {
  activate: () => void;
  deactivate: () => void;
};

export function getFocusableElements(
  container: HTMLElement | null | undefined
): HTMLElement[];

export function useEscapeClose(
  onCloseCallback: (event: KeyboardEvent) => void,
  options?: {
    isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  }
): void;

export function useAsyncRetry<T extends (...args: any[]) => any>(
  asyncFn?: T
): {
  isLoading: import("vue").Ref<boolean>;
  error: import("vue").Ref<any>;
  execute: (...args: any[]) => Promise<any>;
  retry: (...args: any[]) => Promise<any>;
  clearError: () => void;
};

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
export function generateCanonicalSignature(
  obj: any,
  key: string | Uint8Array
): Promise<string>;
export function verifyCanonicalSignature(
  obj: any,
  sig: string,
  key: string | Uint8Array
): Promise<boolean>;
export function generateGatewaySignature(
  userId: string,
  roles: string,
  timestamp: string,
  version: string | undefined,
  changeReason: string | null | undefined,
  secret: string | Uint8Array
): Promise<string>;
export function verifyGatewaySignature(
  signature: string,
  userId: string,
  roles: string,
  timestamp: string,
  version: string | undefined,
  changeReason: string | null | undefined,
  secret: string | Uint8Array
): Promise<boolean>;
export function generateJwtHS256(
  payload: any,
  secret: string | Uint8Array
): Promise<string>;
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
