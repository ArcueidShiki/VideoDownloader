import { useState, useEffect, useRef } from 'react'
import gsap from 'gsap'
import { useGSAP } from '@gsap/react'
import './App.css'
import { useLocale, LOCALE_NAMES, LOCALES } from './i18n'
import { useTheme } from './theme'
import {
  IconDownload,
  IconShield,
  IconBolt,
  IconGlobe,
  IconApple,
  IconWindows,
  IconSun,
  IconMoon,
  IconChevronDown,
  IconCheckCircle,
} from './icons'

gsap.registerPlugin(useGSAP)

// ---------------------------------------------------------------------------
// Release definitions
// ---------------------------------------------------------------------------

interface AppRelease {
  id: string
  platform: string
  icon: React.ReactNode
  version: string
  filename: string
  href: string
  available: boolean
  requirements: string
  badge?: string
}

// GitHub releases landing page — lists every installer variant (arm64 / x64 / x86,
// .exe / .msi / .dmg) so the user picks the one that matches their platform.
// `/releases/latest` auto-redirects to the newest published release, so this URL
// never has to change when cutting a new version.
const GITHUB_REPO = 'https://github.com/ArcueidShiki/AnyDownloader'
const RELEASES_PAGE = `${GITHUB_REPO}/releases/latest`

const RELEASES: AppRelease[] = [
  {
    id: 'macos',
    platform: 'macOS',
    icon: <IconApple size={28} />,
    version: '1.0',
    filename: 'AnyDownloader-1.0.dmg',
    href: RELEASES_PAGE,
    available: true,
    requirements: 'macOS 13+, Apple Silicon or Intel',
    badge: 'Recommended',
  },
  {
    id: 'windows',
    platform: 'Windows',
    icon: <IconWindows size={28} />,
    version: '1.0',
    filename: 'arm64 / x64 / x86 · .exe or .msi',
    href: RELEASES_PAGE,
    available: true,
    requirements: 'Windows 10/11 (arm64, x64, x86)',
  },
  {
    id: 'chrome',
    platform: 'Chrome Extension',
    icon: <IconChromeLogo size={28} />,
    version: '1.0',
    filename: 'Chrome Web Store',
    href: '#',
    available: false,
    requirements: 'Chrome / Edge / Brave',
    badge: 'Browser Add-on',
  },
]

// ---------------------------------------------------------------------------
// ThemeToggle
// ---------------------------------------------------------------------------

function ThemeToggle() {
  const { theme, toggleTheme } = useTheme()
  return (
    <button
      className="theme-toggle"
      onClick={toggleTheme}
      aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
      title={theme === 'dark' ? 'Light mode' : 'Dark mode'}
    >
      {theme === 'dark'
        ? <IconSun  size={15} strokeWidth={1.8} />
        : <IconMoon size={15} strokeWidth={1.8} />
      }
    </button>
  )
}

// ---------------------------------------------------------------------------
// LanguagePicker
// ---------------------------------------------------------------------------

function LanguagePicker() {
  const { locale, setLocale } = useLocale()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function handler(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  return (
    <div className="lang-picker" ref={ref}>
      <button
        className="lang-trigger"
        onClick={() => setOpen(o => !o)}
        aria-label="Select language"
        aria-expanded={open}
      >
        <IconGlobe size={13} strokeWidth={1.8} />
        <span className="lang-current">{LOCALE_NAMES[locale]}</span>
        <IconChevronDown size={11} strokeWidth={2} className={open ? 'chevron-up' : ''} />
      </button>
      {open && (
        <div className="lang-dropdown" role="listbox">
          {LOCALES.map(l => (
            <button
              key={l}
              role="option"
              aria-selected={l === locale}
              className={`lang-option ${l === locale ? 'active' : ''}`}
              onClick={() => { setLocale(l); setOpen(false) }}
            >
              {LOCALE_NAMES[l]}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Chrome logo SVG
// ---------------------------------------------------------------------------

function IconChromeLogo({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg" aria-hidden>
      <path d="M50,50 L50,0 A50,50,0,0,1,93.3,75 Z" fill="#EA4335"/>
      <path d="M50,50 L93.3,75 A50,50,0,0,1,6.7,75 Z" fill="#FBBC05"/>
      <path d="M50,50 L6.7,75 A50,50,0,0,1,50,0 Z" fill="#34A853"/>
      <circle cx="50" cy="50" r="30" fill="white"/>
      <circle cx="50" cy="50" r="20" fill="#4285F4"/>
    </svg>
  )
}

// ---------------------------------------------------------------------------
// FeatureItem
// ---------------------------------------------------------------------------

function FeatureItem({ icon, colorClass, label, desc }: {
  icon: React.ReactNode; colorClass: string; label: string; desc: string
}) {
  return (
    <div className="feature-item">
      <div className={`feature-icon ${colorClass}`}>{icon}</div>
      <div>
        <p className="feature-label">{label}</p>
        <p className="feature-desc">{desc}</p>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// ReleaseCard
// ---------------------------------------------------------------------------

function ReleaseCard({ release }: { release: AppRelease }) {
  const { t } = useLocale()
  const { platform, icon, version, filename, href, available, requirements, badge } = release

  return (
    <div className="app-card">
      <div className="app-card-header">
        <div className="app-icon-wrap">
          {icon}
        </div>
        <div>
          <p className="app-name">{platform}</p>
          <p className="app-version">v{version}</p>
        </div>
        {badge && <span className="badge-tag">{badge}</span>}
      </div>

      <div className="app-card-features">
        <div className="app-feature-row">
          <IconCheckCircle size={14} strokeWidth={2} />
          <span>{t('featSites')}</span>
        </div>
        <div className="app-feature-row">
          <IconCheckCircle size={14} strokeWidth={2} />
          <span>{t('featBackground')}</span>
        </div>
        <div className="app-feature-row">
          <IconCheckCircle size={14} strokeWidth={2} />
          <span>{t('featPrivacy')}</span>
        </div>
      </div>

      <p className="app-requirements">{requirements}</p>

      <div className="app-card-footer">
        <code className="app-filename">{filename}</code>
        {available ? (
          <a
            className="btn btn-download"
            href={href}
            target="_blank"
            rel="noopener noreferrer"
          >
            <IconDownload size={14} strokeWidth={2} />
            {t('btnDownload')}
          </a>
        ) : (
          <button className="btn btn-primary" disabled>
            {t('dlComingSoon')}
          </button>
        )}
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// HowItWorks
// ---------------------------------------------------------------------------

function HowItWorks() {
  const { t } = useLocale()
  return (
    <div className="how-it-works">
      <h2 className="section-title">{t('howTitle')}</h2>
      <div className="steps-grid">
        <div className="step-card">
          <div className="step-number">1</div>
          <p className="step-label">{t('howStep1')}</p>
          <p className="step-desc">{t('howStep1Desc')}</p>
        </div>
        <div className="step-card">
          <div className="step-number">2</div>
          <p className="step-label">{t('howStep2')}</p>
          <p className="step-desc">{t('howStep2Desc')}</p>
        </div>
        <div className="step-card">
          <div className="step-number">3</div>
          <p className="step-label">{t('howStep3')}</p>
          <p className="step-desc">{t('howStep3Desc')}</p>
        </div>
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------

export default function App() {
  const { t } = useLocale()
  const mainRef = useRef<HTMLDivElement>(null)

  useGSAP(() => {
    gsap.from('.header', { y: -20, opacity: 0, duration: 0.45, ease: 'power3.out' })
    gsap.from('.hero',   { y: 24,  opacity: 0, duration: 0.55, ease: 'power3.out', delay: 0.1 })
    gsap.from('.app-card', { y: 20, opacity: 0, stagger: 0.1, duration: 0.5, ease: 'power3.out', delay: 0.2 })
    gsap.from('.how-it-works', { y: 20, opacity: 0, duration: 0.5, ease: 'power3.out', delay: 0.4 })
    gsap.from('.features', { y: 20, opacity: 0, duration: 0.5, ease: 'power3.out', delay: 0.5 })
  }, { dependencies: [] })

  return (
    <div className="app">
      {/* Header */}
      <header className="header">
        <div className="header-inner">
          <div className="logo-btn">
            <div className="logo-icon-wrap">
              <IconDownload size={16} strokeWidth={2} />
            </div>
            <span className="logo-text">{t('siteTitle')}</span>
          </div>

          <div className="header-right">
            <ThemeToggle />
            <LanguagePicker />
          </div>
        </div>
      </header>

      {/* Main content */}
      <main className="main landing-main" ref={mainRef}>
        {/* Hero */}
        <div className="hero">
          <div className="hero-eyebrow">
            <IconDownload size={11} strokeWidth={2.5} />
            {t('heroEyebrow')}
          </div>
          <h1 className="hero-title">{t('heroTitle')}</h1>
          <p className="hero-sub">{t('heroSub')}</p>
        </div>

        {/* Download cards */}
        <div className="downloads-grid">
          {RELEASES.map(rel => (
            <ReleaseCard key={rel.id} release={rel} />
          ))}
        </div>

        {/* How it works */}
        <HowItWorks />

        {/* Feature strip */}
        <div className="features">
          <FeatureItem icon={<IconGlobe size={18} />}  colorClass="accent"  label={t('feat1Label')} desc={t('feat1Desc')} />
          <FeatureItem icon={<IconShield size={18} />} colorClass="green"   label={t('feat3Label')} desc={t('feat3Desc')} />
          <FeatureItem icon={<IconBolt size={18} />}   colorClass="yellow"  label={t('feat4Label')} desc={t('feat4Desc')} />
          <FeatureItem icon={<IconDownload size={18} />} colorClass="purple" label={t('featEdgeLabel')} desc={t('featEdgeDesc')} />
        </div>
      </main>

      <footer className="footer">
        <p>{t('footer')}</p>
      </footer>
    </div>
  )
}
