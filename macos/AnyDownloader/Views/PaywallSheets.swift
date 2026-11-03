import SwiftUI

// ─────────────────────────────────────────────────────────────────────────────
// Payment method logos shown at the bottom of every paywall sheet.
// Apple Pay + major card schemes are handled by the App Store.
// Stripe / PayPal / Alipay / WeChat would require a separate web checkout.
// ─────────────────────────────────────────────────────────────────────────────

struct PaymentMethodBadges: View {
    var body: some View {
        VStack(spacing: 5) {
            Text("Accepted payment methods")
                .font(.caption2)
                .foregroundColor(.secondary)

            // Row 1 — App Store supported
            HStack(spacing: 5) {
                ForEach(["apple.logo", "creditcard"], id: \.self) { icon in
                    Image(systemName: icon)
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .padding(.horizontal, 6).padding(.vertical, 3)
                        .background(Color.secondary.opacity(0.1))
                        .cornerRadius(4)
                }
                badge("PayPal")
                badge("Visa")
                badge("MC")
                badge("Amex")
            }

            // Row 2 — Web checkout (Stripe) supported separately
            HStack(spacing: 5) {
                badge("Google Pay")
                badge("Alipay")
                badge("WeChat Pay")
                badge("Stripe")
            }

            Text("App Store payments via Apple · Direct checkout via Stripe")
                .font(.caption2)
                .foregroundColor(Color.secondary.opacity(0.7))
        }
    }

    private func badge(_ label: String) -> some View {
        Text(label)
            .font(.system(size: 10, weight: .medium))
            .foregroundColor(.secondary)
            .padding(.horizontal, 5).padding(.vertical, 2)
            .background(Color.secondary.opacity(0.1))
            .cornerRadius(4)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PremiumPaywallSheet — one-time purchase
// ─────────────────────────────────────────────────────────────────────────────

struct PremiumPaywallSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var showPayment = false

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                Image(systemName: "crown.fill")
                    .font(.system(size: 44))
                    .foregroundColor(.yellow)

                Text("AnyDownloader Premium")
                    .font(.title2).bold()

                Text("Unlimited downloads, batch mode,\nand priority support — one-time payment.")
                    .font(.body)
                    .multilineTextAlignment(.center)
                    .foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                VStack(spacing: 8) {
                    priceRow(label: "Lifetime License", price: "$9.99", highlight: true)
                    priceRow(label: "Family License (5 Macs)", price: "$24.99", highlight: false)
                }
                .padding(.horizontal)

                HStack(spacing: 12) {
                    Button("Maybe Later") { dismiss() }
                        .buttonStyle(.bordered)
                    Button("Buy Now — $9.99") { showPayment = true }
                        .buttonStyle(.borderedProminent)
                        .tint(.yellow)
                }

                PaymentMethodBadges()

                Text("One-time purchase. No subscription. All future updates included.")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(32)
        }
        .frame(width: 380, height: 480)
        .sheet(isPresented: $showPayment) { PaymentView() }
    }

    private func priceRow(label: String, price: String, highlight: Bool) -> some View {
        HStack {
            Text(label).font(.subheadline)
            Spacer()
            Text(price).font(.subheadline).bold()
                .foregroundColor(highlight ? .yellow : .primary)
        }
        .padding(10)
        .background(highlight ? Color.yellow.opacity(0.1) : Color.clear)
        .cornerRadius(8)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SubscriptionSheet — recurring subscription / ad-free
// ─────────────────────────────────────────────────────────────────────────────

struct SubscriptionSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var selectedPlan: Plan = .yearly
    @State private var showPayment = false

    enum Plan: String, CaseIterable, Identifiable {
        case monthly = "Monthly"
        case yearly  = "Yearly"
        var id: String { rawValue }
        var price: String  { self == .monthly ? "$1.99/mo" : "$9.99/yr" }
        var saving: String { self == .yearly  ? "Save 58%" : "" }
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                Image(systemName: "sparkles")
                    .font(.system(size: 44))
                    .foregroundColor(.purple)

                Text("Subscribe & Remove Ads")
                    .font(.title2).bold()

                Text("No ads. No daily limits. No interruptions.\nCancel any time from App Store.")
                    .font(.body)
                    .multilineTextAlignment(.center)
                    .foregroundColor(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                // Plan picker — segmented, text must not overflow
                Picker("Plan", selection: $selectedPlan) {
                    ForEach(Plan.allCases) { plan in
                        Text("\(plan.rawValue)  \(plan.price)\(plan.saving.isEmpty ? "" : "  (\(plan.saving))")")
                            .tag(plan)
                    }
                }
                .pickerStyle(.segmented)
                .padding(.horizontal)

                // Selected plan detail card
                planDetailCard

                HStack(spacing: 12) {
                    Button("No Thanks") { dismiss() }
                        .buttonStyle(.bordered)

                    Button {
                        showPayment = true
                    } label: {
                        Text("Subscribe  ·  \(selectedPlan.price)")
                            .lineLimit(1)
                            .fixedSize()
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(.purple)
                }

                PaymentMethodBadges()

                Text("Subscription auto-renews. Cancel anytime in\nSettings › Apple ID › Subscriptions.")
                    .font(.caption2)
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal)
            }
            .padding(32)
        }
        .frame(width: 400, height: 520)
        .sheet(isPresented: $showPayment) { PaymentView() }
    }

    @ViewBuilder
    private var planDetailCard: some View {
        VStack(spacing: 6) {
            HStack {
                VStack(alignment: .leading, spacing: 3) {
                    Text(selectedPlan.rawValue + " Plan")
                        .font(.subheadline).bold()
                    if !selectedPlan.saving.isEmpty {
                        Text(selectedPlan.saving)
                            .font(.caption)
                            .foregroundColor(.green)
                    }
                }
                Spacer()
                Text(selectedPlan.price)
                    .font(.title3).bold()
                    .foregroundColor(.purple)
            }
        }
        .padding(14)
        .background(Color.purple.opacity(0.07))
        .cornerRadius(10)
        .padding(.horizontal)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// QuotaExceededSheet — shown when the 5/day free limit is reached
// ─────────────────────────────────────────────────────────────────────────────

struct QuotaExceededSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var showPayment = false

    private let quota = DownloadQuotaService.shared

    var body: some View {
        VStack(spacing: 22) {
            Image(systemName: "clock.badge.xmark.fill")
                .font(.system(size: 48))
                .foregroundStyle(.orange, .orange.opacity(0.25))

            VStack(spacing: 6) {
                Text("Daily Limit Reached")
                    .font(.title2).bold()
                Text("You've used all \(DownloadQuotaService.dailyFreeLimit) free downloads for today.")
                    .font(.body)
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
            }

            VStack(spacing: 10) {
                Button {
                    dismiss()
                    showPayment = true
                } label: {
                    Label("Upgrade to Premium", systemImage: "crown.fill")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .tint(.yellow)
                .controlSize(.large)
            }
            .padding(.horizontal)

            Button("Try Again Tomorrow") { dismiss() }
                .buttonStyle(.plain)
                .foregroundColor(.secondary)
                .font(.caption)
        }
        .padding(36)
        .frame(width: 360)
        .sheet(isPresented: $showPayment) { PaymentView() }
    }
}
