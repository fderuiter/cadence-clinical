import {
  Bell,
  Globe,
  HelpCircle,
  Info,
  Menu,
  Search,
  X,
} from "lucide-vue-next";
import {
  CLINICAL_SHELL_ICON_NAMES,
  isClinicalShellIconName,
} from "./iconNames.js";

export { CLINICAL_SHELL_ICON_NAMES, isClinicalShellIconName };

/**
 * Boot- and shell-scoped clinical icons shared by navigation chrome and overlays.
 * Kept minimal and separate from routeIcons.js so route closures stay lean.
 */
export const CLINICAL_SHELL_ICONS = {
  bell: Bell,
  globe: Globe,
  "help-circle": HelpCircle,
  info: Info,
  menu: Menu,
  search: Search,
  x: X,
};
