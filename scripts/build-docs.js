const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');

/**
 * Preflight checks to verify required executables are on PATH.
 */
function runPreflightChecks() {
  console.log('--- Preflight Tool-Availability Check ---');
  const tools = [
    { name: 'node', cmd: 'node -v', desc: 'Node.js runtime environment', expected: 'expected to be provided by Node.js installer or nvm' },
    { name: 'python3', cmd: 'python3 --version', desc: 'Python 3 interpreter', expected: 'expected to be provided by Python 3 installer or your system package manager' },
    { name: 'pnpm', cmd: 'pnpm -v', desc: 'pnpm package manager', expected: 'expected to be provided via npm install -g pnpm or system package manager' }
  ];

  for (const tool of tools) {
    try {
      execSync(tool.cmd, { stdio: 'ignore', cwd: repoRoot });
    } catch (err) {
      console.error(`\n[ERROR] Missing required tool: "${tool.name}" (${tool.desc}).`);
      console.error(`How it's expected to be provided: ${tool.expected}. Please install it and try again.`);
      process.exit(1);
    }
  }
  console.log('Preflight checks successful. All tools are available.\n');
}

/**
 * Executes a shell command from the application directory.
 * @param {string} command - The shell command to execute.
 */
function runCommand(command) {
  console.log(`Running: ${command}`);
  try {
    // Use repoRoot for cwd to keep the execution portable and environment-agnostic
    execSync(command, { stdio: 'inherit', cwd: repoRoot });
  } catch (error) {
    console.error(`Command failed: ${command}`);
    throw error;
  }
}

/**
 * Rewrites relative links inside staged markdown documents.
 * @param {string} content - The markdown file content.
 * @returns {string} The updated markdown content with corrected links.
 */
function rewriteLinks(content) {
  // Inline links [text](link)
  let rewritten = content.replace(/(\[(?:[^\]]|\\\])*\]\()([^)]+)(\))/g, (match, prefix, url, suffix) => {
    let cleanUrl = url.trim();
    // Only modify relative local links. If it starts with http, https, mailto, or #, keep it as-is.
    if (
      cleanUrl.startsWith('http://') ||
      cleanUrl.startsWith('https://') ||
      cleanUrl.startsWith('mailto:') ||
      cleanUrl.startsWith('#')
    ) {
      return match;
    }

    // If the relative link starts with docs/, strip 'docs/' prefix
    if (cleanUrl.startsWith('docs/')) {
      cleanUrl = cleanUrl.substring(5); // strip 'docs/'
    } else if (cleanUrl === 'LICENSE') {
      // If it points to LICENSE, change it to LICENSE.md
      cleanUrl = 'LICENSE.md';
    }

    return `${prefix}${cleanUrl}${suffix}`;
  });

  // Reference links [id]: link
  rewritten = rewritten.replace(/^(\[[^\]]+\]:\s*)(\S+)/gm, (match, prefix, url) => {
    let cleanUrl = url.trim();
    if (
      cleanUrl.startsWith('http://') ||
      cleanUrl.startsWith('https://') ||
      cleanUrl.startsWith('mailto:') ||
      cleanUrl.startsWith('#')
    ) {
      return match;
    }

    if (cleanUrl.startsWith('docs/')) {
      cleanUrl = cleanUrl.substring(5);
    } else if (cleanUrl === 'LICENSE') {
      cleanUrl = 'LICENSE.md';
    }

    return `${prefix}${cleanUrl}`;
  });

  return rewritten;
}

/**
 * Copies a source markdown file to docs directory with link preprocessing.
 * @param {string} srcName - The source file name in repo root.
 * @param {string} destName - The destination file name in docs/ folder.
 */
function copyAndPreprocess(srcName, destName) {
  const srcPath = path.join(repoRoot, srcName);
  const destPath = path.join(repoRoot, 'docs', destName);
  let content = fs.readFileSync(srcPath, 'utf8');
  content = rewriteLinks(content);
  fs.writeFileSync(destPath, content, 'utf8');
  console.log(`Successfully prepared and preprocessed ${srcName} -> docs/${destName}`);
}

// Run preflight checks before Step 1
runPreflightChecks();

try {
  // 1. Run validation scripts
  console.log('--- Step 1: Pre-Build Validation ---');
  runCommand('node scripts/check-links.js');
  runCommand('python3 scripts/validate_adrs.py');
  runCommand('python3 scripts/validate_markdown.py');

  // 2. Run compliance compiler
  console.log('--- Step 2: Running Compliance Tracer ---');
  runCommand('python3 scripts/generate_rtm.py');

  // 3. Prepare files for VitePress
  console.log('--- Step 3: Preparing Documentation Files ---');
  // Copy files with link preprocessing instead of straight copy to fix relative links
  copyAndPreprocess('README.md', 'index.md');
  copyAndPreprocess('ARCHITECTURE.md', 'ARCHITECTURE.md');
  copyAndPreprocess('AGENTS.md', 'AGENTS.md');

  // Convert plain-text LICENSE to docs/LICENSE.md
  const licenseSrcPath = path.join(repoRoot, 'LICENSE');
  const licenseDestPath = path.join(repoRoot, 'docs', 'LICENSE.md');
  if (fs.existsSync(licenseSrcPath)) {
    const licenseContent = fs.readFileSync(licenseSrcPath, 'utf8');
    const licenseMarkdown = `# Project License\n\n\`\`\`text\n${licenseContent}\n\`\`\`\n`;
    fs.writeFileSync(licenseDestPath, licenseMarkdown, 'utf8');
    console.log(`Successfully converted plain-text LICENSE to markdown at docs/LICENSE.md`);
  } else {
    console.warn(`Plain-text LICENSE file not found at ${licenseSrcPath}`);
  }

  // 4. Build VitePress static portal
  console.log('--- Step 4: Compiling VitePress Static Portal ---');
  runCommand('pnpm vitepress build docs');

  console.log('--- Docs Build Completed Successfully! ---');
} catch (error) {
  console.error('Docs build pipeline failed.');
  process.exit(1);
} finally {
  console.log('--- Cleanup Temporary Docs ---');
  // Derive temporary docs files relative to repoRoot for cleanup
  for (const file of ['index.md', 'ARCHITECTURE.md', 'AGENTS.md', 'LICENSE.md']) {
    const filePath = path.join(repoRoot, 'docs', file);
    if (fs.existsSync(filePath)) {
      try {
        fs.unlinkSync(filePath);
        console.log(`Cleaned up ${filePath}`);
      } catch (err) {
        console.warn(`Could not clean up ${filePath}:`, err);
      }
    }
  }
}
