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
  // Copy files using path.join and repoRoot instead of hardcoded /app paths to maintain portability
  fs.copyFileSync(path.join(repoRoot, 'README.md'), path.join(repoRoot, 'docs', 'index.md'));
  fs.copyFileSync(path.join(repoRoot, 'ARCHITECTURE.md'), path.join(repoRoot, 'docs', 'ARCHITECTURE.md'));
  fs.copyFileSync(path.join(repoRoot, 'AGENTS.md'), path.join(repoRoot, 'docs', 'AGENTS.md'));
  console.log('Successfully prepared README.md, ARCHITECTURE.md, and AGENTS.md');

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
  for (const file of ['index.md', 'ARCHITECTURE.md', 'AGENTS.md']) {
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
