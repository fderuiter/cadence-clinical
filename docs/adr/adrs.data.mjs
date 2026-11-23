import path from "path";
import { fileURLToPath } from "url";
import { loadAdrCatalog } from "../.vitepress/adr.mjs";

const currentDir = path.dirname(fileURLToPath(import.meta.url));
const docsDir = path.resolve(currentDir, "..");

export const data = [];

export default {
  watch: ["./*.md"],
  load() {
    return loadAdrCatalog(docsDir);
  },
};
