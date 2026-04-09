/**
 * AdSlot — Environment-controlled advertisement component
 *
 * Behavior is driven by two build-time env vars:
 *
 *   VITE_SHOW_ADS=false (default) → renders nothing at all
 *   VITE_SHOW_ADS=true + VITE_AD_MODE=placeholder → gray placeholder boxes
 *   VITE_SHOW_ADS=true + VITE_AD_MODE=live       → real Monetag ad containers
 *
 * Zone IDs are assigned in the Monetag publisher dashboard and passed as
 * props so each placement maps to its own revenue unit.
 */

const SHOW_ADS = import.meta.env.VITE_SHOW_ADS === 'true'
const AD_MODE  = (import.meta.env.VITE_AD_MODE as string) ?? 'placeholder'

export interface AdSlotProps {
  /** Monetag zone ID from publisher dashboard — required in live mode */
  zoneId?: string
  className?: string
}

export function AdSlot({ zoneId, className = '' }: AdSlotProps) {
  // Kill switch: VITE_SHOW_ADS=false → nothing rendered, layout shifts disappear
  if (!SHOW_ADS) return null

  if (AD_MODE === 'live' && zoneId) {
    /**
     * In live mode, Monetag fills this container via its zone scripts.
     * The `data-zone` attribute is used by the Monetag init script to locate
     * and populate each unit. No manual scripting needed here — Monetag's
     * publisher script (injected in App.tsx) handles everything.
     */
    return (
      <div
        className={`ad-slot-live ${className}`}
        data-zone={zoneId}
        id={`monetag-zone-${zoneId}`}
      />
    )
  }

  // Placeholder: shows bounding box so layout is identical to live ads
  return (
    <div className={`ad-slot ${className}`}>
      <span className="ad-tag">AD</span>
      <p className="ad-copy">Advertisement placeholder</p>
    </div>
  )
}
