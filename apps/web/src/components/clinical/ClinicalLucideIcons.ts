import { defineComponent, h, type VNode } from "vue";

function createLucideIcon(displayName: string, renderNodes: () => VNode[]) {
  return defineComponent({
    name: displayName,
    props: {
      size: {
        type: [Number, String],
        default: 18,
      },
    },
    setup(props) {
      return () =>
        h(
          "svg",
          {
            xmlns: "http://www.w3.org/2000/svg",
            width: props.size,
            height: props.size,
            viewBox: "0 0 24 24",
            fill: "none",
            stroke: "currentColor",
            "stroke-width": 2,
            "stroke-linecap": "round",
            "stroke-linejoin": "round",
            class: "lucide",
            "aria-hidden": "true",
          },
          renderNodes()
        );
    },
  });
}

export const AlertTriangle = createLucideIcon("LucideAlertTriangle", () => [
  h("path", {
    d: "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z",
  }),
  h("path", { d: "M12 9v4" }),
  h("path", { d: "M12 17h.01" }),
]);

export const Award = createLucideIcon("LucideAward", () => [
  h("circle", { cx: 12, cy: 8, r: 6 }),
  h("path", { d: "M15.477 12.89 17 22l-5-3-5 3 1.523-9.11" }),
]);

export const BookOpen = createLucideIcon("LucideBookOpen", () => [
  h("path", { d: "M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z" }),
  h("path", { d: "M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z" }),
]);

export const Check = createLucideIcon("LucideCheck", () => [
  h("path", { d: "M20 6 9 17l-5-5" }),
]);

export const CheckCircle2 = createLucideIcon("LucideCheckCircle2", () => [
  h("circle", { cx: 12, cy: 12, r: 10 }),
  h("path", { d: "m9 12 2 2 4-4" }),
]);

export const ChevronLeft = createLucideIcon("LucideChevronLeft", () => [
  h("path", { d: "m15 18-6-6 6-6" }),
]);

export const ChevronRight = createLucideIcon("LucideChevronRight", () => [
  h("path", { d: "m9 18 6-6-6-6" }),
]);

export const Clock = createLucideIcon("LucideClock", () => [
  h("circle", { cx: 12, cy: 12, r: 10 }),
  h("polyline", { points: "12 6 12 12 16 14" }),
]);

export const FileSignature = createLucideIcon("LucideFileSignature", () => [
  h("path", {
    d: "M20 19.5v.5a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8.5L18 5.5",
  }),
  h("path", { d: "M8 18h1" }),
  h("path", {
    d: "M18.42 9.61a2.1 2.1 0 1 1 2.97 2.97L16.95 17 13 18l.99-3.95 4.43-4.44Z",
  }),
]);

export const Loader2 = createLucideIcon("LucideLoader2", () => [
  h("path", { d: "M21 12a9 9 0 1 1-6.219-8.56" }),
]);

export const Lock = createLucideIcon("LucideLock", () => [
  h("rect", { width: 18, height: 11, x: 3, y: 11, rx: 2, ry: 2 }),
  h("path", { d: "M7 11V7a5 5 0 0 1 10 0v4" }),
]);

export const Package = createLucideIcon("LucidePackage", () => [
  h("path", {
    d: "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",
  }),
  h("path", { d: "M12 22V12" }),
  h("path", { d: "m3.3 7 7.703 4.734a2 2 0 0 0 1.994 0L20.7 7" }),
  h("path", { d: "m7.5 4.27 9 5.15" }),
]);

export const Plus = createLucideIcon("LucidePlus", () => [
  h("path", { d: "M5 12h14" }),
  h("path", { d: "M12 5v14" }),
]);

export const Printer = createLucideIcon("LucidePrinter", () => [
  h("path", {
    d: "M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2",
  }),
  h("path", { d: "M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6" }),
  h("rect", { x: 6, y: 14, width: 12, height: 8, rx: 1 }),
]);

export const RefreshCw = createLucideIcon("LucideRefreshCw", () => [
  h("path", { d: "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" }),
  h("path", { d: "M21 3v5h-5" }),
  h("path", { d: "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" }),
  h("path", { d: "M8 16H3v5" }),
]);

export const ScanBarcode = createLucideIcon("LucideScanBarcode", () => [
  h("path", { d: "M3 7V5a2 2 0 0 1 2-2h2" }),
  h("path", { d: "M17 3h2a2 2 0 0 1 2 2v2" }),
  h("path", { d: "M21 17v2a2 2 0 0 1-2 2h-2" }),
  h("path", { d: "M7 21H5a2 2 0 0 1-2-2v-2" }),
  h("path", { d: "M8 7v10" }),
  h("path", { d: "M12 7v10" }),
  h("path", { d: "M17 7v10" }),
]);

export const X = createLucideIcon("LucideX", () => [
  h("path", { d: "M18 6 6 18" }),
  h("path", { d: "m6 6 12 12" }),
]);
