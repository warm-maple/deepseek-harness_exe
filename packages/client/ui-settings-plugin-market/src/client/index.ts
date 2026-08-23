/** Plugin marketplace tab registered into Web Settings. */

import type {} from '@deepseek-ai/dsh-client-locale/client'
import type { ClientContext } from '@deepseek-ai/dsh-client-runtime/client'
import type {} from '@deepseek-ai/dsh-client-ui-settings/client'
import {
  PluginMarketSettingsTab,
  type PluginMarketSettingsTabInjected,
} from './PluginMarketSettingsTab.tsx'
import { en, zh, type PluginMarketLocaleKey } from './locales.ts'

export type { PluginMarketLocaleKey } from './locales.ts'
export type {
  PluginMarketSettingsTabInjected,
  PluginMarketSettingsTabProps,
} from './PluginMarketSettingsTab.tsx'

declare module '@deepseek-ai/dsh-client-ui-slots' {
  interface LocaleNamespaceMap {
    /** Plugin marketplace copy. */
    'settings.pluginMarket': PluginMarketLocaleKey
  }
}

/** Dictionary namespace owned by this plugin. */
export const NS = 'settings.pluginMarket'

/** Services required by the Settings registration and generated Remote face. */
export const inject = ['slots', 'locale', 'remote', 'remote.pluginMarket']

/** Contribute the marketplace tab to the Plugins settings section. */
export function apply(ctx: ClientContext): void {
  ctx.effect(() => ctx.locale.register(NS, { zh, en }), 'ui-settings-plugin-market: dictionaries')

  const t = ctx.locale.bind(NS)
  const search: PluginMarketSettingsTabInjected['search'] = async (query) => {
    const result = await ctx.remote.pluginMarket.search(query)
    if (!result.ok) {
      throw new Error(`pluginMarket.search failed: ${result.error.code}: ${result.error.message}`)
    }
    return result.value
  }
  const state: PluginMarketSettingsTabInjected['state'] = async () => {
    const result = await ctx.remote.pluginMarket.state()
    if (!result.ok) {
      throw new Error(`pluginMarket.state failed: ${result.error.code}: ${result.error.message}`)
    }
    return result.value
  }
  const install: PluginMarketSettingsTabInjected['install'] = async (spec) => {
    const result = await ctx.remote.pluginMarket.installPlugin(spec)
    if (!result.ok) {
      throw new Error(`pluginMarket.installPlugin failed: ${result.error.code}: ${result.error.message}`)
    }
    return result.value
  }
  const uninstall: PluginMarketSettingsTabInjected['uninstall'] = async (name) => {
    const result = await ctx.remote.pluginMarket.uninstallPlugin(name)
    if (!result.ok) {
      throw new Error(`pluginMarket.uninstallPlugin failed: ${result.error.code}: ${result.error.message}`)
    }
    return result.value
  }
  const injected = (): PluginMarketSettingsTabInjected => ({ search, state, install, uninstall })

  ctx.slots.inject('settings.plugins.tab', () => ctx.slots.register({
    name: 'settings.plugins.tab',
    id: 'market',
    order: 20,
    label: () => t('tab'),
    locale: NS,
    inject: injected,
  }, PluginMarketSettingsTab))
}
