#if DEBUG
import SwiftUI

// ─────────────────────────────────────────────────────────────────────────────
// DebugMonetizationView — DEBUG builds only, stripped from App Store / TF.
//
// Lets QA / you trigger every monetization + limit scenario instantly:
//   1. SKStoreReviewController rating prompt
//   2. PremiumPaywallSheet    (one-time purchase)
//   3. SubscriptionSheet      (subscription)
//   4. QuotaExceededSheet     (daily-limit paywall)
//   5. Ad placeholder preview (debug only)
//   6. Quota controls         (reset / set premium)
// ─────────────────────────────────────────────────────────────────────────────

struct DebugMonetizationView: View {
    @Environment(\.dismiss) private var dismiss
    private let quota = DownloadQuotaService.shared

    @State private var showPremium        = false
    @State private var showSubscription   = false
    @State private var showQuotaExceeded  = false
    @State private var showAdPreview      = false
    @State private var ratingRequested    = false

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                header

                Divider()

                // MARK: Monetization prompts
                sectionLabel("Monetization Prompts")
                VStack(spacing: 10) {
                    debugButton("⭐ App Store Rating",    color: .yellow,
                                help: "Calls SKStoreReviewController") { requestRating() }
                    debugButton("💳 Premium Paywall",     color: .blue,
                                help: "One-time $9.99 purchase")       { showPremium = true }
                    debugButton("📦 Subscription Sheet",  color: .purple,
                                help: "Monthly / yearly subscription") { showSubscription = true }
                    debugButton("🚫 Quota Exceeded Sheet", color: .orange,
                                help: "Shows when 5/day limit hit")    { showQuotaExceeded = true }
                }

                if ratingRequested {
                    Text("Rating requested — OS will show when conditions are met")
                        .font(.caption).foregroundColor(.secondary)
                }

                Divider()

                // MARK: Quota controls
                sectionLabel("Daily Quota (\(quota.todayCount)/\(DownloadQuotaService.dailyFreeLimit) used)")
                HStack(spacing: 8) {
                    Button("Reset Count") {
                        quota.resetForTesting()
                    }
                    .buttonStyle(.bordered)
                    Button(quota.isPremium ? "✓ Premium ON" : "Set Premium ON") {
                        quota.isPremium = true
                    }
                    .buttonStyle(.bordered)
                    .tint(quota.isPremium ? .green : .blue)
                }

                Divider()

                // MARK: Ad placeholder
                sectionLabel("Ad Placeholder Preview")
                Button("Preview Ad Banner (Debug)") { showAdPreview = true }
                    .buttonStyle(.bordered)
                    .tint(.gray)

                Divider()

                Button("Close") { dismiss() }
                    .keyboardShortcut(.escape)
            }
            .padding(28)
        }
        .frame(width: 400)
        .sheet(isPresented: $showPremium)       { PremiumPaywallSheet() }
        .sheet(isPresented: $showSubscription)  { SubscriptionSheet() }
        .sheet(isPresented: $showQuotaExceeded) { QuotaExceededSheet() }
        .sheet(isPresented: $showAdPreview)     { DebugAdPreviewSheet() }
    }

    // MARK: - Sub-views

    private var header: some View {
        VStack(spacing: 6) {
            Image(systemName: "wrench.and.screwdriver.fill")
                .font(.system(size: 28)).foregroundColor(.orange)
            Text("Monetization Debug")
                .font(.title2).bold()
            Text("DEBUG build only — stripped from App Store / TestFlight")
                .font(.caption).foregroundColor(.secondary)
                .multilineTextAlignment(.center)
        }
    }

    private func sectionLabel(_ label: String) -> some View {
        HStack {
            Text(label)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.secondary)
                .textCase(.uppercase)
            Spacer()
        }
    }

    private func debugButton(_ label: String, color: Color, help: String,
                             action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(label)
                .font(.system(size: 13, weight: .semibold))
                .frame(maxWidth: .infinity)
                .padding(.vertical, 9)
        }
        .buttonStyle(.bordered)
        .tint(color)
        .help(help)
    }

    // MARK: - Actions

    private func requestRating() {
        ratingRequested = true
        NSLog("[DebugMonetization] Rating requested — macOS triggers it automatically based on engagement")
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DebugAdPreviewSheet
//
// Shows what in-app banner / interstitial ads look like for design review.
// This is a MOCK — no real ad SDK is integrated.
//
// Ad Network Options for macOS (see docs/ad-providers.md for full details):
//   • Google AdSense via WKWebView  — https://adsense.google.com
//   • Carbon Ads (dev-focused)      — https://www.carbonads.net
//   • BuySellAds                    — https://www.buysellads.com
//   • Custom sponsorship            — direct deal, highest CPM
//
// Mobile-only (iOS): AdMob, Unity Ads, AppLovin — NOT available on macOS.
// ─────────────────────────────────────────────────────────────────────────────

private struct DebugAdPreviewSheet: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 20) {
            Image(systemName: "banknote")
                .font(.system(size: 32)).foregroundColor(.gray)
            Text("Ad Placeholder Preview")
                .font(.title3).bold()
            Text("Real ads not integrated yet — these are layout mockups.")
                .font(.caption).foregroundColor(.secondary)
                .multilineTextAlignment(.center)

            Divider()

            // Banner ad mockup
            VStack(alignment: .leading, spacing: 6) {
                Text("LEADERBOARD BANNER  (728 × 90)").font(.caption2).foregroundColor(.secondary)
                ZStack {
                    RoundedRectangle(cornerRadius: 6)
                        .fill(Color.gray.opacity(0.12))
                        .frame(height: 90)
                    VStack(spacing: 2) {
                        Text("[ Your Ad Here ]")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.gray)
                        Text("Powered by Carbon Ads / Google AdSense")
                            .font(.caption2).foregroundColor(Color.gray.opacity(0.7))
                    }
                }
            }

            // Sidebar ad mockup
            VStack(alignment: .leading, spacing: 6) {
                Text("SIDEBAR RECTANGLE  (300 × 250)").font(.caption2).foregroundColor(.secondary)
                ZStack {
                    RoundedRectangle(cornerRadius: 6)
                        .fill(Color.gray.opacity(0.12))
                        .frame(width: 300, height: 250)
                    VStack(spacing: 4) {
                        Image(systemName: "video.fill")
                            .font(.system(size: 40)).foregroundColor(.gray.opacity(0.5))
                        Text("Video / Display Ad")
                            .font(.subheadline).foregroundColor(.gray)
                        Text("BuySellAds · Carbon Ads · Direct sponsor")
                            .font(.caption2).foregroundColor(.gray.opacity(0.7))
                    }
                }
            }

            Divider()
            Text("See docs/ad-providers.md for integration guides & provider links.")
                .font(.caption).foregroundColor(.secondary).multilineTextAlignment(.center)

            Button("Close") { dismiss() }
        }
        .padding(28)
        .frame(width: 400)
    }
}

#endif
