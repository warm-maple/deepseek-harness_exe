/** Plugin marketplace: npm registry search plus profile-scoped install and uninstall. */

import { spawn } from 'node:child_process'
import { createRequire } from 'node:module'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import yaml from 'js-yaml'
import type { Context } from '@deepseek-ai/cordis'
import type {} from '@deepseek-ai/cordis-plugin-loader'
import {
  readProfileManifest,
  writeProfileManifest,
  type ProfileManifest,
} from '@deepseek-ai/dsh-app-boot'
import { TypertRemoteService, Remote } from '@deepseek-ai/dsh-typert-protocol'
// Typert-generated ./typert and ./remote artifacts import Zod at runtime.
import type {} from 'zod'
import type {
  MarketInstallKind,
  MarketInstallResult,
  MarketInstalledEntry,
  MarketPluginSummary,
  MarketSearchResult,
  MarketStateResult,
  MarketUninstallResult,
} from './types.ts'

export type * from './types.ts'

// The gateway deliberately uses no `#private` members: the Remote dispatch
// binds every method with the service instance as receiver, and V8's private
// brand check rejects any helper reached through a rebound `this`. Internal
// helpers are module-level functions or plainly named members instead.

/** Registry queried for marketplace listings; npm's own config wins when set. */
const DEFAULT_REGISTRY = 'https://registry.npmjs.org'

/** Search page size; the tab renders registry relevance order. */
const SEARCH_SIZE = 20

/** Registry keyword a package declares to appear in the dsh marketplace listing. */
const MARKET_KEYWORD = 'dsh-plugin'

/** The profile patch row id this gateway manages for its inserted plugin rows. */
const marketRowId = (packageName: string): string => `market:${packageName}`

/** Absolute package name test: no version range, no path, no flags. */
const EXACT_NAME_PATTERN = /^(?:@[a-z0-9-~][a-z0-9-._~]*\/)?[a-z0-9-~][a-z0-9-._~]*$/iu

/**
 * Whether one install specifier is safe to hand to npm verbatim: a registry
 * name (optionally scoped, optionally with a version range) or a local
 * `file:`/absolute path. Anything that could parse as an npm flag is rejected;
 * shell interpolation is impossible because npm is spawned without a shell.
 */
function isSafeSpec(spec: string): boolean {
  if (spec.startsWith('-') || /\s/u.test(spec)) return false
  if (spec.startsWith('file:') || spec.startsWith('/') || spec.startsWith('\\')
    || /^(?:file:)?[A-Za-z]:[\\/]/u.test(spec)) return true
  return /^(?:@[a-z0-9-~][a-z0-9-._~]*\/)?[a-z0-9-~][a-z0-9-._~]*(?:@[^\s]+)?$/iu.test(spec)
}

/** Bounded tail of one npm run's combined output, for error surfacing. */
function outputTail(chunks: readonly string[], maximumLines = 20): string {
  return chunks.join('').trim().split(/\r?\n/u).slice(-maximumLines).join('\n')
}

/** One npm registry search hit, pruned to the fields the wire carries. */
interface RegistrySearchObject {
  package: {
    name: string
    version?: string
    description?: string
    keywords?: string[]
    links?: { npm?: string }
    publisher?: { username?: string }
    date?: string
  }
}

/** The registry package document served for one exact name. */
interface RegistryPackageDocument {
  'dist-tags'?: { latest?: string }
  description?: string
  keywords?: string[]
  time?: Record<string, string | { }>
  maintainers?: Array<{ name?: string }>
  repository?: string | { url?: string }
}

/** Registry-facing link for one package. */
function packageLink(registry: string, name: string): string {
  return `${registry.replace(/\/+$/u, '')}/package/${name}`
}

/** Repository URL of one package document, normalized to a browsable form. */
function repositoryLink(document: RegistryPackageDocument): string {
  const raw = typeof document.repository === 'string'
    ? document.repository
    : document.repository?.url
  if (raw === undefined) return ''
  return raw.replace(/^git\+/u, '').replace(/^git@([^:]+):/u, 'https://$1/').replace(/\.git$/u, '')
}

/** Wire summary built from one registry search hit. */
function summaryFromSearch(object: RegistrySearchObject, installed: ReadonlySet<string>): MarketPluginSummary {
  const pkg = object.package
  return {
    name: pkg.name,
    version: pkg.version ?? '',
    description: pkg.description ?? '',
    publisher: pkg.publisher?.username ?? '',
    date: pkg.date ?? '',
    keywords: pkg.keywords ?? [],
    link: pkg.links?.npm ?? packageLink(DEFAULT_REGISTRY, pkg.name),
    installed: installed.has(pkg.name),
  }
}

/** Wire summary built from one registry package document (exact-name lookup). */
function summaryFromDocument(name: string, document: RegistryPackageDocument, installed: ReadonlySet<string>): MarketPluginSummary {
  const version = document['dist-tags']?.latest ?? ''
  const repository = repositoryLink(document)
  const publishedAt = document.time?.[version]
  return {
    name,
    version,
    description: document.description ?? '',
    publisher: document.maintainers?.[0]?.name ?? '',
    date: typeof publishedAt === 'string' ? publishedAt : '',
    keywords: document.keywords ?? [],
    link: repository !== '' ? repository : packageLink(DEFAULT_REGISTRY, name),
    installed: installed.has(name),
  }
}

/** One row of the profile patch layer this gateway edits. */
interface PatchRow {
  id?: string
  name?: string
  disabled?: boolean
  insert?: Array<{ id?: string, name?: string }>
}

/** The running profile's directory, anchored at the Loader's config tree. */
function profileDirOf(ctx: Context): string {
  const baseUrl = ctx.baseUrl
  if (typeof baseUrl !== 'string' || baseUrl === '') {
    throw new Error('plugin-market: ctx.baseUrl is unset — the gateway needs the profile anchor')
  }
  // The Loader anchors the config tree at the profile directory, as a file URL.
  const dir = baseUrl.startsWith('file://') ? fileURLToPath(baseUrl) : baseUrl
  if (!existsSync(join(dir, 'package.json'))) {
    throw new Error(`plugin-market: ${dir} holds no profile package.json — refusing to operate outside a profile`)
  }
  return dir
}

/** Names one profile directory currently depends on. */
function installedNamesIn(dir: string): Set<string> {
  return new Set(Object.keys(readProfileManifest('plugin-market', dir).dependencies ?? {}))
}

/** Run the bundled npm CLI with one profile directory as working directory. */
async function runNpm(dir: string, args: readonly string[]): Promise<void> {
  const require = createRequire(import.meta.url)
  // npm's exports map does not expose ./bin/npm-cli.js, so resolve the package
  // manifest and derive the CLI path from its directory on disk.
  const cli = require.resolve('npm/package.json').split('package.json').join('bin/npm-cli.js')
  const stdout: string[] = []
  const stderr: string[] = []
  const child = spawn(process.execPath, [cli, ...args], {
    cwd: dir,
    windowsHide: true,
    env: {
      ...process.env,
      NO_COLOR: '1',
      npm_config_progress: 'false',
      npm_config_fund: 'false',
      npm_config_audit: 'false',
    },
  })
  child.stdout.on('data', (chunk: Buffer) => { stdout.push(chunk.toString('utf8')) })
  child.stderr.on('data', (chunk: Buffer) => { stderr.push(chunk.toString('utf8')) })
  const code = await new Promise<null | number>(resolve => {
    child.once('error', error => { stderr.push(String(error)); resolve(null) })
    child.once('close', exitCode => resolve(exitCode))
  })
  if (code !== 0) {
    const tail = outputTail([...stdout, ...stderr])
    throw new Error(`plugin-market: npm ${args[0]} exited with ${code === null ? 'a spawn error' : `code ${code}`}\n${tail}`)
  }
}

/** Read one installed package's manifest, anchored at the profile. */
function packageManifestIn(dir: string, name: string): ProfileManifest {
  const require = createRequire(join(dir, 'package.json'))
  const path = require.resolve(`${name}/package.json`)
  return JSON.parse(readFileSync(path, 'utf8')) as ProfileManifest
}

/** How one installed package participates in the profile composition. */
function kindOf(manifest: ProfileManifest): MarketInstallKind {
  if (manifest.dsh?.bundle?.patch !== undefined) return 'bundle'
  if (manifest.dsh !== undefined) return 'plugin'
  return 'library'
}

/** Parse one profile patch layer into rows, tolerating an absent file. */
function readPatchRows(path: string): PatchRow[] {
  if (!existsSync(path)) return []
  const loaded: unknown = yaml.load(readFileSync(path, 'utf8'))
  return Array.isArray(loaded) ? loaded as PatchRow[] : []
}

/** Append one managed Loader insert row to the profile patch layer. */
function insertPluginRow(dir: string, name: string): void {
  const path = join(dir, 'cordis.patch.yml')
  const rows = readPatchRows(path)
  if (rows.some(row => row.insert?.some(item => item.name === name))) return
  rows.push({ insert: [{ id: marketRowId(name), name }] })
  writeFileSync(path, yaml.dump(rows, { lineWidth: 100 }), 'utf8')
}

/** Remove the managed Loader insert rows of one package from the profile patch layer. */
function removePluginRow(dir: string, name: string): void {
  const path = join(dir, 'cordis.patch.yml')
  const rows = readPatchRows(path)
  const filtered = rows.filter(row => !row.insert?.some(item => item.name === name || item.id === marketRowId(name)))
  if (filtered.length === rows.length) return
  writeFileSync(path, yaml.dump(filtered, { lineWidth: 100 }), 'utf8')
}

/** Serialize one mutation behind the previous one; npm rewrites the profile manifest. */
function enqueue(queue: { current: Promise<unknown> }, run: () => Promise<unknown>): Promise<unknown> {
  const result = queue.current.then(run, run)
  queue.current = result.then(() => undefined, () => undefined)
  return result
}

/** Search the npm registry for marketplace listings. */
async function searchRegistry(query: string, installed: ReadonlySet<string>): Promise<MarketSearchResult> {
  const text = query.trim()
  const registry = (process.env.npm_config_registry ?? DEFAULT_REGISTRY).replace(/\/+$/u, '')
  const exactName = EXACT_NAME_PATTERN.test(text)
  if (exactName) {
    const response = await fetch(`${registry}/${text}`, {
      signal: AbortSignal.timeout(15_000),
      headers: { accept: 'application/vnd.npm.install-v1+json, application/json' },
    })
    if (response.ok) {
      const document = await response.json() as RegistryPackageDocument
      return { plugins: [summaryFromDocument(text, document, installed)] }
    }
  }
  const searchText = text === '' ? `keywords:${MARKET_KEYWORD}` : `${text} keywords:${MARKET_KEYWORD}`
  const url = `${registry}/-/v1/search?size=${SEARCH_SIZE}&text=${encodeURIComponent(searchText)}`
  const response = await fetch(url, { signal: AbortSignal.timeout(15_000) })
  if (!response.ok) {
    throw new Error(`plugin-market: registry search failed with HTTP ${response.status}`)
  }
  const payload = await response.json() as { objects?: RegistrySearchObject[] }
  const plugins = (payload.objects ?? [])
    .map(object => summaryFromSearch(object, installed))
    .filter(summary => summary.name !== '')
  return { plugins }
}

/** Install one package into one profile and wire it into the composition. */
async function installInto(dir: string, spec: string): Promise<MarketInstallResult> {
  const text = spec.trim()
  if (!isSafeSpec(text)) {
    throw new Error(`plugin-market: refusing to install ${JSON.stringify(spec)}`)
  }
  await runNpm(dir, ['install', text, '--no-audit', '--no-fund', '--loglevel=error'])
  const manifest = readProfileManifest('plugin-market', dir)
  const name = Object.keys(manifest.dependencies ?? {})
    .find(dep => text === dep || text.startsWith(`${dep}@`) || text === `file:${dep}`)
  if (name === undefined) {
    throw new Error('plugin-market: npm finished but the profile manifest records no new dependency')
  }
  const recorded = manifest.dependencies?.[name] ?? ''
  const kind = kindOf(packageManifestIn(dir, name))
  if (kind === 'bundle') {
    const bundles = manifest.dsh?.profile?.bundles ?? []
    if (!bundles.includes(name)) {
      manifest.dsh = { ...manifest.dsh, profile: { ...manifest.dsh?.profile, bundles: [...bundles, name] } }
      writeProfileManifest(dir, manifest)
    }
  } else if (kind === 'plugin') {
    insertPluginRow(dir, name)
  }
  return {
    name,
    spec: recorded,
    kind,
    restartRecommended: kind !== 'library',
  }
}

/** Uninstall one profile dependency and remove the rows this gateway manages for it. */
async function uninstallFrom(dir: string, name: string): Promise<MarketUninstallResult> {
  const text = name.trim()
  if (!EXACT_NAME_PATTERN.test(text)) {
    throw new Error(`plugin-market: refusing to uninstall ${JSON.stringify(name)}`)
  }
  const manifest = readProfileManifest('plugin-market', dir)
  if ((manifest.dependencies ?? {})[text] === undefined) {
    throw new Error(`plugin-market: ${text} is not a dependency of this profile`)
  }
  await runNpm(dir, ['uninstall', text, '--no-audit', '--no-fund', '--loglevel=error'])
  const after = readProfileManifest('plugin-market', dir)
  const bundles = after.dsh?.profile?.bundles ?? []
  const index = bundles.indexOf(text)
  if (index >= 0) {
    bundles.splice(index, 1)
    after.dsh = { ...after.dsh, profile: { ...after.dsh?.profile, bundles } }
    writeProfileManifest(dir, after)
  }
  removePluginRow(dir, text)
  return { name: text }
}

/** Remote service exposing marketplace search, install, and uninstall. */
export class PluginMarketGateway extends TypertRemoteService {
  static inject = ['loader']

  /** Serialized mutation queue holder; npm rewrites the profile manifest, so concurrent runs would race it. */
  private readonly queue = { current: Promise.resolve() }

  constructor(ctx: Context) {
    super(ctx, 'pluginMarket')
  }

  /**
   * Search the npm registry. Free-text queries list packages tagged with the
   * marketplace keyword; a query that looks like an exact package name
   * resolves that package document directly, so any npm package is reachable
   * by name even before the ecosystem adopts the keyword.
   * @param query - free text, an exact package name, or empty for the listing.
   * @returns Best-match-first hits with the profile's install state merged in.
   */
  @Remote('search')
  async search(query: string): Promise<MarketSearchResult> {
    return searchRegistry(query, installedNamesIn(profileDirOf(this.ctx)))
  }

  /**
   * List the profile's out-of-tree dependencies with their Loader enablement.
   * @returns One entry per profile dependency, in manifest order.
   */
  @Remote('state')
  state(): MarketStateResult {
    const dir = profileDirOf(this.ctx)
    const manifest = readProfileManifest('plugin-market', dir)
    const enabled = new Set<string>()
    for (const entry of this.ctx.loader.entries()) {
      if (entry.options.group || entry.disabled) continue
      enabled.add(entry.options.name)
    }
    const entries: MarketInstalledEntry[] = Object.entries(manifest.dependencies ?? {}).map(([name, spec]) => ({
      name,
      spec,
      enabledInLoader: enabled.has(name),
    }))
    return { entries }
  }

  /**
   * Install one package into the running profile with the bundled npm, then
   * wire it into the composition: `dsh.bundle` packages join the profile's
   * bundle layer list, plugin-shaped packages get a managed Loader insert row
   * in the profile patch (hot-applied by the config watcher), and everything
   * else installs as a plain library dependency. The wire name avoids the
   * client namespace service's reserved `install` member.
   * @param spec - registry name (optionally with a version range) or `file:` path.
   * @returns What was installed and how it participates in the profile.
   */
  @Remote('installPlugin')
  installPlugin(spec: string): Promise<MarketInstallResult> {
    return enqueue(this.queue, () => installInto(profileDirOf(this.ctx), spec)) as Promise<MarketInstallResult>
  }

  /**
   * Uninstall one profile dependency and remove every composition row this
   * gateway manages for it (bundle layer entry and patch insert row). The
   * wire name mirrors {@link installPlugin}.
   * @param name - exact package name recorded in the profile manifest.
   * @returns Confirmation of the removal.
   */
  @Remote('uninstallPlugin')
  uninstallPlugin(name: string): Promise<MarketUninstallResult> {
    return enqueue(this.queue, () => uninstallFrom(profileDirOf(this.ctx), name)) as Promise<MarketUninstallResult>
  }
}

export default PluginMarketGateway
