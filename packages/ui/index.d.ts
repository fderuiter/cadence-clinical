import { DefineComponent, DefineSetupFnComponent, SlotsType } from "vue";

type DefineSlotsComponent<
  Props extends Record<string, any>,
  Slots extends Record<string, any>,
> = DefineSetupFnComponent<Props, Record<string, any>, SlotsType<Slots>>;

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

export const ClinicalContextBar: DefineComponent<
  {
    studiesLabel?: string;
    studiesTo?: string | Record<string, any>;
    studyCode?: string;
    studyTo?: string | Record<string, any>;
    siteCode?: string;
    siteLabelPrefix?: string;
    siteTo?: string | Record<string, any>;
    participantCode?: string;
    participantTo?: string | Record<string, any>;
    currentLabel?: string;
    ariaLabel?: string;
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

export interface ClinicalSelectOption {
  value: string | number;
  label?: string;
  disabled?: boolean;
}

export const ClinicalInput: DefineSlotsComponent<
  {
    id: string;
    label: string;
    modelValue?: string | number;
    type?: string;
    as?: "input" | "select" | "textarea" | string;
    options?: Array<ClinicalSelectOption | string | number> | null;
    placeholder?: string;
    disabled?: boolean;
    readonly?: boolean;
    required?: boolean;
    density?: "comfortable" | "compact" | string;
    hint?: string;
    hideLabel?: boolean;
    showQueryFlag?: boolean;
    rows?: number | string;
    min?: number | string | null;
    max?: number | string | null;
    step?: number | string | null;
    maxlength?: number | string | null;
    autocomplete?: string;
    inputmode?: string;
    tabularNums?: boolean;
    list?: string;
    name?: string;
    ariaLabel?: string;
    ariaDescribedby?: string;
    extraClass?: string;
    labelClass?: string | any[] | Record<string, boolean>;
    controlClass?: string | any[] | Record<string, boolean>;
    controlAttrs?: Record<string, any>;
    query?: any;
    gridSpan?: number | string;
    error?: string | null;
    attributes?: Record<string, any>;
    canManageQueries?: boolean;
    queryLabel?: string;
    "onUpdate:modelValue"?: (value: any) => void;
    onInput?: (value: any, event?: Event) => void;
    onChange?: (value: any, target?: HTMLElement) => void;
    onBlur?: (event: FocusEvent) => void;
    onKeydown?: (event: KeyboardEvent) => void;
    onKeyup?: (event: KeyboardEvent) => void;
  },
  {
    default?: (props: Record<string, never>) => any;
    "after-input"?: (props: Record<string, never>) => any;
  }
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
    isOpen?: boolean;
    fieldLabel?: string;
  },
  {},
  any
>;

export interface ClinicalTabItem {
  id: string | number;
  label: string;
  badge?: string | number | null;
  disabled?: boolean;
  buttonId?: string;
  panelId?: string;
  buttonClass?: string | any[] | Record<string, boolean>;
  title?: string;
  dataTestid?: string;
}

export const ClinicalTabs: DefineComponent<
  {
    modelValue: string | number;
    tabs: ClinicalTabItem[];
    variant?: "underline" | "pills" | "segmented";
    overflow?: "scroll" | "wrap";
    ariaLabel?: string;
    ariaLabelledby?: string;
    orientation?: "horizontal" | "vertical";
    id?: string;
    idPrefix?: string;
    listClass?: string | any[] | Record<string, boolean>;
    tabClass?: string | any[] | Record<string, boolean>;
    activeTabClass?: string;
    manual?: boolean;
    keepAlivePanels?: boolean;
  },
  {},
  any
>;

export const ClinicalIconButton: DefineComponent<
  {
    label: string;
    icon?: string;
    type?: "button" | "submit" | "reset";
    variant?: "unstyled" | "ghost" | "secondary" | "danger";
    size?: "sm" | "md" | "lg";
    disabled?: boolean;
    title?: string;
  },
  {},
  any
>;

export const ClinicalDropzone: DefineSlotsComponent<
  {
    inputId?: string;
    accept?: string;
    multiple?: boolean;
    disabled?: boolean;
    hasSelection?: boolean;
    icon?: string;
    promptText?: string;
    hintText?: string;
    buttonLabel?: string;
    buttonContext?: string;
    buttonClass?: string;
    inputAriaLabel?: string;
    describedBy?: string;
    removable?: boolean;
    removeLabel?: string;
    removeContext?: string;
    removeButtonClass?: string;
  },
  {
    prompt?: (props: Record<string, never>) => any;
    hint?: (props: Record<string, never>) => any;
    selected?: (props: Record<string, never>) => any;
    actions?: (props: {
      openFilePicker: () => void;
      resetInput: () => void;
    }) => any;
  }
>;

export function createSrOnlyActionButton(options?: {
  label: string;
  srContext?: string;
  className?: string;
  dataId?: string | number;
  disabled?: boolean;
  ariaBusy?: boolean;
  type?: string;
  id?: string;
}): string;

export const ClinicalQueryPanel: DefineComponent<
  {
    id: string;
    query?: any;
    label: string;
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

export const EmptyState: DefineSlotsComponent<
  {
    icon?: string;
    title?: string;
    message?: string;
    actionText?: string;
  },
  {
    default?: (props: Record<string, never>) => any;
    icon?: (props: Record<string, never>) => any;
    action?: (props: Record<string, never>) => any;
  }
>;

export const ClinicalModal: DefineSlotsComponent<
  {
    modelValue?: boolean;
    isOpen?: boolean;
    open?: boolean;
    title?: string;
    ariaLabel?: string;
    ariaLabelledby?: string;
    ariaDescribedby?: string;
    size?: "sm" | "md" | "lg" | "xl" | string;
    role?: "dialog" | "alertdialog" | string;
    dismissible?: boolean;
    closeOnBackdrop?: boolean;
    initialFocus?:
      | import("vue").Ref<HTMLElement | null>
      | (() => HTMLElement | null)
      | string
      | null;
    restoreFocus?:
      | boolean
      | import("vue").Ref<HTMLElement | null>
      | (() => HTMLElement | null)
      | string;
    fallbackFocus?:
      | import("vue").Ref<HTMLElement | null>
      | (() => HTMLElement | null)
      | string
      | null;
    teleport?: boolean | string;
    id?: string;
    overlayId?: string;
    dialogClass?: string | any[] | Record<string, boolean>;
    backdropClass?: string | any[] | Record<string, boolean>;
    headerClass?: string | any[] | Record<string, boolean>;
    bodyClass?: string | any[] | Record<string, boolean>;
    footerClass?: string | any[] | Record<string, boolean>;
    closeLabel?: string;
    showHeader?: boolean;
    showClose?: boolean;
    showFooter?: boolean;
  },
  {
    default?: (props: Record<string, never>) => any;
    header?: (props: { titleId: string; close: () => void }) => any;
    footer?: (props: { close: () => void }) => any;
  }
>;

export const ClinicalDrawer: DefineSlotsComponent<
  {
    modelValue?: boolean;
    isOpen?: boolean;
    open?: boolean;
    docked?: boolean;
    title?: string;
    subtitle?: string;
    ariaLabel?: string;
    ariaLabelledby?: string;
    id?: string;
    overlayId?: string;
    overlayClass?: string | any[] | Record<string, boolean>;
    panelClass?: string | any[] | Record<string, boolean>;
    bodyClass?: string | any[] | Record<string, boolean>;
    position?: "right" | "left" | "bottom" | string;
    mobileBottomSheet?: boolean;
    size?: "sm" | "md" | "lg" | "xl" | string;
    role?: "dialog" | "alertdialog" | string;
    dismissible?: boolean;
    closeOnBackdrop?: boolean;
    teleport?: boolean | string;
    initialFocus?:
      | import("vue").Ref<HTMLElement | null>
      | (() => HTMLElement | null)
      | string
      | null;
    restoreFocus?:
      | boolean
      | import("vue").Ref<HTMLElement | null>
      | (() => HTMLElement | null)
      | string;
    fallbackFocus?:
      | import("vue").Ref<HTMLElement | null>
      | (() => HTMLElement | null)
      | string
      | null;
    closeLabel?: string;
    showHeader?: boolean;
  },
  {
    default?: (props: Record<string, never>) => any;
    header?: (props: { titleId: string; close: () => void }) => any;
    subheader?: (props: Record<string, never>) => any;
    footer?: (props: { close: () => void }) => any;
    "after-panel"?: (props: Record<string, never>) => any;
  }
>;

export const AuditLogViewer: DefineComponent<
  {
    logs: Array<{
      id: string | number;
      timestamp?: string;
      user_id?: string;
      action?: string;
      entity_type?: string;
      entity_id?: string;
      reason_for_change?: string;
      changes?: Array<{ field: string; old_val?: any; new_val?: any }>;
    }>;
    title?: string;
  },
  {},
  any
>;

export interface ClinicalDataTableColumn {
  key: string;
  label: string;
  sortable?: boolean;
  rowHeader?: boolean;
  align?: "left" | "right" | "center" | string;
  headerClass?: string | any[] | Record<string, boolean>;
  cellClass?: string | any[] | Record<string, boolean>;
}

export const ClinicalDataTable: DefineSlotsComponent<
  {
    columns: ClinicalDataTableColumn[];
    data: any[];
    pageSize?: number;
    selectable?: boolean;
    title?: string;
    hideHeader?: boolean;
    labelledBy?: string;
    ariaLabel?: string;
    plain?: boolean;
    containerClass?: string | any[] | Record<string, boolean>;
    wrapperClass?: string | any[] | Record<string, boolean>;
    wrapperTestId?: string;
    tableId?: string;
    tableClass?: string | any[] | Record<string, boolean>;
    tableTestId?: string;
    tbodyId?: string;
    rowKey?: string | ((row: any, index: number) => string | number);
    rowId?: (row: any, index: number) => string | undefined;
    rowClass?:
      | string
      | ((
          row: any,
          index: number
        ) => string | any[] | Record<string, boolean> | undefined);
    rowAttrs?: (
      row: any,
      index: number
    ) => Record<string, string | number | boolean | undefined> | undefined;
    selectedRowKeys?: any[] | null;
    emptyMessage?: string;
    isRowMasked?: (row: any, index: number) => boolean;
    externalPagination?: boolean;
    page?: number | null;
    totalRecords?: number | null;
    hasMore?: boolean;
    loadingMore?: boolean;
    loadMoreLabel?: string;
    loadMoreId?: string;
    externalSort?: boolean;
    initialSortKey?: string;
    initialSortOrder?: "asc" | "desc" | string;
    onSelect?: (selectedKeys: any[]) => void;
    "onRow-click"?: (row: any) => void;
    "onPage-change"?: (page: number) => void;
    "onLoad-more"?: () => void;
    "onSort-change"?: (payload: { key: string; order: string }) => void;
  },
  {
    [headSlot: `head-${string}`]: (props: {
      column: ClinicalDataTableColumn;
    }) => any;
    [cellSlot: `cell-${string}`]: (props: {
      row: any;
      value: any;
      index: number;
    }) => any;
    "header-actions"?: (props: Record<string, never>) => any;
    "header-select"?: (props: Record<string, never>) => any;
    "masked-row"?: (props: { row: any; index: number; colSpan: number }) => any;
    empty?: (props: Record<string, never>) => any;
    pagination?: (props: {
      currentPage: number;
      totalPages: number;
      totalRecords: number;
      goToPage: (page: number) => void;
    }) => any;
  }
>;

export const ClinicalFieldLayout: DefineSlotsComponent<
  {
    id: string;
    label: string;
    query?: any;
    gridSpan?: number | string;
    error?: string | null;
    attributes?: Record<string, any>;
    tag?: string;
    extraClass?: string;
    labelClass?: string | any[] | Record<string, boolean>;
    canManageQueries?: boolean;
    queryLabel?: string;
    density?: "comfortable" | "compact" | string;
    required?: boolean;
    hint?: string;
    hideLabel?: boolean;
    showQueryFlag?: boolean;
  },
  {
    default?: (props: {
      id: string;
      error?: string | null;
      errorId: string;
      hintId?: string;
      describedBy?: string;
      hasError: boolean;
    }) => any;
    "after-input"?: (props: Record<string, never>) => any;
  }
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

export const PersonaSwitcher: DefineComponent<
  {
    modelValue?: string;
    personas?: Array<{
      id: string;
      label: string;
      role: string;
      badge?: string;
    }>;
  },
  {},
  any
>;

export const SignOutConfirmModal: DefineComponent<
  {
    modelValue?: boolean;
    open?: boolean;
    title?: string;
    description?: string;
    stayLabel?: string;
    confirmLabel?: string;
    id?: string;
    teleport?: boolean | string;
    onConfirm?: () => void;
    onCancel?: () => void;
  },
  {},
  any
>;

export interface ClinicalToastItem {
  id: string | number;
  tone?: "info" | "success" | "warning" | "error" | string;
  variant?: "info" | "success" | "warning" | "error" | string;
  title?: string;
  message?: string;
  action?: { label: string; to: string } | null;
  timeoutMs?: number;
  duration?: number;
}

export const ClinicalToastHost: DefineSlotsComponent<
  {
    toasts?: ClinicalToastItem[];
    ariaLabel?: string;
    dismissLabel?: string;
    latestToastId?: string;
    manageTimers?: boolean;
    onDismiss?: (id: string | number) => void;
    onAction?: (toast: ClinicalToastItem) => void;
    onPause?: (id: string | number) => void;
    onResume?: (id: string | number) => void;
  },
  {
    extra?: (props: { toast: any }) => any;
  }
>;

export interface FocusTrapOptions {
  isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  modal?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  onEscape?: (event: KeyboardEvent) => void;
  escapeDeactivates?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  initialFocus?:
    import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | string;
  restoreFocus?:
    | boolean
    | import("vue").Ref<HTMLElement | null>
    | (() => HTMLElement | null)
    | string;
  fallbackFocus?:
    | import("vue").Ref<HTMLElement | null>
    | (() => HTMLElement | null)
    | string
    | null;
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

export function getTopActiveModalContainer(): HTMLElement | null;

export function hasActiveModalFocusTrap(options?: {
  excludeId?: string | null;
  excludeElement?: HTMLElement | null;
}): boolean;

export function trapTabFocus(
  event: KeyboardEvent,
  container: HTMLElement | null | undefined
): boolean;

export type InlineFocusCandidate =
  | HTMLElement
  | string
  | import("vue").Ref<HTMLElement | null | undefined>
  | (() =>
      | HTMLElement
      | string
      | Array<HTMLElement | string | null | undefined>
      | null
      | undefined)
  | null
  | undefined;

export interface HandoffFocusOptions {
  container?:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null;
  fallback?: InlineFocusCandidate | InlineFocusCandidate[];
  scrollIfOffscreen?: boolean;
  preventScroll?: boolean;
  awaitNextTick?: boolean;
  includeDefaultFallbacks?: boolean;
  keepTrigger?: boolean;
}

export function handoffFocus(
  targets: InlineFocusCandidate | InlineFocusCandidate[],
  options?: HandoffFocusOptions
): Promise<HTMLElement | null>;

export interface InlineFocusHandoffOptions {
  isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
  openTarget?: InlineFocusCandidate | InlineFocusCandidate[];
  closeFallback?: InlineFocusCandidate | InlineFocusCandidate[];
  fallback?: InlineFocusCandidate | InlineFocusCandidate[];
  container?:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null;
  scrollIfOffscreen?: boolean;
}

export interface InlineFocusHandoffControls {
  rememberTrigger: (
    explicitTrigger?: InlineFocusCandidate
  ) => HTMLElement | null;
  restoreTrigger: (
    fallbackTargets?: InlineFocusCandidate | InlineFocusCandidate[],
    options?: HandoffFocusOptions
  ) => Promise<HTMLElement | null>;
  clearTrigger: () => void;
  handoff: (
    targets: InlineFocusCandidate | InlineFocusCandidate[],
    options?: HandoffFocusOptions
  ) => Promise<HTMLElement | null>;
}

export function useInlineFocusHandoff(
  options?: InlineFocusHandoffOptions
): InlineFocusHandoffControls;

export interface ScrollOverflowState {
  canScrollStart: boolean;
  canScrollEnd: boolean;
  scrollLeft: number;
  maxScrollLeft: number;
}

export interface AlignActiveScrollOptions {
  selector?: string;
  immediate?: boolean;
  behavior?: "auto" | "smooth";
  threshold?: number;
}

export interface AlignActiveScrollResult {
  aligned: boolean;
  targetScrollLeft: number;
  behavior: "auto" | "smooth";
  canScrollStart: boolean;
  canScrollEnd: boolean;
}

export function prefersReducedMotion(): boolean;

export function computeScrollOverflowState(
  container:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null,
  threshold?: number
): ScrollOverflowState;

export function alignActiveScrollItem(
  container:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null,
  options?: AlignActiveScrollOptions
): AlignActiveScrollResult;

export function useActiveScrollAlignment(
  containerRef:
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined),
  options?: {
    activeKey?: any;
    selector?: string;
    threshold?: number;
  }
): {
  canScrollStart: import("vue").Ref<boolean>;
  canScrollEnd: import("vue").Ref<boolean>;
  alignActiveItem: (
    options?: AlignActiveScrollOptions
  ) => AlignActiveScrollResult;
  updateOverflowState: () => ScrollOverflowState;
};

export function useEscapeClose(
  onCloseCallback: (event: KeyboardEvent) => void,
  options?: {
    isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
    target?: EventTarget | null;
  }
): {
  stop: () => void;
};

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
export type DebouncedFunction<T extends (...args: any[]) => any> = ((
  ...args: Parameters<T>
) => void) & {
  flush: () => ReturnType<T> | undefined;
  cancel: () => void;
  pending: () => boolean;
};

export function debounce<T extends (...args: any[]) => any>(
  func: T,
  wait: number
): DebouncedFunction<T>;

export function registerDraftSource(source: {
  flush?: () => void | Promise<void>;
  hasDrafts?: () => boolean;
}): () => void;
export function flushRegisteredDraftSources(): Promise<void>;
export function flushRegisteredDraftSourcesSync(): void;
export function hasRegisteredUnsavedDrafts(): boolean;
export function resetDraftSourcesForTests(): void;
export function useSignOutGuard(options?: {
  onConfirmSignOut?: () => void | Promise<void>;
  flushExternalDrafts?: () => void | Promise<void>;
  hasExternalDrafts?: () => boolean;
}): {
  isConfirmOpen: import("vue").Ref<boolean>;
  isFlushing: import("vue").Ref<boolean>;
  requestSignOut: () => Promise<{ prompted: boolean; signedOut: boolean }>;
  confirmSignOut: () => Promise<void>;
  cancelSignOut: () => void;
};
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
  salt: string | Uint8Array,
  iterations?: number
): Promise<Uint8Array>;

export const SEMANTIC_ACTION_PATTERN: RegExp;
export const STEP_UP_ACTION_MAX_LENGTH: number;
export const SEMANTIC_ACTIONS: Readonly<{
  CTMS_DOA_SIGN_OFF: "ctms.doa.sign_off";
  ECONSENT_CAPTURE: "econsent.consent.capture";
  ECONSENT_RECONSENT_SIGN: "econsent.reconsent.sign";
  CEC_CHAIR_ADJUDICATE: "execution.cec.chair_adjudicate";
  ETMF_DOCUMENT_SIGN_OFF: "etmf.document.sign_off";
  COLLECTION_SIGN: "collection.sign";
  SOA_APPROVE: "designer.soa.approve";
}>;
export function isValidStepUpAction(action: string | null | undefined): boolean;
export function isValidSemanticAction(
  semanticAction: string | null | undefined
): boolean;
export interface StepUpVerificationPayloadInput {
  username?: string | null;
  password: string;
  totp?: string | null;
  action: string;
  semanticAction?: string;
  semantic_action?: string;
  batchId?: string | null;
  batch_id?: string | null;
}
export interface StepUpVerificationPayload {
  username?: string;
  password: string;
  totp?: string;
  action: string;
  semantic_action: string;
  batch_id?: string;
}
export function buildStepUpVerificationPayload(
  input: StepUpVerificationPayloadInput
): StepUpVerificationPayload;
export const STEP_UP_VERIFICATION_PATH: "/api/v1/auth/signature-verification";
export function resolveDefaultSemanticAction(action?: string | null): string;
export interface StepUpSignatureRequestInput {
  username?: string | null;
  password?: string;
  totp?: string | null;
  action: string;
  semanticAction?: string;
  semantic_action?: string;
  batchId?: string | null;
  batch_id?: string | null;
}
export interface StepUpSignatureRequestOptions<
  TResponse = { sig_token: string; expires_in?: number },
> {
  post: (path: string, body: StepUpVerificationPayload) => Promise<TResponse>;
  requireSigToken?: boolean;
  missingTokenMessage?: string;
}
export function requestStepUpSignature<
  TResponse = { sig_token: string; expires_in?: number },
>(
  input: StepUpSignatureRequestInput,
  options: StepUpSignatureRequestOptions<TResponse>
): Promise<TResponse>;

export const SIGNATURE_OTHER_REASON: "Other";
export const SIGNATURE_PASSWORD_INPUT_ATTRS: Readonly<{
  type: "password";
  inputmode: "text";
  autocomplete: "current-password";
  autocapitalize: "off";
  autocorrect: "off";
  spellcheck: "false";
}>;
export function requiresCustomSignatureReason(
  selectedReason: string | null | undefined
): boolean;
export function resolveSignatureReason(
  selectedReason: string | null | undefined,
  customReason?: string | null
): { valid: boolean; reason: string; error: string | null };
export function deriveSignerRoleFromClaims(
  claimsOrRoles: string[] | string | Record<string, any> | null | undefined,
  fallback?: string
): string;

export const DEFAULT_AUTH_RETURN_URL_KEY: string;
export function sanitizeReturnUrl(
  rawUrl: string | null | undefined,
  options?: { baseOrigin?: string }
): string | null;
export function saveAuthReturnUrl(
  url: string | null | undefined,
  options?: { storage?: Storage | null; key?: string; baseOrigin?: string }
): string | null;
export function peekAuthReturnUrl(options?: {
  storage?: Storage | null;
  key?: string;
  baseOrigin?: string;
}): string | null;
export function consumeAuthReturnUrl(options?: {
  storage?: Storage | null;
  key?: string;
  baseOrigin?: string;
}): string | null;
export function clearAuthReturnUrl(options?: {
  storage?: Storage | null;
  key?: string;
}): void;
export function resolveOidcRedirectUri(
  targetPathOrUrl?: string | null,
  options?: { origin?: string; basePath?: string }
): string;
export function isStepUpAuthFailure(
  url: string | URL | null | undefined,
  status: number,
  errorBody?: any
): boolean;

export interface OidcTokenRefreshResult {
  ok: boolean;
  refreshed: boolean;
  token: string | null;
  error?: any;
}

export interface OidcSessionCoordinator {
  attachKeycloak: (keycloakInstance: any) => any;
  resolveKeycloak: () => any;
  ensureFreshToken: (minValidity?: number) => Promise<OidcTokenRefreshResult>;
  refreshOnUnauthorized: (options?: {
    returnUrl?: string | null;
  }) => Promise<OidcTokenRefreshResult>;
  markSessionExpired: (options?: {
    reason?: string;
    error?: any;
    returnUrl?: string | null;
  }) => string | null;
  markAuthenticated: (token?: string | null) => void;
  beginVoluntaryLogout: () => void;
  reset: () => void;
  isSessionExpired: () => boolean;
  isRefreshing: () => boolean;
  saveReturnUrl: (url: string | null | undefined) => string | null;
  peekReturnUrl: () => string | null;
  consumeReturnUrl: () => string | null;
}

export function createOidcSessionCoordinator(options?: {
  getKeycloak?: () => any;
  minValiditySeconds?: number;
  forceRefreshMinValiditySeconds?: number;
  onTokenRefreshed?: (token: string | null, keycloak: any) => void;
  onSessionExpired?: (context: {
    reason: string;
    error?: any;
    returnUrl: string | null;
  }) => void;
  onAuthLogout?: () => void;
  getCurrentReturnUrl?: () => string | null;
  storage?: Storage | null;
  returnUrlKey?: string;
}): OidcSessionCoordinator;

export interface ScopeAssignmentClaims {
  assigned_studies?: unknown;
  assignedStudies?: unknown;
  study_ids?: unknown;
  studies?: unknown;
  study_id?: unknown;
  studyId?: unknown;
  assigned_sites?: unknown;
  assignedSites?: unknown;
  site_ids?: unknown;
  sites?: unknown;
  site_id?: unknown;
  siteId?: unknown;
  [key: string]: unknown;
}

export interface CallerScopeGuidanceContext {
  studyId?: string;
  siteId?: string;
  assignedStudies?: readonly string[];
  assignedSites?: readonly string[];
  hasAllStudiesScope?: boolean;
}

export function parseClaimStringList(raw: unknown): string[];
export function decodeJwtPayloadClaims(
  token: string | null | undefined
): Record<string, unknown> | null;
export function extractAssignedStudies(
  claimsOrToken?: Record<string, unknown> | string | null
): {
  assignedStudies: string[];
  hasAllStudiesScope: boolean;
};
export function extractAssignedSites(
  claimsOrToken?: Record<string, unknown> | string | null
): string[];
export function extractPrimaryStudyId(
  claimsOrToken?: Record<string, unknown> | string | null
): string | null;
export function extractPrimarySiteId(
  claimsOrToken?: Record<string, unknown> | string | null
): string | null;
export function isForbiddenScopeError(err: unknown): boolean;
export function formatScopeForbiddenGuidance(
  err: unknown,
  context?: CallerScopeGuidanceContext
): string;

export interface ClinicalBreadcrumbItem {
  id?: string;
  label: string;
  href?: string;
  current?: boolean;
  onSelect?: (item: ClinicalBreadcrumbItem, event: MouseEvent) => void;
}

export const ClinicalBreadcrumbs: DefineComponent<
  {
    items?: ClinicalBreadcrumbItem[];
    ariaLabel?: string;
  },
  {},
  any
>;

export interface EntityLinkNodeRef {
  type: string;
  id: string;
  label?: string;
  studyId?: string | null;
  siteId?: string | null;
}

export interface ParsedNodeRef {
  raw: string;
  masked: boolean;
  kind: string;
  kindLabel: string;
  id: string | null;
  version: number | null;
  maskReason: string | null;
  shortRef: string;
  displayLabel: string;
}

export const EntityLink: DefineSlotsComponent<
  {
    nodeRef?: string | EntityLinkNodeRef | null;
    entityType?: string;
    entityId?: string;
    label?: string;
    srContext?: string;
    href?: string;
    preventNavigation?: boolean;
    tabular?: boolean;
    compact?: boolean;
    showCode?: boolean;
    onPeek?: (payload: any) => void;
    onSelect?: (payload: any) => void;
  },
  {
    default?: (props: Record<string, never>) => any;
  }
>;

export const ClinicalExplanation: DefineComponent<
  {
    explanation?: Record<string, any> | null;
    fallbackReason?: string;
    code?: string;
    compact?: boolean;
    onPeek?: (payload: any) => void;
  },
  {},
  any
>;

export const ClinicalGatedAction: DefineSlotsComponent<
  {
    permitted?: boolean;
    explanation?: Record<string, any> | null;
    reason?: string;
    code?: string;
    label?: string;
    whyLabel?: string;
    inline?: boolean;
    defaultExpanded?: boolean;
    onAction?: () => void;
    onPeek?: (payload: any) => void;
  },
  {
    default?: (props: {
      disabled: boolean;
      permitted: boolean;
      ariaDisabled?: "true";
      explanationId?: string;
    }) => any;
  }
>;

export const ClinicalIcon: DefineComponent<
  {
    name?: string;
    size?: number | string;
  },
  {},
  any
>;

export function parseNodeRef(raw: string): ParsedNodeRef;
export function stripRawPermissionTokens(text: string): string;
export function registerEntityPeekHandler(
  handler: (payload: Record<string, any>) => void
): () => void;
export function openEntityPeek(
  nodeRef: string,
  context?: Record<string, any>
): boolean;
