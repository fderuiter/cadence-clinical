import {
  Brain,
  Camera,
  ClipboardList,
  FileSignature,
  Info,
  Mail,
  PenTool,
  Phone,
  RefreshCw,
  Rocket,
  ScrollText,
  Search,
  TrendingUp,
  Upload,
  Video,
  Wallet,
} from "lucide-vue-next";
import { registerClinicalIcons } from "./clinicalCoreIcons.js";

export const PORTAL_CLINICAL_ICON_MAP = /* @__PURE__ */ Object.freeze({
  brain: Brain,
  camera: Camera,
  "clipboard-list": ClipboardList,
  "file-signature": FileSignature,
  info: Info,
  mail: Mail,
  "pen-tool": PenTool,
  phone: Phone,
  "refresh-cw": RefreshCw,
  rocket: Rocket,
  "scroll-text": ScrollText,
  search: Search,
  "trending-up": TrendingUp,
  upload: Upload,
  video: Video,
  wallet: Wallet,
});

export function enablePortalClinicalIcons() {
  registerClinicalIcons(PORTAL_CLINICAL_ICON_MAP);
}
