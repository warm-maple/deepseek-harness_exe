/** Wire vocabulary of the plugin marketplace Remote. */

/** One marketplace search hit, with the running profile's install state merged in. */
export interface MarketPluginSummary {
  /** Exact npm package name (the install specifier). */
  readonly name: string
  /** Latest published version at query time. */
  readonly version: string
  /** One-line package description, empty when the manifest has none. */
  readonly description: string
  /** Publisher username from the registry, empty when unknown. */
  readonly publisher: string
  /** ISO date of the latest publish, empty when unknown. */
  readonly date: string
  /** Registry keywords, in registry order. */
  readonly keywords: readonly string[]
  /** Absolute npm page URL. */
  readonly link: string
  /** Whether the package is already a dependency of the running profile. */
  readonly installed: boolean
}

/** Search response: zero or more hits, best match first. */
export interface MarketSearchResult {
  readonly plugins: readonly MarketPluginSummary[]
}

/** One out-of-tree dependency of the running profile. */
export interface MarketInstalledEntry {
  /** Exact package name from the profile manifest. */
  readonly name: string
  /** Version range exactly as recorded in the profile manifest. */
  readonly spec: string
  /** Whether a non-disabled Loader entry currently mounts this package. */
  readonly enabledInLoader: boolean
}

/** Installed-state response covering every profile dependency. */
export interface MarketStateResult {
  readonly entries: readonly MarketInstalledEntry[]
}

/** How an installed package participates in the profile composition. */
export type MarketInstallKind = 'bundle' | 'plugin' | 'library'

/** Install response after a successful package installation. */
export interface MarketInstallResult {
  readonly name: string
  /** Installed version exactly as npm recorded it in the profile manifest. */
  readonly spec: string
  readonly kind: MarketInstallKind
  /** Whether a restart is needed for the package to take full effect. */
  readonly restartRecommended: boolean
}

/** Uninstall response after a successful package removal. */
export interface MarketUninstallResult {
  readonly name: string
}
