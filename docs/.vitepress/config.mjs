import { defineConfig } from 'vitepress'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

function getAdrSidebar() {
  const adrDir = path.resolve(__dirname, '../adr')
  if (!fs.existsSync(adrDir)) return []
  const files = fs.readdirSync(adrDir)
  return files
    .filter(file => file.endsWith('.md') && file !== 'index.md' && file !== 'TEMPLATE.md')
    .sort()
    .reverse() // Newest first
    .map(file => {
      const filePath = path.join(adrDir, file)
      const content = fs.readFileSync(filePath, 'utf-8')
      const titleMatch = content.match(/^#\s+(.*)/m)
      const title = titleMatch ? titleMatch[1].trim() : file.replace('.md', '')
      return { text: title, link: `/adr/${file}` }
    })
}

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

export default defineConfig({
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
      { text: 'Architecture', link: '/ARCHITECTURE' },
      { text: 'Agents', link: '/AGENTS' },
      { text: 'SRS', link: '/SRS' }
    ],
    sidebar: [
      {
        text: 'Overview',
        items: [
          { text: 'Welcome & Readme', link: '/' },
          { text: 'System Architecture', link: '/ARCHITECTURE' },
          { text: 'AI Agents & Instructions', link: '/AGENTS' }
        ]
      },
      {
        text: 'Core Documentation',
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
        items: getAdrSidebar()
      }
    ]
  }
})
