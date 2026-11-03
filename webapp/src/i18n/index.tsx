/**
 * i18n/index.tsx — Locale context, provider, and hook
 *
 * Usage:
 *   const { t, locale, setLocale } = useLocale()
 *   t('heroTitle')                        → "Download Any Video, Free"
 *   t('statusQueuePos', { pos: 2, total: 5 }) → "Position 2 of 5 — starting soon"
 */

import {
  createContext,
  useContext,
  useState,
  useEffect,
  type ReactNode,
} from 'react'
import {
  TRANSLATIONS,
  LOCALE_NAMES,
  RTL_LOCALES,
  LOCALES,
  type Locale,
  type TKey,
} from './translations'

export { LOCALE_NAMES, RTL_LOCALES, LOCALES, type Locale, type TKey }

const LS_KEY = 'anydownloader-locale'

/** Detect the best locale for the user from localStorage → browser language → 'en' */
function detectLocale(): Locale {
  try {
    const saved = localStorage.getItem(LS_KEY)
    if (saved && (LOCALES as readonly string[]).includes(saved)) return saved as Locale
  } catch {}

  const lang = (navigator.language ?? '').toLowerCase()
  if (lang.startsWith('zh-tw') || lang.startsWith('zh-hk') || lang.startsWith('zh-mo')) return 'zh-TW'
  if (lang.startsWith('zh')) return 'zh-CN'
  if (lang.startsWith('ja')) return 'ja'
  if (lang.startsWith('es')) return 'es'
  if (lang.startsWith('fr')) return 'fr'
  if (lang.startsWith('de')) return 'de'
  if (lang.startsWith('ar')) return 'ar'
  return 'en'
}

/** Apply locale to the <html> element (lang + dir). */
function applyLocaleToDOM(locale: Locale) {
  document.documentElement.lang = locale
  document.documentElement.dir  = RTL_LOCALES.has(locale) ? 'rtl' : 'ltr'
}

interface I18nCtx {
  locale: Locale
  setLocale: (l: Locale) => void
  t: (key: TKey, vars?: Record<string, string | number>) => string
}

const Ctx = createContext<I18nCtx>(null!)

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(detectLocale)

  // Apply on first render
  useEffect(() => { applyLocaleToDOM(locale) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  function setLocale(l: Locale) {
    setLocaleState(l)
    try { localStorage.setItem(LS_KEY, l) } catch {}
    applyLocaleToDOM(l)
  }

  function t(key: TKey, vars?: Record<string, string | number>): string {
    // Fall back to English if locale is missing the key
    const str = TRANSLATIONS[locale]?.[key] ?? TRANSLATIONS.en[key] ?? key
    if (!vars) return str
    return Object.entries(vars).reduce(
      (s, [k, v]) => s.replaceAll(`{${k}}`, String(v)),
      str,
    )
  }

  return <Ctx.Provider value={{ locale, setLocale, t }}>{children}</Ctx.Provider>
}

export function useLocale(): I18nCtx {
  return useContext(Ctx)
}
