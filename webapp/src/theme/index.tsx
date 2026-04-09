/**
 * theme/index.tsx — Dark / Light mode context
 *
 * Usage:
 *   const { theme, toggleTheme } = useTheme()
 *
 * Applies data-theme="dark"|"light" to <html> and updates
 * the <meta name="theme-color"> for browser chrome.
 */

import { createContext, useContext, useState, useEffect, type ReactNode } from 'react'

export type Theme = 'dark' | 'light'
const LS_KEY = 'anydownloader-theme'

const THEME_COLORS: Record<Theme, string> = {
  dark:  '#070b12',
  light: '#f0f4f8',
}

function detectTheme(): Theme {
  try {
    const saved = localStorage.getItem(LS_KEY)
    if (saved === 'dark' || saved === 'light') return saved
  } catch {}
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

function applyTheme(theme: Theme) {
  document.documentElement.setAttribute('data-theme', theme)
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', THEME_COLORS[theme])
}

interface ThemeCtx {
  theme: Theme
  toggleTheme: () => void
}

const Ctx = createContext<ThemeCtx>(null!)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(detectTheme)

  useEffect(() => { applyTheme(theme) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  function toggleTheme() {
    const next: Theme = theme === 'dark' ? 'light' : 'dark'
    setTheme(next)
    try { localStorage.setItem(LS_KEY, next) } catch {}
    applyTheme(next)
  }

  return <Ctx.Provider value={{ theme, toggleTheme }}>{children}</Ctx.Provider>
}

export function useTheme(): ThemeCtx {
  return useContext(Ctx)
}
