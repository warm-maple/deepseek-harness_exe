import { useCallback, useEffect, useState, type ReactNode } from 'react'
import type {
  MarketInstallResult,
  MarketSearchResult,
  MarketStateResult,
  MarketUninstallResult,
} from '@deepseek-ai/dsh-api-remotes/client'
import {
  IconSearchOutline16,
} from '@deepseek-ai/dsh-client-ui-primitives'
import type { InjectFace, PropsLocale, PropsRuntime } from '@deepseek-ai/dsh-client-ui-slots'
import css from './PluginMarketSettingsTab.module.css'

type MarketPluginSummary = MarketSearchResult['plugins'][number]
type MarketInstalledEntry = MarketStateResult['entries'][number]

/** Registration-side Remote face used by the tab. */
export interface PluginMarketSettingsTabInjected {
  /** Query the marketplace; empty text lists the `dsh-plugin` keyword listing. */
  search: (query: string) => Promise<MarketSearchResult>
  /** Read the profile's installed dependencies and Loader enablement. */
  state: () => Promise<MarketStateResult>
  /** Install one package (name or `file:` path) into the running profile. */
  install: (spec: string) => Promise<MarketInstallResult>
  /** Uninstall one profile dependency by exact package name. */
  uninstall: (name: string) => Promise<MarketUninstallResult>
}

/** Full component props assembled by the Settings slot renderer. */
export type PluginMarketSettingsTabProps =
  PropsRuntime<'settings.plugins.tab'>
  & PropsLocale<'settings.pluginMarket'>
  & InjectFace<PluginMarketSettingsTabInjected>

type SearchState =
  | { readonly status: 'idle' }
  | { readonly status: 'searching' }
  | { readonly status: 'error'; readonly detail?: string }
  | { readonly status: 'ready'; readonly plugins: readonly MarketPluginSummary[] }

type Pending = { readonly kind: 'install' | 'uninstall'; readonly name: string } | null

/** Localized "time ago" style date rendering without a date library. */
function formatDate(iso: string): string {
  return iso === '' ? '' : new Date(iso).toISOString().slice(0, 10)
}

/** Render the marketplace search, results, and installed-dependency lists. */
export function PluginMarketSettingsTab({ search, state, install, uninstall, t }: PluginMarketSettingsTabProps): ReactNode {
  const [query, setQuery] = useState('')
  const [searchState, setSearchState] = useState<SearchState>({ status: 'searching' })
  const [installed, setInstalled] = useState<readonly MarketInstalledEntry[]>([])
  const [pending, setPending] = useState<Pending>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const refreshState = useCallback(() => {
    void state().then(
      result => { setInstalled(result.entries) },
      () => { setInstalled([]) },
    )
  }, [state])

  const runSearch = useCallback((text: string) => {
    setSearchState({ status: 'searching' })
    void search(text).then(
      result => { setSearchState({ status: 'ready', plugins: result.plugins }) },
      (error: Error) => { setSearchState({ status: 'error', detail: error.message }) },
    )
  }, [search])

  useEffect(() => {
    refreshState()
    runSearch('')
  }, [refreshState, runSearch])

  const onInstall = (spec: string): void => {
    setPending({ kind: 'install', name: spec })
    setNotice(null)
    setFailure(null)
    void install(spec).then(
      () => {
        setPending(null)
        setNotice(t('restartHint'))
        refreshState()
        runSearch(query.trim())
      },
      (error: Error) => {
        setPending(null)
        setFailure(t('actionFailed').replace('{message}', error.message))
      },
    )
  }

  const onUninstall = (name: string): void => {
    setPending({ kind: 'uninstall', name })
    setNotice(null)
    setFailure(null)
    void uninstall(name).then(
      () => {
        setPending(null)
        setNotice(t('uninstalledHint'))
        refreshState()
        runSearch(query.trim())
      },
      (error: Error) => {
        setPending(null)
        setFailure(t('actionFailed').replace('{message}', error.message))
      },
    )
  }

  return (
    <div className={css.section}>
      <form
        className={css.searchForm}
        onSubmit={(event) => {
          event.preventDefault()
          const text = query.trim()
          // A file: or absolute-path spec installs a local package directly,
          // which is also how unpublished plugins are tried out.
          if (text.startsWith('file:') || /^(?:[A-Za-z]:[\\/]|\\\\|\/)/u.test(text)) {
            onInstall(text)
            return
          }
          runSearch(text)
        }}
      >
        <label className={css.search}>
          <IconSearchOutline16 aria-hidden="true" />
          <span className={css.visuallyHidden}>{t('search')}</span>
          <input
            type="search"
            value={query}
            placeholder={t('searchPlaceholder')}
            aria-label={t('search')}
            onChange={(event) => { setQuery(event.currentTarget.value) }}
          />
        </label>
        <button type="submit" disabled={searchState.status === 'searching'}>
          {t('search')}
        </button>
      </form>

      {notice !== null ? <p className={css.notice} role="status">{notice}</p> : null}
      {failure !== null ? <p className={css.failure} role="alert">{failure}</p> : null}

      {searchState.status === 'searching' ? <p className={css.status}>{t('searching')}</p> : null}
      {searchState.status === 'error' ? (
        <div className={css.failure}>
          <p role="alert">{t('error')}</p>
          {searchState.detail !== undefined ? <p className={css.meta}>{searchState.detail}</p> : null}
          <button type="button" onClick={() => { runSearch(query.trim()) }}>{t('retry')}</button>
        </div>
      ) : null}
      {searchState.status === 'ready' ? (
        <div className={css.catalog}>
          {searchState.plugins.length === 0
            ? <p className={css.status}>{query.trim() === '' ? t('empty') : t('emptySearch')}</p>
            : (
              <ul className={css.cards}>
                {searchState.plugins.map((plugin) => {
                  const busy = pending?.name === plugin.name
                  return (
                    <li className={css.card} key={plugin.name} data-plugin-name={plugin.name}>
                      <div className={css.cardBody}>
                        <div className={css.cardHeading}>
                          <strong className={css.cardTitle} title={plugin.name}>{plugin.name}</strong>
                          {plugin.version !== '' ? <span className={css.meta}>{t('version')} {plugin.version}</span> : null}
                          {plugin.installed ? <span className={css.tag}>{t('installedTag')}</span> : null}
                        </div>
                        {plugin.description !== '' ? <p className={css.description}>{plugin.description}</p> : null}
                        <div className={css.cardFooter}>
                          <span className={css.meta}>
                            {plugin.publisher !== '' ? `${t('publisher')}: ${plugin.publisher}` : ''}
                            {plugin.publisher !== '' && plugin.date !== '' ? ' · ' : ''}
                            {plugin.date !== '' ? `${t('updated')} ${formatDate(plugin.date)}` : ''}
                          </span>
                          <span className={css.actions}>
                            {plugin.link !== ''
                              ? <a href={plugin.link} target="_blank" rel="noreferrer">{t('viewNpm')}</a>
                              : null}
                            {plugin.installed ? (
                              <button
                                type="button"
                                disabled={busy || pending !== null}
                                onClick={() => { onUninstall(plugin.name) }}
                              >
                                {busy && pending?.kind === 'uninstall' ? t('uninstalling') : t('uninstall')}
                              </button>
                            ) : (
                              <button
                                type="button"
                                className={css.primary}
                                disabled={busy || pending !== null}
                                onClick={() => { onInstall(plugin.name) }}
                              >
                                {busy && pending?.kind === 'install' ? t('installing') : t('install')}
                              </button>
                            )}
                          </span>
                        </div>
                      </div>
                    </li>
                  )
                })}
              </ul>
            )}
        </div>
      ) : null}

      <div className={css.installedBlock}>
        <div className={css.catalogHeading}>
          <h3>{t('installedHeading')}</h3>
          <button type="button" className={css.refresh} onClick={refreshState}>{t('refresh')}</button>
        </div>
        {installed.length === 0
          ? <p className={css.status}>{t('installedEmpty')}</p>
          : (
            <ul className={css.installedList}>
              {installed.map((entry) => {
                const busy = pending?.name === entry.name
                return (
                  <li key={entry.name} data-installed-name={entry.name}>
                    <code className={css.installedName}>{entry.name}</code>
                    <span className={css.meta}>{entry.spec}</span>
                    <span className={css.loaderState} data-mounted={entry.enabledInLoader ? 'true' : 'false'}>
                      {entry.enabledInLoader ? t('loaderOn') : t('loaderOff')}
                    </span>
                    <span className={css.actions}>
                      <button
                        type="button"
                        disabled={busy || pending !== null}
                        onClick={() => { onUninstall(entry.name) }}
                      >
                        {busy && pending?.kind === 'uninstall' ? t('uninstalling') : t('uninstall')}
                      </button>
                    </span>
                  </li>
                )
              })}
            </ul>
          )}
      </div>
    </div>
  )
}
