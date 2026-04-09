import Foundation
import Security

/// Minimal Keychain wrapper for storing boolean/string values securely.
///
/// Used for `isPremium` because UserDefaults can be trivially overwritten via
/// `defaults write com.anydownloader.app quota_is_premium -bool YES` in Terminal.
/// The Keychain is sandboxed to this app's entitlement group and cannot be
/// modified by other processes without the same code-signing identity.
enum KeychainHelper {

    private static let service = "com.anydownloader.app"

    // MARK: - Bool

    static func set(bool value: Bool, key: String) {
        let data = Data([value ? 1 : 0])
        var query = baseQuery(key: key)

        // Try update first; fall back to add
        let status = SecItemUpdate(query as CFDictionary,
                                   [kSecValueData as String: data] as CFDictionary)
        if status == errSecItemNotFound {
            query[kSecValueData as String] = data
            SecItemAdd(query as CFDictionary, nil)
        }
    }

    static func getBool(key: String) -> Bool {
        var query = baseQuery(key: key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data,
              data.first == 1 else { return false }
        return true
    }

    static func delete(key: String) {
        SecItemDelete(baseQuery(key: key) as CFDictionary)
    }

    // MARK: - Private

    private static func baseQuery(key: String) -> [String: Any] {
        [
            kSecClass as String:                    kSecClassGenericPassword,
            kSecAttrService as String:              service,
            kSecAttrAccount as String:              key,
            // Use the modern Data Protection Keychain so macOS never prompts
            // for the login-keychain password on unsigned / dev builds.
            kSecUseDataProtectionKeychain as String: true,
            // Accessible after first unlock: no user-presence prompt on each access.
            kSecAttrAccessible as String:           kSecAttrAccessibleAfterFirstUnlock,
        ]
    }
}
