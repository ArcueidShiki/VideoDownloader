import Foundation

public struct URLClassifier: Sendable {
    private static let ytdlpHosts: Set<String> = [
        "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com",
        "tiktok.com", "www.tiktok.com", "vm.tiktok.com",
        "bilibili.com", "www.bilibili.com",
        "douyin.com", "www.douyin.com",
        "vimeo.com", "www.vimeo.com",
        "twitch.tv", "www.twitch.tv",
        "twitter.com", "x.com", "www.twitter.com",
        "instagram.com", "www.instagram.com",
        "facebook.com", "www.facebook.com",
    ]

    public init() {}

    public func classify(_ input: String) -> DownloadType {
        let trimmed = input.trimmingCharacters(in: .whitespaces)
        if trimmed.lowercased().hasPrefix("magnet:") { return .torrent }
        if trimmed.lowercased().hasSuffix(".torrent") { return .torrent }
        guard let url = URL(string: trimmed), let host = url.host?.lowercased() else {
            return .http
        }
        if Self.ytdlpHosts.contains(host) { return .ytdlp }
        if url.pathExtension.lowercased() == "torrent" { return .torrent }
        return .http
    }
}
