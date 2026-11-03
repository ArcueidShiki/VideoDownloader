import SwiftUI
import StoreKit
import WebKit

// ─────────────────────────────────────────────────────────────────────────────
// PaymentView — full payment hub.
//
// Tier 1 (App Store, recommended):
//   • StoreKit 2 handles Visa, MC, Amex, Apple Pay, PayPal (via App Store)
//
// Tier 2 (Web checkout via Stripe, opens in Safari):
//   • Stripe Checkout: Visa, MC, Apple Pay, Google Pay, Alipay, WeChat Pay
//   • Create payment links at https://dashboard.stripe.com/payment-links
//   • Replace the placeholder URLs below with your real Stripe Payment Link IDs
//
// Documentation references:
//   StoreKit 2 — https://developer.apple.com/documentation/storekit
//   Stripe Payment Links — https://docs.stripe.com/payment-links
//   Stripe Alipay — https://docs.stripe.com/payments/alipay
//   Stripe WeChat Pay — https://docs.stripe.com/payments/wechat-pay
// ─────────────────────────────────────────────────────────────────────────────

struct PaymentView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var manager = StoreKitManager.shared
    @State private var tab: PaymentTab = .appStore
    @State private var showStripeCheckout = false
    @State private var stripeCheckoutURL: URL?
    @State private var showRestoreAlert = false

    enum PaymentTab: String, CaseIterable, Identifiable {
        case appStore = "App Store"
        case stripe   = "Stripe / Other"
        var id: String { rawValue }
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                // Tab picker
                Picker("Payment Method", selection: $tab) {
                    ForEach(PaymentTab.allCases) { t in Text(t.rawValue).tag(t) }
                }
                .pickerStyle(.segmented)
                .padding()

                Divider()

                ScrollView {
                    VStack(spacing: 24) {
                        switch tab {
                        case .appStore: appStoreSection
                        case .stripe:   stripeSection
                        }
                    }
                    .padding(24)
                }
            }
            .navigationTitle("Upgrade AnyDownloader")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                        .keyboardShortcut(.escape)
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Restore Purchases") {
                        Task { await manager.restorePurchases(); showRestoreAlert = true }
                    }
                    .font(.caption)
                    .disabled(manager.isPurchasing)
                }
            }
            .alert("Restore Complete",
                   isPresented: $showRestoreAlert) { Button("OK") {} }
                message: {
                    Text(manager.isPremium
                         ? "Your purchase has been restored."
                         : "No previous purchase found for this Apple ID.")
                }
        }
        .frame(width: 500, height: 540)
        .task { await manager.loadProducts() }
        .sheet(isPresented: $showStripeCheckout) {
            if let url = stripeCheckoutURL {
                StripeCheckoutView(url: url)
            }
        }
    }

    // MARK: - App Store section (StoreKit 2)

    @ViewBuilder
    private var appStoreSection: some View {
        // Premium status banner
        if manager.isPremium {
            premiumActiveBanner
        }

        // Product cards
        if manager.isLoadingProducts {
            ProgressView("Loading products…").frame(maxWidth: .infinity)
        } else if manager.products.isEmpty {
            Text("Could not load products. Check your internet connection.")
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
        } else {
            ForEach(manager.products, id: \.id) { product in
                ProductCard(product: product, isPurchased: manager.purchasedIDs.contains(product.id)) {
                    Task { await manager.purchase(product) }
                }
            }
        }

        if let err = manager.errorMessage {
            Text(err).foregroundColor(.red).font(.caption).multilineTextAlignment(.center)
        }

        appStorePaymentBadges
    }

    private var premiumActiveBanner: some View {
        HStack(spacing: 10) {
            Image(systemName: "checkmark.seal.fill").foregroundColor(.green).font(.title3)
            VStack(alignment: .leading, spacing: 2) {
                Text("Premium Active").font(.subheadline).bold()
                Text("Thank you! All limits removed.").font(.caption).foregroundColor(.secondary)
            }
            Spacer()
        }
        .padding(14)
        .background(Color.green.opacity(0.1))
        .cornerRadius(10)
    }

    private var appStorePaymentBadges: some View {
        VStack(spacing: 6) {
            Divider()
            Text("Processed by the App Store · Secure & private")
                .font(.caption2).foregroundColor(.secondary)
            HStack(spacing: 8) {
                appStoreBadge("Apple Pay",  systemImage: "apple.logo")
                appStoreBadge("Visa",       systemImage: "creditcard")
                appStoreBadge("Mastercard", systemImage: "creditcard.fill")
                appStoreBadge("Amex",       systemImage: "creditcard")
                appStoreBadge("PayPal",     systemImage: "p.circle")
            }
            Text("The App Store also accepts gift cards, store credit, and carrier billing.")
                .font(.caption2).foregroundColor(Color.secondary.opacity(0.7))
                .multilineTextAlignment(.center)
        }
    }

    private func appStoreBadge(_ label: String, systemImage: String) -> some View {
        Label(label, systemImage: systemImage)
            .font(.system(size: 9, weight: .medium))
            .foregroundColor(.secondary)
            .padding(.horizontal, 5).padding(.vertical, 3)
            .background(Color.secondary.opacity(0.1))
            .cornerRadius(4)
    }

    // MARK: - Stripe section (web checkout, no SDK required)

    @ViewBuilder
    private var stripeSection: some View {
        VStack(spacing: 16) {
            Image(systemName: "globe")
                .font(.system(size: 36))
                .foregroundColor(.accentColor)

            Text("Pay via Stripe Web Checkout")
                .font(.title3).bold()

            Text("Opens in your browser. Supports Visa, Mastercard,\nApple Pay, Google Pay, Alipay, and WeChat Pay.")
                .font(.body)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)

            // Stripe Payment Link buttons
            VStack(spacing: 10) {
                stripeButton(
                    title: "Lifetime Premium — $9.99",
                    subtitle: "One-time · All features forever",
                    icon: "crown.fill", tint: .yellow,
                    // Replace with your real Stripe Payment Link from dashboard.stripe.com/payment-links
                    urlString: "https://buy.stripe.com/YOUR_PREMIUM_LINK"
                )

                stripeButton(
                    title: "Monthly Subscription — $1.99/mo",
                    subtitle: "Auto-renews · Cancel anytime",
                    icon: "sparkles", tint: .purple,
                    urlString: "https://buy.stripe.com/YOUR_MONTHLY_LINK"
                )

                stripeButton(
                    title: "Yearly Subscription — $9.99/yr",
                    subtitle: "Save 58% vs monthly · Auto-renews",
                    icon: "calendar.badge.checkmark", tint: .blue,
                    urlString: "https://buy.stripe.com/YOUR_YEARLY_LINK"
                )
            }

            Divider()

            // Supported methods grid
            stripePaymentGrid

            Text("After payment, click \"Restore Purchases\" (App Store tab) to unlock premium,\nor contact support with your receipt if purchasing via Stripe.")
                .font(.caption2)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.horizontal)
        }
    }

    private func stripeButton(title: String, subtitle: String,
                              icon: String, tint: Color,
                              urlString: String) -> some View {
        Button {
            if let url = URL(string: urlString) {
                // Stripe Payment Links are fully hosted — just open in Safari
                NSWorkspace.shared.open(url)
            }
        } label: {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .font(.title3)
                    .foregroundColor(tint)
                    .frame(width: 32)
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.subheadline).bold()
                    Text(subtitle).font(.caption).foregroundColor(.secondary)
                }
                Spacer()
                Image(systemName: "arrow.up.right.square")
                    .font(.caption)
                    .foregroundColor(.secondary)
            }
            .padding(14)
            .background(tint.opacity(0.07))
            .cornerRadius(10)
        }
        .buttonStyle(.plain)
    }

    private var stripePaymentGrid: some View {
        VStack(spacing: 8) {
            Text("Stripe-supported payment methods")
                .font(.caption2).foregroundColor(.secondary)
            FlowLayout(spacing: 6) {
                ForEach(stripePaymentMethods, id: \.name) { method in
                    HStack(spacing: 4) {
                        Image(systemName: method.icon)
                            .font(.system(size: 10))
                        Text(method.name)
                            .font(.system(size: 10, weight: .medium))
                    }
                    .foregroundColor(.secondary)
                    .padding(.horizontal, 6).padding(.vertical, 3)
                    .background(Color.secondary.opacity(0.1))
                    .cornerRadius(4)
                }
            }
        }
    }

    private struct PaymentMethod { let name: String; let icon: String }
    private let stripePaymentMethods: [PaymentMethod] = [
        .init(name: "Visa",        icon: "creditcard"),
        .init(name: "Mastercard",  icon: "creditcard.fill"),
        .init(name: "Amex",        icon: "creditcard"),
        .init(name: "Apple Pay",   icon: "apple.logo"),
        .init(name: "Google Pay",  icon: "g.circle"),
        .init(name: "PayPal",      icon: "p.circle"),
        .init(name: "Alipay",      icon: "a.circle"),
        .init(name: "WeChat Pay",  icon: "message.circle"),
        .init(name: "iDEAL",       icon: "eurosign.circle"),
        .init(name: "SEPA",        icon: "eurosign"),
        .init(name: "Klarna",      icon: "k.circle"),
        .init(name: "Afterpay",    icon: "cart"),
    ]
}

// ─────────────────────────────────────────────────────────────────────────────
// ProductCard — one StoreKit product displayed as a tappable card
// ─────────────────────────────────────────────────────────────────────────────

private struct ProductCard: View {
    let product: Product
    let isPurchased: Bool
    let onBuy: () -> Void
    @State private var manager = StoreKitManager.shared

    private var isSubscription: Bool {
        product.type == .autoRenewable
    }

    var body: some View {
        HStack(spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text(product.displayName)
                    .font(.subheadline).bold()
                if let desc = product.description.nilIfEmpty {
                    Text(desc)
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer()

            if isPurchased {
                Label("Owned", systemImage: "checkmark.circle.fill")
                    .font(.caption)
                    .foregroundColor(.green)
            } else {
                Button(action: onBuy) {
                    if manager.isPurchasing {
                        ProgressView().scaleEffect(0.7)
                    } else {
                        Text(product.displayPrice)
                            .font(.subheadline).bold()
                    }
                }
                .buttonStyle(.borderedProminent)
                .tint(isSubscription ? .purple : .yellow)
                .disabled(manager.isPurchasing)
            }
        }
        .padding(14)
        .background(Color(NSColor.controlBackgroundColor))
        .cornerRadius(10)
        .overlay(RoundedRectangle(cornerRadius: 10)
            .stroke(isPurchased ? Color.green.opacity(0.4) : Color.clear, lineWidth: 1.5))
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// StripeCheckoutView — WKWebView for in-app Stripe checkout
// (alternative to opening Safari; requires WKNavigationDelegate for redirect)
// ─────────────────────────────────────────────────────────────────────────────

struct StripeCheckoutView: View {
    let url: URL
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Secure Checkout · Powered by Stripe")
                    .font(.caption).foregroundColor(.secondary)
                Spacer()
                Button("Done") { dismiss() }
            }
            .padding(.horizontal, 16).padding(.vertical, 10)
            .background(Color(NSColor.windowBackgroundColor))
            Divider()
            StripeWebView(url: url)
        }
        .frame(width: 520, height: 640)
    }
}

// NSViewRepresentable wrapping WKWebView for the Stripe hosted page
private struct StripeWebView: NSViewRepresentable {
    let url: URL

    func makeNSView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        let webView = WKWebView(frame: .zero, configuration: config)
        webView.load(URLRequest(url: url))
        return webView
    }

    func updateNSView(_ webView: WKWebView, context: Context) {}
}

// ─────────────────────────────────────────────────────────────────────────────
// FlowLayout — wraps badges in a left-aligned flowing layout
// ─────────────────────────────────────────────────────────────────────────────

private struct FlowLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        layout(subviews: subviews, width: proposal.width ?? 400).size
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize,
                       subviews: Subviews, cache: inout ()) {
        let result = layout(subviews: subviews, width: bounds.width)
        for (index, frame) in result.frames.enumerated() {
            subviews[index].place(at: CGPoint(x: bounds.minX + frame.minX,
                                              y: bounds.minY + frame.minY),
                                  proposal: ProposedViewSize(frame.size))
        }
    }

    private struct LayoutResult { var size: CGSize; var frames: [CGRect] }

    private func layout(subviews: Subviews, width: CGFloat) -> LayoutResult {
        var x: CGFloat = 0; var y: CGFloat = 0
        var rowHeight: CGFloat = 0; var frames: [CGRect] = []
        var maxWidth: CGFloat = 0

        for sub in subviews {
            let sz = sub.sizeThatFits(.unspecified)
            if x + sz.width > width && x > 0 {
                x = 0; y += rowHeight + spacing; rowHeight = 0
            }
            frames.append(CGRect(origin: CGPoint(x: x, y: y), size: sz))
            x += sz.width + spacing
            rowHeight = max(rowHeight, sz.height)
            maxWidth = max(maxWidth, x)
        }
        return LayoutResult(size: CGSize(width: maxWidth, height: y + rowHeight), frames: frames)
    }
}

// MARK: - Helpers

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
