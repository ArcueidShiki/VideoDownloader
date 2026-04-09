import Foundation

public enum ValidationError: Error, LocalizedError, Equatable, Sendable {
    case emptyInput
    case inputTooLong(maxLength: Int)
    case nullBytesDetected
    case shellInjectionCharacters
    case invalidScheme(String)
    case privateIPAddress
    case malformedURL
    case invalidMagnetURI

    public var errorDescription: String? {
        switch self {
        case .emptyInput:
            return "Please enter a URL, magnet link, or path to a .torrent file."
        case .inputTooLong(let max):
            return "Input exceeds the maximum allowed length of \(max) characters."
        case .nullBytesDetected:
            return "Input contains invalid characters (null bytes)."
        case .shellInjectionCharacters:
            return "Input contains unsafe characters that are not allowed in a URL."
        case .invalidScheme(let scheme):
            return "'\(scheme)' is not a supported URL scheme. Use https, http, or a magnet link."
        case .privateIPAddress:
            return "Downloading from private or internal network addresses is not allowed."
        case .malformedURL:
            return "The URL could not be parsed. Please check it and try again."
        case .invalidMagnetURI:
            return "Invalid magnet link format. A valid magnet link must contain 'xt=urn:btih:'."
        }
    }
}

public struct InputValidator: Sendable {
    public static let maxInputLength = 2048

    private static let allowedSchemes: Set<String> = ["https", "http", "magnet"]

    // Characters that are never valid in a URL and indicate shell injection attempts
    // Note: & ? = # % are all valid URL characters — only these are blocked
    private static let shellDangerChars: Set<Character> = ["`", "$", "{", "}", "|", ";"]

    private static let privateIPPrefixes = [
        "192.168.", "10.", "172.16.", "172.17.", "172.18.", "172.19.",
        "172.20.", "172.21.", "172.22.", "172.23.", "172.24.", "172.25.",
        "172.26.", "172.27.", "172.28.", "172.29.", "172.30.", "172.31.",
        "169.254.", "127.", "::1", "fc00:", "fd",
    ]
    private static let privateHosts: Set<String> = ["localhost", "localhost.localdomain"]

    public init() {}

    /// Validates and sanitises the user-supplied input string.
    /// - Returns: the trimmed, validated string ready for use.
    /// - Throws: `ValidationError` describing exactly what is wrong.
    @discardableResult
    public func validate(_ input: String) throws -> String {
        let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)

        // ── Basic checks ────────────────────────────────────────────────────
        guard !trimmed.isEmpty else { throw ValidationError.emptyInput }

        guard trimmed.count <= Self.maxInputLength else {
            throw ValidationError.inputTooLong(maxLength: Self.maxInputLength)
        }

        guard !trimmed.contains("\0") else { throw ValidationError.nullBytesDetected }

        guard !trimmed.contains(where: { Self.shellDangerChars.contains($0) }) else {
            throw ValidationError.shellInjectionCharacters
        }

        // ── Scheme pre-check ─────────────────────────────────────────────────
        // Runs before URL(string:) and before the local-path branch, because
        // schemes like "javascript:" and "data:" don't contain "://" and would
        // otherwise fall through to the .malformedURL branch rather than
        // .invalidScheme. RFC 3986 scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
        if let colonIdx = trimmed.firstIndex(of: ":") {
            let rawScheme = String(trimmed[..<colonIdx]).lowercased()
            let isValidSchemeChars = !rawScheme.isEmpty &&
                rawScheme.unicodeScalars.allSatisfy {
                    CharacterSet.letters.union(CharacterSet(charactersIn: "+-.")
                    ).contains($0) || ($0.value >= 48 && $0.value <= 57 && rawScheme.first?.isLetter == true)
                }
            if isValidSchemeChars && !Self.allowedSchemes.contains(rawScheme) {
                throw ValidationError.invalidScheme(rawScheme)
            }
        }

        // ── Magnet URI ───────────────────────────────────────────────────────
        if trimmed.lowercased().hasPrefix("magnet:") {
            // Require xt=urn:btih: followed by a valid hex info-hash (≥32 chars)
            let btihRange = trimmed.range(
                of: #"xt=urn:btih:[0-9a-fA-F]{32,}"#,
                options: .regularExpression
            )
            guard btihRange != nil else { throw ValidationError.invalidMagnetURI }
            return trimmed
        }

        // ── Local .torrent file path ─────────────────────────────────────────
        // (no scheme → treat as a local filesystem path)
        if !trimmed.contains("://") {
            var isDir: ObjCBool = false
            let exists = FileManager.default.fileExists(atPath: trimmed, isDirectory: &isDir)
            if exists && !isDir.boolValue && trimmed.lowercased().hasSuffix(".torrent") {
                return trimmed
            }
            throw ValidationError.malformedURL
        }

        // ── HTTP / HTTPS URLs ────────────────────────────────────────────────
        guard let url = URL(string: trimmed) else { throw ValidationError.malformedURL }

        let scheme = url.scheme?.lowercased() ?? ""
        guard Self.allowedSchemes.contains(scheme) else {
            throw ValidationError.invalidScheme(scheme)
        }

        if let host = url.host?.lowercased() {
            if Self.privateHosts.contains(host) { throw ValidationError.privateIPAddress }
            if Self.privateIPPrefixes.contains(where: { host.hasPrefix($0) }) {
                throw ValidationError.privateIPAddress
            }
        }

        return trimmed
    }
}
