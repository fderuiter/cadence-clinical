import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

/**
 * Builds domain-grouped sidebar entries for architectural decision records.
 * Parses `docs/adr/index.md` to extract domain headings and markdown links.
 *
 * @return {Array<{text: string, collapsed?: boolean, items: Array<{text: string, link: string}>}>} Grouped ADR sidebar entries.
 */
function getAdrSidebarGroups() {
  const indexPath = path.resolve(__dirname, '../adr/index.md')
  if (!fs.existsSync(indexPath)) return []

  const content = fs.readFileSync(indexPath, 'utf-8')
  const lines = content.split('\n')

  const groups = []
  let currentGroup = null

  for (const line of lines) {
    const headingMatch = line.match(/^###\s+(.*)/)
    if (headingMatch) {
      if (currentGroup && currentGroup.items.length > 0) {
        groups.push(currentGroup)
      }
      currentGroup = {
        text: headingMatch[1].trim(),
        collapsed: true,
        items: []
      }
      continue
    }

    const linkMatch = line.match(/^-\s*\[(.*?)\]\((.*?)\)/)
    if (linkMatch && currentGroup) {
      const text = linkMatch[1].trim()
      let filename = linkMatch[2].trim()
      if (filename.startsWith('docs/adr/')) {
        filename = filename.replace('docs/adr/', '')
      }
      if (filename.endsWith('.md')) {
        filename = filename.replace('.md', '')
      }
      currentGroup.items.push({
        text,
        link: `/adr/${filename}`
      })
    }
  }

  if (currentGroup && currentGroup.items.length > 0) {
    groups.push(currentGroup)
  }

  return [
    {
      text: 'ADR Overview & Log',
      link: '/adr/index'
    },
    ...groups
  ]
}

/**
 * Builds sidebar entries from the Markdown files in the SDLC documentation directory.
 * @return {Array<{text: string, link: string}>} Sorted sidebar entries using each file's first H1 heading or filename as its text.
 */
function getSdlcSidebar() {
  const sdlcDir = path.resolve(__dirname, '../SDLC')
  if (!fs.existsSync(sdlcDir)) return []
  const files = fs.readdirSync(sdlcDir)
  return files
    .filter(file => file.endsWith('.md'))
    .sort()
    .map(file => {
      const filePath = path.join(sdlcDir, file)
      const content = fs.readFileSync(filePath, 'utf-8')
      const titleMatch = content.match(/^#\s+(.*)/m)
      const title = titleMatch ? titleMatch[1].trim() : file.replace('.md', '')
      return { text: title, link: `/SDLC/${file}` }
    })
}

export default withMermaid(defineConfig({
  title: 'Cadence Clinical Portal',
  description: 'Metadata-Driven Clinical Execution Platform Documentation Portal',
  base: '/cadence-clinical/docs/',
  outDir: path.resolve(__dirname, '../../apps/web/dist/docs'),
  ignoreDeadLinks: true,
  themeConfig: {
    search: {
      provider: 'local'
    },
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Doc Index', link: '/DOCUMENTATION_INDEX' },
      { text: 'Architecture', link: '/ARCHITECTURE' },
      { text: 'Agents', link: '/AGENTS' },
      { text: 'SRS', link: '/SRS' }
    ],
    sidebar: [
      {
        text: 'Overview & Guides',
        items: [
          { text: 'Welcome & Readme', link: '/' },
          { text: 'Master Documentation Index', link: '/DOCUMENTATION_INDEX' },
          { text: 'System Architecture', link: '/ARCHITECTURE' },
          { text: 'AI Agents & Instructions', link: '/AGENTS' }
        ]
      },
      {
        text: 'Core Specifications',
        items: [
          { text: 'Data Lifecycle Management', link: '/DATA_LIFECYCLE' },
          { text: 'Feature Matrix', link: '/FEATURE_MATRIX' },
          { text: 'Local Development Environment', link: '/LOCAL_DEV_ENVIRONMENT' },
          { text: 'System Requirements Specification (SRS)', link: '/SRS' }
        ]
      },
      {
        text: 'SDLC Compliance Guidelines',
        items: getSdlcSidebar()
      },
      {
        text: 'Architectural Decision Records (ADRs)',
        items: getAdrSidebarGroups()
      }
    ]
  }
}))
