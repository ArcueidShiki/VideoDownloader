import Foundation

public struct DependencyStatus: Sendable {
    public let aria2cPath: String?
    public let ytDlpPath: String?

    public var aria2cMissing: Bool { aria2cPath == nil }
    public var ytDlpMissing: Bool  { ytDlpPath == nil }
    public var allPresent: Bool    { !aria2cMissing && !ytDlpMissing }

    public static func check() -> DependencyStatus {
        DependencyStatus(
            aria2cPath: Self.find("aria2c"),
            ytDlpPath:  Self.find("yt-dlp")
        )
    }

    private static func find(_ name: String) -> String? {
        // Bundled binary takes priority (portable .app bundle)
        if let bundled = Bundle.main.resourceURL?.appendingPathComponent(name).path,
           FileManager.default.isExecutableFile(atPath: bundled) {
            return bundled
        }
        let candidates: [String]
        switch name {
        case "aria2c":
            candidates = [
                "/opt/homebrew/bin/aria2c",
                "/usr/local/bin/aria2c",
                "/usr/bin/aria2c",
            ]
        case "yt-dlp":
            candidates = [
                "/opt/homebrew/bin/yt-dlp",
                "/usr/local/bin/yt-dlp",
                "/usr/bin/yt-dlp",
            ]
        default:
            candidates = ["/opt/homebrew/bin/\(name)", "/usr/local/bin/\(name)"]
        }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }
}
