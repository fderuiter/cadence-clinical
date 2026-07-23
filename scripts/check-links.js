const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');

function getMarkdownFiles(dir, fileList = []) {
  const files = fs.readdirSync(dir);
  for (const file of files) {
    const filePath = path.join(dir, file);
    if (
      filePath.includes('node_modules') ||
      filePath.includes('.venv') ||
      filePath.includes('.git') ||
      filePath.includes('.cache') ||
      filePath.includes('.pnpm-store')
    ) {
      continue;
    }
    const stat = fs.statSync(filePath);
    if (stat.isDirectory()) {
      getMarkdownFiles(filePath, fileList);
    } else if (file.endsWith('.md')) {
      fileList.push(filePath);
    }
  }
  return fileList;
}

function checkLinks() {
  const mdFiles = getMarkdownFiles(repoRoot);
  let hasError = false;
  let totalChecked = 0;
  let totalBroken = 0;

  const inlineLinkRegex = /\[(?:[^\]]|\\\])*\]\(([^)]+)\)/g;
  const refLinkRegex = /^\[([^\]]+)\]:\s*(\S+)/gm;

  for (const file of mdFiles) {
    const content = fs.readFileSync(file, 'utf8');
    const relativeDir = path.dirname(file);

    const checkPath = (linkTarget, lineNum) => {
      // Ignore web links, mailto, and anchor-only links
      if (
        linkTarget.startsWith('http://') ||
        linkTarget.startsWith('https://') ||
        linkTarget.startsWith('mailto:') ||
        linkTarget.startsWith('#')
      ) {
        return;
      }

      // Remove query and anchor from internal link
      const cleanTarget = linkTarget.split('#')[0].split('?')[0];
      if (!cleanTarget) {
        return; // Empty target after removing anchor
      }

      totalChecked++;

      // Resolve path
      let resolvedPath;
      if (cleanTarget.startsWith('/')) {
        resolvedPath = path.join(repoRoot, cleanTarget);
      } else {
        resolvedPath = path.resolve(relativeDir, cleanTarget);
      }

      if (!fs.existsSync(resolvedPath)) {
        console.error(`Broken link in ${path.relative(repoRoot, file)}:${lineNum} -> "${linkTarget}" (Resolved: "${path.relative(repoRoot, resolvedPath)}")`);
        hasError = true;
        totalBroken++;
      }
    };

    // Helper to find line number of a match
    const getLineNumber = (index) => {
      return content.substring(0, index).split('\n').length;
    };

    // Find inline links
    let match;
    inlineLinkRegex.lastIndex = 0;
    while ((match = inlineLinkRegex.exec(content)) !== null) {
      const target = match[1].trim();
      const lineNum = getLineNumber(match.index);
      checkPath(target, lineNum);
    }

    // Find reference-style links
    let refMatch;
    refLinkRegex.lastIndex = 0;
    while ((refMatch = refLinkRegex.exec(content)) !== null) {
      const target = refMatch[2].trim();
      const lineNum = getLineNumber(refMatch.index);
      checkPath(target, lineNum);
    }
  }

  console.log(`\nLink Check Summary:`);
  console.log(`Total files scanned: ${mdFiles.length}`);
  console.log(`Total relative links checked: ${totalChecked}`);
  console.log(`Total broken links found: ${totalBroken}`);

  if (hasError) {
    process.exit(1);
  } else {
    console.log('All internal relative links are valid! 🎉');
    process.exit(0);
  }
}

checkLinks();
