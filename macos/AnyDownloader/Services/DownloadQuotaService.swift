import Foundation
import Observation

/// Tracks how many downloads a free user has initiated today.
/// Resets automatically at midnight. Premium users bypass the limit entirely.
///
/// isPremium is backed by the Keychain (not UserDefaults) so it cannot be
/// trivially spoofed with `defaults write`. StoreKitManager keeps it in sync
/// after every purchase / entitlement refresh.
@Observable
@MainActor
final class DownloadQuotaService {

    static let shared       = DownloadQuotaService()
    static let dailyFreeLimit = 125
    private static let keychainKey = "isPremium"

    /// Downloads started today (resets at midnight).
    private(set) var todayCount: Int = 0

    /// Backed by Keychain — cannot be spoofed via `defaults write`.
    var isPremium: Bool {
        get { KeychainHelper.getBool(key: Self.keychainKey) }
        set { KeychainHelper.set(bool: newValue, key: Self.keychainKey) }
    }

    var isLimitReached: Bool { !isPremium && todayCount >= Self.dailyFreeLimit }
    var remaining: Int { isPremium ? .max : max(0, Self.dailyFreeLimit - todayCount) }

    // MARK: - Private

    private let defaults = UserDefaults.standard
    private let countKey = "quota_today_count"
    private let dateKey  = "quota_last_date"

    init() { refreshIfNewDay() }

    // MARK: - Public

    /// Call before adding a download. Returns `true` when the slot was consumed,
    /// `false` when the daily limit has been reached (caller should show paywall).
    @discardableResult
    func consumeSlot() -> Bool {
        refreshIfNewDay()
        guard !isLimitReached else { return false }
        todayCount += 1
        defaults.set(todayCount, forKey: countKey)
        defaults.set(todayDateString, forKey: dateKey)
        return true
    }

    /// Refund a previously consumed slot — call when `store.add()` throws
    /// (URL invalid, etc.) so no actual download started.
    func refundSlot() {
        guard !isPremium, todayCount > 0 else { return }
        todayCount -= 1
        defaults.set(todayCount, forKey: countKey)
    }

    /// Reset count to zero. For debug/testing only.
    func resetForTesting() {
        todayCount = 0
        isPremium  = false
        defaults.set(0,               forKey: countKey)
        defaults.set(todayDateString, forKey: dateKey)
    }

    // MARK: - Helpers

    private var todayDateString: String {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: Date())
    }

    private func refreshIfNewDay() {
        let stored = defaults.string(forKey: dateKey) ?? ""
        if stored != todayDateString {
            todayCount = 0
            defaults.set(0,               forKey: countKey)
            defaults.set(todayDateString, forKey: dateKey)
        } else {
            todayCount = defaults.integer(forKey: countKey)
        }
    }
}
