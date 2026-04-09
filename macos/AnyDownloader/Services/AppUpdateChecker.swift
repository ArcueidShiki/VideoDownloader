import AppKit
import Foundation
import Observation

// ─────────────────────────────────────────────────────────────────────────────
// AppUpdateChecker
//
// Queries the public App Store lookup API to compare the latest published
// version against the running bundle version. On update detection, sets
// `updateAvailable = true` and provides the macappstore:// deep-link URL.
//
// Usage:
//   1. Replace APP_STORE_ID below with the numeric ID from App Store Connect
//      (found under App Information → Apple ID).
//   2. Call `await AppUpdateChecker.shared.checkForUpdates()` on app launch.
//   3. Observe `updateAvailable` to show the update banner.
// ─────────────────────────────────────────────────────────────────────────────

@Observable
@MainActor
final class AppUpdateChecker {

    static let shared = AppUpdateChecker()

    /// Set to the numeric App Store ID once the app is published.
    /// Example: "6478123456"
    static let appStoreID = "YOUR_APP_STORE_ID"

    var updateAvailable = false
    var latestVersion: String?
    var releaseNotes: String?

    private(set) var appStoreURL: URL?

    private var currentVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"
    }

    private init() {}

    // MARK: - Check

    /// Silently checks for an update. Safe to call on every launch.
    func checkForUpdates() async {
        let lookupURL = "https://itunes.apple.com/lookup?bundleId=\(Bundle.main.bundleIdentifier ?? "com.anydownloader.app")"
        guard let url = URL(string: lookupURL) else { return }
        do {
            let (data, response) = try await URLSession.shared.data(from: url)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { return }
            let decoded = try JSONDecoder().decode(AppStoreLookupResponse.self, from: data)
            guard let result = decoded.results.first else { return }

            latestVersion = result.version
            releaseNotes = result.releaseNotes
            if let trackURL = URL(string: result.trackViewUrl) {
                // Prefer macappstore:// so it opens in the App Store app, not Safari
                let deepLink = result.trackViewUrl
                    .replacingOccurrences(of: "https://apps.apple.com",
                                          with: "macappstore://apps.apple.com")
                appStoreURL = URL(string: deepLink) ?? trackURL
            }
            updateAvailable = isNewer(result.version, than: currentVersion)
        } catch {
            // Update checks are best-effort — never interrupt the user on failure.
        }
    }

    // MARK: - Open App Store

    func openAppStorePage() {
        if let url = appStoreURL {
            NSWorkspace.shared.open(url)
        } else if Self.appStoreID != "YOUR_APP_STORE_ID",
                  let url = URL(string: "macappstore://apps.apple.com/app/id\(Self.appStoreID)") {
            NSWorkspace.shared.open(url)
        } else {
            // Fallback: open Mac App Store and let user search
            NSWorkspace.shared.open(URL(string: "macappstore://")!)
        }
    }

    // MARK: - Version comparison

    /// Returns true when `latest` is strictly greater than `current`.
    private func isNewer(_ latest: String, than current: String) -> Bool {
        let lhs = latest.components(separatedBy: ".").compactMap(Int.init)
        let rhs = current.components(separatedBy: ".").compactMap(Int.init)
        let len = max(lhs.count, rhs.count)
        for i in 0..<len {
            let l = i < lhs.count ? lhs[i] : 0
            let r = i < rhs.count ? rhs[i] : 0
            if l > r { return true }
            if l < r { return false }
        }
        return false
    }
}

// MARK: - App Store Lookup API response

private struct AppStoreLookupResponse: Decodable {
    let results: [AppStoreResult]
}

private struct AppStoreResult: Decodable {
    let version: String
    let trackViewUrl: String
    let releaseNotes: String?
}
