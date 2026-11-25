interface ImportMetaGlobOptions {
  eager?: boolean;
  import?: string;
}

interface ImportMeta {
  glob<T = unknown>(
    pattern: string | string[],
    options: { eager: true; import?: string }
  ): Record<string, T>;
  glob<T = unknown>(
    pattern: string | string[],
    options?: ImportMetaGlobOptions
  ): Record<string, () => Promise<T>>;
}

declare module "*.vue" {
  import type { DefineComponent } from "vue";
  const component: DefineComponent<
    Record<string, unknown>,
    Record<string, unknown>,
    unknown
  >;
  export default component;
}
