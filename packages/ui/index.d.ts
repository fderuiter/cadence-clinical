import { DefineComponent, DefineSetupFnComponent, SlotsType } from "vue";

type DefineSlotsComponent<
  Props extends Record<string, any>,
  Slots extends Record<string, any>,
  Exposed extends Record<string, any> = {},
> = DefineSetupFnComponent<Props, Record<string, any>, SlotsType<Slots>> &
  (new () => Exposed);

// Export generated CDASH types
export * from "./src/types/cdash.js";

// Vue Components
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

export interface ClinicalGovernanceGateItem {
  key?: string;
  label: string;
  count?: number;
  testId?: string;
}

export interface ClinicalGovernanceBannerProps {
  id?: string;
  status?: "unlocked" | "frozen" | "locked" | "ready" | "blocked" | "loading";
  statusLabel?: string;
  statusTestId?: string;
  summary?: string;
  summaryTestId?: string;
  gates?: ClinicalGovernanceGateItem[];
  gatesTestId?: string;
  blockerCount?: number;
  showBlockerAction?: boolean;
  blockerActionLabel?: string;
  blockerActionId?: string;
  blockerActionTestId?: string;
  blockerDrawerTitle?: string;
  blockerDrawerSubtitle?: string;
  showManageAction?: boolean;
  manageActionLabel?: string;
  manageActionId?: string;
  manageActionTestId?: string;
  manageDrawerTitle?: string;
  manageDrawerSubtitle?: string;
  showExportAction?: boolean;
  exportActionLabel?: string;
  exportActionId?: string;
  exportActionTestId?: string;
  exportDrawerTitle?: string;
  exportDrawerSubtitle?: string;
  modelValue?: string | null;
  activeSheet?: string | null;
  drawerId?: string;
  drawerOverlayId?: string;
  drawerTitle?: string;
  drawerSubtitle?: string;
  drawerSize?: "sm" | "md" | "lg" | "xl" | string;
  drawerDismissible?: boolean;
  closeLabel?: string;
  teleport?: boolean | string;
  mobileBottomSheet?: boolean;
  ariaLabel?: string;
}

export const ClinicalGovernanceBanner: DefineSlotsComponent<
  ClinicalGovernanceBannerProps,
  {
    status?: (props: { status: string; statusLabel: string }) => any;
    summary?: () => any;
    gates?: (props: { gates: ClinicalGovernanceGateItem[] }) => any;
    actions?: (props: {
      activeSheet: string | null;
      openSheet: (sheet: string | null, trigger?: any) => void;
      openBlockers: (event?: Event) => void;
      openLocks: (event?: Event) => void;
      openExport: (event?: Event) => void;
      closeDrawer: () => void;
    }) => any;
    outcome?: () => any;
    blockers?: (props: { close: () => void }) => any;
    locks?: (props: { close: () => void }) => any;
    export?: (props: { close: () => void }) => any;
    drawer?: (props: { activeSheet: string | null; close: () => void }) => any;
  },
  {
    openSheet: (sheet: string | null, trigger?: any) => void;
    openBlockers: (event?: Event) => void;
    openLocks: (event?: Event) => void;
    openExport: (event?: Event) => void;
    closeDrawer: () => void;
    currentSheet: import("vue").ComputedRef<string | null>;
  }
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

export const ClinicalIconButton: DefineSlotsComponent<
  {
    label: string;
    icon?: ClinicalIconName | (string & {});
    type?: "button" | "submit" | "reset";
    variant?: "unstyled" | "ghost" | "secondary" | "danger";
    size?: "sm" | "md" | "lg";
    disabled?: boolean;
    title?: string;
  },
  {
    default?: (props: Record<string, never>) => any;
  }
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
    live?: boolean;
    ariaLabel?: string;
  },
  {
    default?: (props: Record<string, never>) => any;
    icon?: (props: Record<string, never>) => any;
    action?: (props: Record<string, never>) => any;
  }
>;

export type ClinicalLiveRegionPoliteness = "polite" | "assertive";
export type ClinicalLiveRegionVariant =
  "strip" | "banner" | "inline" | "sr-only";
export type ClinicalLiveRegionTone =
  "neutral" | "info" | "success" | "warning" | "error";

export const ClinicalLiveRegion: DefineSlotsComponent<
  {
    message?: string;
    politeMessage?: string;
    assertiveMessage?: string;
    politeness?: ClinicalLiveRegionPoliteness | string;
    variant?: ClinicalLiveRegionVariant | string;
    tone?: ClinicalLiveRegionTone | string;
    icon?: ClinicalIconName | "none" | (string & {});
    active?: boolean;
    atomic?: boolean;
    tabularNums?: boolean;
    id?: string;
    testId?: string;
    persistentId?: boolean;
    liveClass?: string | any[] | Record<string, boolean>;
    containerClass?: string | any[] | Record<string, boolean>;
  },
  {
    default?: (props: Record<string, never>) => any;
    pills?: (props: Record<string, never>) => any;
    actions?: (props: Record<string, never>) => any;
  }
>;

export type StatusAnnouncerSource =
  | import("vue").Ref<string | null | undefined>
  | import("vue").ComputedRef<string | null | undefined>
  | (() => string | null | undefined)
  | null;

export interface StatusAnnouncerOptions {
  debounceMs?: number;
  reannounceDelayMs?: number;
  initialPolite?: string;
  initialAssertive?: string;
  statusSource?: StatusAnnouncerSource;
  errorSource?: StatusAnnouncerSource;
  announceOnMount?: boolean;
}

export interface StatusAnnounceCallOptions {
  politeness?: ClinicalLiveRegionPoliteness;
  immediate?: boolean;
  debounceMs?: number;
}

export function useStatusAnnouncer(options?: StatusAnnouncerOptions): {
  politeMessage: import("vue").Ref<string>;
  assertiveMessage: import("vue").Ref<string>;
  announce: (message: string, options?: StatusAnnounceCallOptions) => void;
  announcePolite: (
    message: string,
    options?: Omit<StatusAnnounceCallOptions, "politeness">
  ) => void;
  announceAssertive: (
    message: string,
    options?: Omit<StatusAnnounceCallOptions, "politeness">
  ) => void;
  clear: (channel?: ClinicalLiveRegionPoliteness) => void;
  clearStatus: () => void;
  clearAssertive: () => void;
};

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
    keepMounted?: boolean;
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
    footer?: (props: { close: () => void }) => any;
    "after-panel"?: (props: Record<string, never>) => any;
  },
  {
    drawerRef: HTMLElement | null;
  }
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
    loading?: boolean;
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
    clearAllLabel?: string;
    position?: "bottom-right" | "top-right" | string;
    latestToastId?: string;
    manageTimers?: boolean;
    onDismiss?: (id: string | number) => void;
    onAction?: (toast: ClinicalToastItem) => void;
    onPause?: (id: string | number) => void;
    onResume?: (id: string | number) => void;
    onClear?: () => void;
  },
  {
    extra?: (props: { toast: any }) => any;
  }
>;

export interface VisualViewportBounds {
  left: number;
  top: number;
  width: number;
  height: number;
  right: number;
  bottom: number;
}

export interface FloatingPositionInput {
  anchorRect?: {
    left?: number;
    top?: number;
    right?: number;
    bottom?: number;
    width?: number;
    height?: number;
  } | null;
  floatingSize?: {
    width?: number;
    height?: number;
  } | null;
  viewport?: Partial<VisualViewportBounds>;
  placement?: "bottom" | "top" | string;
  align?: "start" | "center" | "end" | string;
  offset?: number;
  margin?: number;
  flip?: boolean;
  preferredWidth?: number | null;
  minHeight?: number;
}

export interface FloatingPositionResult {
  left: number;
  top: number;
  maxWidth: number;
  maxHeight: number;
  placement: "bottom" | "top";
}

export function getVisualViewportBounds(): VisualViewportBounds;

export function observeVisualViewport(
  onUpdate: () => void,
  options?: { trackScroll?: boolean }
): () => void;

export function useViewportMaxHeight(options?: {
  shortViewportThreshold?: number;
  shortTopOffset?: number;
  maxTopOffset?: number;
  topOffsetRatio?: number;
  bottomMargin?: number;
  minHeight?: number;
}): {
  maxHeightPx: import("vue").Ref<number | null>;
  maxHeightStyle: import("vue").ComputedRef<{ maxHeight: string } | undefined>;
  syncViewportMaxHeight: () => void;
};

export function computeFloatingPosition(
  options?: FloatingPositionInput
): FloatingPositionResult;

export function useFloatingPosition(
  anchorRef:
    | import("vue").Ref<Element | null | undefined>
    | (() => Element | null | undefined)
    | Element
    | null
    | undefined,
  floatingRef:
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined),
  options?: {
    isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
    anchorRect?:
      | FloatingPositionInput["anchorRect"]
      | (() => FloatingPositionInput["anchorRect"]);
    placement?:
      import("vue").Ref<string> | (() => string) | "bottom" | "top" | string;
    align?:
      | import("vue").Ref<string>
      | (() => string)
      | "start"
      | "center"
      | "end"
      | string;
    offset?: import("vue").Ref<number> | (() => number) | number;
    margin?: import("vue").Ref<number> | (() => number) | number;
    flip?: import("vue").Ref<boolean> | (() => boolean) | boolean;
    preferredWidth?:
      import("vue").Ref<number | null> | (() => number | null) | number | null;
    minHeight?: import("vue").Ref<number> | (() => number) | number;
  }
): {
  coordinates: import("vue").Ref<FloatingPositionResult>;
  floatingStyle: import("vue").ComputedRef<Record<string, string>>;
  placement: import("vue").ComputedRef<"bottom" | "top">;
  updatePosition: () => FloatingPositionResult;
};

export const ClinicalPopover: DefineSlotsComponent<
  {
    modelValue?: boolean;
    isOpen?: boolean;
    open?: boolean;
    anchor?:
      | import("vue").Ref<Element | null | undefined>
      | (() => Element | null | undefined)
      | Element
      | null;
    placement?: "bottom" | "top" | string;
    align?: "start" | "center" | "end" | string;
    offset?: number;
    viewportMargin?: number;
    preferredWidth?: number | null;
    minHeight?: number;
    flip?: boolean;
    teleport?: boolean | string;
    id?: string;
    role?: string;
    ariaLabel?: string;
    ariaLabelledby?: string;
    closeOnEscape?: boolean;
    closeOnOutsidePointer?: boolean;
    restoreFocusOnEscape?: boolean;
    hoverBridge?: boolean;
    popoverClass?: string | any[] | Record<string, boolean>;
    "onUpdate:modelValue"?: (value: boolean) => void;
    onClose?: (payload: { reason: "escape" | "outside" | string }) => void;
    onMouseenter?: (event: MouseEvent) => void;
    onMouseleave?: (event: MouseEvent) => void;
    onFocusin?: (event: FocusEvent) => void;
    onFocusout?: (event: FocusEvent) => void;
  },
  {
    default?: (props: Record<string, never>) => any;
  },
  {
    popoverRef: HTMLElement | null;
    scrollRef: HTMLElement | null;
    placement: "bottom" | "top";
    updatePosition: () => FloatingPositionResult;
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
  stickyOffset?: number;
  scrollTarget?: InlineFocusCandidate;
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
  stickyOffset?: number;
  scrollTarget?: InlineFocusCandidate;
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

export interface HandoffRetryFocusOptions extends HandoffFocusOptions {
  retryScope?: InlineFocusCandidate;
  alertElement?:
    | HTMLElement
    | import("vue").Ref<HTMLElement | null | undefined>
    | (() => HTMLElement | null | undefined)
    | null;
  errorTargets?: InlineFocusCandidate | InlineFocusCandidate[];
  successTargets?: InlineFocusCandidate | InlineFocusCandidate[];
  recoveredTargets?: InlineFocusCandidate | InlineFocusCandidate[];
  isError?: () => boolean;
  hasError?: () => boolean;
}

export type RetryFocusHandoffOptions = HandoffRetryFocusOptions;

export function handoffRetryFocus(
  action?: (() => unknown | Promise<unknown>) | null,
  options?: HandoffRetryFocusOptions
): Promise<HTMLElement | null>;

export interface RetryFocusHandoffControls {
  retryWithFocus: (
    action?: (() => unknown | Promise<unknown>) | null,
    overrideOptions?: HandoffRetryFocusOptions
  ) => Promise<HTMLElement | null>;
}

export function useRetryFocusHandoff(
  defaultOptions?: HandoffRetryFocusOptions
): RetryFocusHandoffControls;

export function useEscapeClose(
  onCloseCallback: (event: KeyboardEvent) => void,
  options?: {
    isOpen?: import("vue").Ref<boolean> | (() => boolean) | boolean;
    target?: EventTarget | null;
  }
): {
  stop: () => void;
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
export function initHoverDetection(): void;

export function normalizeApprovedConsent(approved: any): any;
export function shapeComprehensionAnswers(answers: any): any;
export function interpretComprehensionResult(result: any): any;

export function canonicalSerialize(obj: any): string;
export function generateCanonicalSignature(
  obj: any,
  key: string | Uint8Array
): Promise<string>;
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
  CGT_BEDSIDE_VERIFY: "execution.cgt.bedside_verify";
  CGT_DESTRUCTION_CERTIFICATE: "execution.cgt.destruction_certificate";
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
export function formatCgtBedsideBindingId(input?: {
  studyId?: string | null;
  subjectId?: string | null;
  din?: string | null;
  batchId?: string | null;
}): string;
export function formatCgtDestructionBindingId(input?: {
  studyId?: string | null;
  siteId?: string | null;
  lotNumber?: string | null;
  din?: string | null;
}): string;
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

export type ClinicalShellIconName =
  "bell" | "globe" | "help-circle" | "info" | "menu" | "search" | "x";

export type ClinicalRouteIconName =
  | "alert-circle"
  | "alert-triangle"
  | "book-open"
  | "calendar"
  | "camera"
  | "check"
  | "check-circle"
  | "chevron-down"
  | "chevron-left"
  | "chevron-right"
  | "chevron-up"
  | "clipboard-list"
  | "clock"
  | "copy"
  | "download"
  | "edit"
  | "external-link"
  | "eye"
  | "file-signature"
  | "file-text"
  | "flask-conical"
  | "folder"
  | "lock"
  | "mail"
  | "message-square"
  | "mic"
  | "pause"
  | "phone"
  | "play"
  | "plus"
  | "refresh"
  | "rotate-ccw"
  | "shield-check"
  | "sparkles"
  | "stethoscope"
  | "ticket"
  | "trash"
  | "trending-up"
  | "unlock"
  | "upload"
  | "users"
  | "video"
  | "wallet"
  | "x-circle";

export type ClinicalIconName = ClinicalShellIconName | ClinicalRouteIconName;

export const CLINICAL_ICON_STROKE_WIDTH: 1.75;
export const CLINICAL_SHELL_ICON_NAMES: readonly ClinicalShellIconName[];
export const CLINICAL_ROUTE_ICON_NAMES: readonly ClinicalRouteIconName[];
export const CLINICAL_ICON_NAMES: readonly ClinicalIconName[];
export const CLINICAL_SHELL_ICONS: Readonly<
  Record<ClinicalShellIconName, import("vue").Component>
>;
export const CLINICAL_ROUTE_ICONS: Readonly<
  Record<ClinicalRouteIconName, import("vue").Component>
>;
export const CLINICAL_ICONS: Readonly<
  Record<ClinicalIconName, import("vue").Component>
>;

export function isClinicalShellIconName(
  name: unknown
): name is ClinicalShellIconName;
export function isClinicalRouteIconName(
  name: unknown
): name is ClinicalRouteIconName;
export function isClinicalIconName(name: unknown): name is ClinicalIconName;
export function normalizeClinicalIconSize(size?: number | string): 16 | 20;
export function resolveClinicalIcon(
  name?: ClinicalIconName | (string & {})
): import("vue").Component;
export function renderClinicalIconSvg(
  name: ClinicalIconName | (string & {}),
  options?: {
    size?: number | string;
    className?: string;
  }
): string;

export const ClinicalIcon: DefineComponent<
  {
    name?: ClinicalIconName | (string & {});
    size?: 16 | 20 | number | string;
  },
  {},
  any
>;

export type {
  AppliedThemeState,
  ResolvedTheme,
  ThemeControllerOptions,
  ThemePreference,
  ThemeSyncState,
} from "./theme.js";

export const THEME_STORAGE_KEY: "vitepress-theme-appearance";
export const THEME_MODES: readonly ["light", "dark", "auto"];
export const PREFERS_DARK_MEDIA_QUERY: "(prefers-color-scheme: dark)";
export const THEME_CHANGE_EVENT: "cadence:theme-change";

export function normalizeThemePreference(
  rawValue: unknown
): import("./theme.js").ThemePreference;
export function readStoredThemePreference(
  options?: import("./theme.js").ThemeControllerOptions
): import("./theme.js").ThemePreference;
export function writeStoredThemePreference(
  preference: unknown,
  options?: import("./theme.js").ThemeControllerOptions
): import("./theme.js").ThemePreference;
export function detectSystemDarkPreference(win?: Window | null): boolean;
export function resolveEffectiveTheme(
  preference: unknown,
  systemPrefersDark?: boolean
): import("./theme.js").ResolvedTheme;
export function cycleThemePreference(
  currentPreference: unknown
): import("./theme.js").ThemePreference;
export function applyThemeToDocument(
  preference: unknown,
  options?: {
    doc?: Document | null;
    win?: Window | null;
    systemPrefersDark?: boolean;
    emit?: boolean;
  }
): import("./theme.js").AppliedThemeState;
export function bootstrapTheme(
  options?: import("./theme.js").ThemeControllerOptions
): import("./theme.js").AppliedThemeState;
export function subscribeThemeSync(
  onUpdate?: (state: import("./theme.js").ThemeSyncState) => void,
  options?: import("./theme.js").ThemeControllerOptions
): () => void;

export const ClinicalThemeToggle: DefineComponent<
  {
    id?: string;
    density?: "compact" | "touch";
    variant?: "cycle" | "segmented";
    showLabel?: boolean;
    groupAriaLabel?: string;
    onChange?: (mode: "light" | "dark" | "auto") => void;
  },
  {},
  any
>;

export function useTheme(
  options?: import("./theme.js").ThemeControllerOptions
): {
  preference: import("vue").WritableComputedRef<
    import("./theme.js").ThemePreference
  >;
  theme: import("vue").WritableComputedRef<
    import("./theme.js").ThemePreference
  >;
  resolvedTheme: import("vue").ComputedRef<import("./theme.js").ResolvedTheme>;
  isDark: import("vue").ComputedRef<boolean>;
  systemPrefersDark: import("vue").ComputedRef<boolean>;
  modes: readonly ["light", "dark", "auto"];
  setTheme: (nextPreference: unknown) => import("./theme.js").ThemePreference;
  cycleTheme: () => import("./theme.js").ThemePreference;
  syncTheme: () => import("./theme.js").AppliedThemeState;
  stop: () => void;
};

export function parseNodeRef(raw: string): ParsedNodeRef;
export function stripRawPermissionTokens(text: string): string;
export function registerEntityPeekHandler(
  handler: (payload: Record<string, any>) => void
): () => void;
export function openEntityPeek(
  nodeRef: string,
  context?: Record<string, any>
): boolean;
