import Foundation
import StoreKit
import Observation

// ─────────────────────────────────────────────────────────────────────────────
// Product IDs — must match exactly what's configured in App Store Connect.
// Create these under your app → In-App Purchases before shipping.
// ─────────────────────────────────────────────────────────────────────────────

enum AnyDownloaderProduct: String, CaseIterable {
    case premium     = "com.anydownloader.premium"         // non-consumable · $9.99
    case subMonthly  = "com.anydownloader.sub.monthly"     // auto-renewing  · $1.99/mo
    case subYearly   = "com.anydownloader.sub.yearly"      // auto-renewing  · $9.99/yr
}

// ─────────────────────────────────────────────────────────────────────────────
// StoreKitManager
// • Loads products from the App Store on demand
// • Handles purchases and verifies transactions on-device (StoreKit 2)
// • Drives the DownloadQuotaService isPremium flag via Keychain
// • Listens for background transaction updates (e.g., subscription renewals)
// ─────────────────────────────────────────────────────────────────────────────

@Observable
@MainActor
final class StoreKitManager {

    static let shared = StoreKitManager()

    var products:         [Product]     = []
    var purchasedIDs:     Set<String>   = []
    var isPurchasing:     Bool          = false
    var isLoadingProducts: Bool         = false
    var errorMessage:     String?

    /// Derived: premium if any product is purchased/subscribed.
    var isPremium: Bool { !purchasedIDs.isEmpty }

    // Background task that forwards new transactions from the App Store.
    // @ObservationIgnored: it's a private handle, not a view-observable property —
    // prevents the @Observable macro from generating nonisolated access wrappers.
    @ObservationIgnored private var updateListenerTask: Task<Void, Never>?

    private init() {
        updateListenerTask = listenForTransactionUpdates()
    }

    // MARK: - Load products

    func loadProducts() async {
        isLoadingProducts = true
        defer { isLoadingProducts = false }
        do {
            let ids = AnyDownloaderProduct.allCases.map(\.rawValue)
            products = try await Product.products(for: ids)
                .sorted { $0.price < $1.price }
        } catch {
            errorMessage = "Could not load products: \(error.localizedDescription)"
        }
    }

    // MARK: - Purchase

    /// Returns `true` on successful verified purchase.
    @discardableResult
    func purchase(_ product: Product) async -> Bool {
        isPurchasing = true
        defer { isPurchasing = false }
        errorMessage = nil

        do {
            let result = try await product.purchase()
            switch result {
            case .success(let verification):
                return await handleVerification(verification)
            case .userCancelled:
                return false
            case .pending:
                errorMessage = "Purchase is awaiting approval (e.g., Ask to Buy)."
                return false
            @unknown default:
                return false
            }
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }

    // MARK: - Restore

    func restorePurchases() async {
        isPurchasing = true
        defer { isPurchasing = false }
        errorMessage = nil
        do {
            try await AppStore.sync()
            await refreshEntitlements()
        } catch {
            errorMessage = "Restore failed: \(error.localizedDescription)"
        }
    }

    // MARK: - Entitlement refresh (call on app launch)

    func refreshEntitlements() async {
        var ids = Set<String>()
        for await result in Transaction.currentEntitlements {
            if case .verified(let tx) = result, !tx.isExpired {
                ids.insert(tx.productID)
            }
        }
        purchasedIDs = ids
        // Sync to Keychain so DownloadQuotaService can read it without StoreKit
        KeychainHelper.set(bool: !ids.isEmpty, key: "isPremium")
        DownloadQuotaService.shared.isPremium = !ids.isEmpty
    }

    // MARK: - Private helpers

    @discardableResult
    private func handleVerification(_ verification: VerificationResult<Transaction>) async -> Bool {
        guard case .verified(let tx) = verification else {
            errorMessage = "Transaction could not be verified."
            return false
        }
        await tx.finish()
        purchasedIDs.insert(tx.productID)
        KeychainHelper.set(bool: true, key: "isPremium")
        DownloadQuotaService.shared.isPremium = true
        return true
    }

    private func listenForTransactionUpdates() -> Task<Void, Never> {
        Task(priority: .background) { [weak self] in
            for await result in Transaction.updates {
                // handleVerification is @MainActor-isolated; `await` hops automatically.
                await self?.handleVerification(result)
            }
        }
    }
}

// MARK: - Product convenience

extension Product {
    /// Whether this product is an active subscription or non-consumable the user owns.
    var isExpired: Bool { false }  // placeholder — use Transaction.isExpired in real code
}

extension Transaction {
    var isExpired: Bool {
        guard let date = expirationDate else { return false }
        return date < Date()
    }
}
