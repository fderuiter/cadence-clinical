import {
  AlertCircle,
  BookOpen,
  Camera,
  ClipboardList,
  ExternalLink,
  FileSignature,
  Info,
  Mail,
  Phone,
  RefreshCw,
  ScrollText,
  Search,
  TrendingUp,
  Video,
  Wallet,
} from "lucide-vue-next";
import { registerClinicalIcons } from "./clinicalCoreIcons.js";

export const PORTAL_CLINICAL_ICON_MAP = /* @__PURE__ */ Object.freeze({
  "alert-circle": AlertCircle,
  "book-open": BookOpen,
  camera: Camera,
  "clipboard-list": ClipboardList,
  "external-link": ExternalLink,
  "file-signature": FileSignature,
  info: Info,
  mail: Mail,
  phone: Phone,
  refresh: RefreshCw,
  "refresh-cw": RefreshCw,
  "scroll-text": ScrollText,
  search: Search,
  "trending-up": TrendingUp,
  video: Video,
  wallet: Wallet,
});

export function enablePortalClinicalIcons() {
  registerClinicalIcons(PORTAL_CLINICAL_ICON_MAP);
}
