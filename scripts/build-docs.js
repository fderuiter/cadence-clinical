const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');

/**
 * Executes a shell command from the application directory.
 * @param {string} command - The shell command to execute.
 */
function runCommand(command) {
  console.log(`Running: ${command}`);
  try {
    execSync(command, { stdio: 'inherit', cwd: repoRoot });
  } catch (error) {
    console.error(`Command failed: ${command}`);
    throw error;
  }
}

try {
  // 1. Run validation scripts
  console.log('--- Step 1: Pre-Build Validation ---');
  runCommand('node scripts/check-links.js');
  runCommand('uv run python scripts/validate_adrs.py');
  runCommand('uv run python scripts/validate_markdown.py');

  // 2. Run compliance compiler
  console.log('--- Step 2: Running Compliance Tracer ---');
  runCommand('uv run python scripts/generate_rtm.py');

  // 3. Prepare files for VitePress
  console.log('--- Step 3: Preparing Documentation Files ---');
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
