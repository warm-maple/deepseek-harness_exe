/**
 * Repair pnpm legacy deploy output for a relocatable desktop runtime.
 *
 * Legacy deploy can omit direct workspace hoists and leave workspace junctions
 * in node_modules. The installed application cannot refer back to the source
 * checkout, so this script restores omitted packages and replaces every link
 * with files while omitting package-local node_modules trees.
 */
import { cp, lstat, mkdir, readFile, readdir, realpath, rm } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { basename, dirname, join, resolve, sep } from 'node:path'
import { parseArgs } from 'node:util'

const root = resolve(import.meta.dirname, '..')
const { values } = parseArgs({
  args: process.argv.slice(2),
  options: {
    runtime: { type: 'string' },
    source: { type: 'string' },
  },
})
if (values.runtime === undefined || values.source === undefined) {
  throw new Error('Usage: materialize-desktop-runtime --runtime <deploy-dir> --source <source-node_modules>')
}

const runtime = resolve(root, values.runtime)
const sourceNodeModules = resolve(root, values.source)
const nodeModules = join(runtime, 'node_modules')
const manifest = JSON.parse(await readFile(join(runtime, 'package.json'), 'utf8')) as {
  dependencies?: Record<string, string>
}
const restored: string[] = []

for (const dependency of Object.keys(manifest.dependencies ?? {}).sort()) {
  const destination = join(nodeModules, dependency)
  if (existsSync(destination)) continue
  const source = join(sourceNodeModules, dependency)
  if (!existsSync(source)) {
    throw new Error(`Desktop runtime dependency ${dependency} is absent from both deploy and source node_modules.`)
  }
  await mkdir(dirname(destination), { recursive: true })
  await copyPackage(source, destination)
  restored.push(dependency)
}

let remaining = await findLink(nodeModules)
while (remaining !== undefined) {
  const segments = remaining.slice(nodeModules.length + 1).split(sep)
  const binIndex = segments.lastIndexOf('.bin')
  if (binIndex >= 0) {
    await rm(join(nodeModules, ...segments.slice(0, binIndex + 1)), { recursive: true, force: true })
  } else {
    const source = await realpath(remaining)
    await rm(remaining, { recursive: true, force: true })
    await copyPackage(source, remaining)
  }
  remaining = await findLink(nodeModules)
}

// The desktop launches its entry module directly. Package-manager command
// shims, including .CMD files, are not runtime inputs and are not shipped.
await removeCommandShims(nodeModules)

console.log(
  restored.length === 0
    ? 'materialize-desktop-runtime: deploy already contained every direct dependency.'
    : `materialize-desktop-runtime: restored ${restored.length} direct dependencies.`,
)
console.log('materialize-desktop-runtime: runtime is relocatable and contains no filesystem links.')

async function copyPackage(source: string, destination: string): Promise<void> {
  const nestedNodeModules = join(source, 'node_modules')
  await cp(source, destination, {
    recursive: true,
    dereference: true,
    filter: path => path !== nestedNodeModules && !path.startsWith(nestedNodeModules + sep),
  })
}

async function findLink(directory: string): Promise<string | undefined> {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    const metadata = await lstat(path)
    if (metadata.isSymbolicLink()) return path
    if (metadata.isDirectory()) {
      const nested = await findLink(path)
      if (nested !== undefined) return nested
    }
  }
  return undefined
}

async function removeCommandShims(directory: string): Promise<void> {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory() && entry.name === '.bin' && basename(directory) === 'node_modules') {
      await rm(path, { recursive: true, force: true })
    } else if (entry.isDirectory()) {
      await removeCommandShims(path)
    }
  }
}
