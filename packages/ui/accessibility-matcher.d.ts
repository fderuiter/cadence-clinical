export declare const WCAG_22_AA_TAGS: string[];

export interface ToBeAccessibleOptions {
  strictLayoutMode?: boolean;
  runOnly?: {
    type: string;
    values: string[];
  };
  rules?: Record<string, { enabled: boolean; [key: string]: unknown }>;
  [key: string]: unknown;
}

export declare function toBeAccessible(
  received: unknown,
  customOptions?: ToBeAccessibleOptions
): Promise<{
  pass: boolean;
  message: () => string;
}>;
