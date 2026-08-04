/**
 * Resolves a static asset path by dynamically prefixing the configured application
 * base path (import.meta.env.BASE_URL) and using the native browser URL constructor
 * to normalize slashes and resolve paths.
 *
 * @param {string} assetPath - The relative path to the static asset (e.g., 'silent-check-sso.html')
 * @returns {string} The fully resolved absolute URL string.
 */
export function resolveAssetUrl(assetPath) {
  let base = import.meta.env?.BASE_URL;
  if (!base || base === "undefined" || base === "null") {
    base = "/";
  }

  const origin =
    typeof window !== "undefined" && window.location?.origin
      ? window.location.origin
      : "http://localhost";

  // Normalize base to always end with a slash
  const normalizedBase = base.endsWith("/") ? base : `${base}/`;

  // Normalize asset path to strip leading slash so standard native URL constructor resolves it relative to the base directory
  const normalizedAsset = assetPath.startsWith("/")
    ? assetPath.slice(1)
    : assetPath;

  // Construct cleanly using standard native browser URL APIs
  return new URL(normalizedAsset, new URL(normalizedBase, origin)).toString();
}
