import crypto from "crypto";

export function buildPortalCspContent(inlineScriptHashes = []) {
  const scriptSources = ["'self'", ...inlineScriptHashes].join(" ");
  return [
    "default-src 'self'",
    `script-src ${scriptSources}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    "media-src 'self'",
    "worker-src 'self'",
    "connect-src 'self'",
    "frame-src 'self'",
    "manifest-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join("; ");
}

export function injectHashedPortalCsp(html) {
  const inlineHashes = new Set();
  const inlineScriptRegex =
    /<script\b(?![^>]*\bsrc\s*=)[^>]*>([\s\S]*?)<\/script>/gi;
  let match;
  while ((match = inlineScriptRegex.exec(html)) !== null) {
    const scriptBody = match[1];
    if (scriptBody.length > 0) {
      const digest = crypto
        .createHash("sha256")
        .update(scriptBody, "utf8")
        .digest("base64");
      inlineHashes.add(`'sha256-${digest}'`);
    }
  }

  const cspTag = `<meta http-equiv="Content-Security-Policy" content="${buildPortalCspContent([...inlineHashes])}">`;
  const existingCspRegex =
    /\s*<meta\b[^>]*\bhttp-equiv\s*=\s*(?:"Content-Security-Policy"|'Content-Security-Policy'|Content-Security-Policy)[^>]*>/gi;
  const strippedHtml = html.replace(existingCspRegex, "");

  if (/<head\b[^>]*>/i.test(strippedHtml)) {
    return strippedHtml.replace(
      /<head\b[^>]*>/i,
      (headOpen) => `${headOpen}\n    ${cspTag}`
    );
  }

  return `${cspTag}\n${strippedHtml}`;
}
