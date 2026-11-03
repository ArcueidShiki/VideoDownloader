import Testing
@testable import AnyDownloaderCore

@Suite("URLClassifier")
struct URLClassifierTests {
    let classifier = URLClassifier()

    @Test("YouTube URL → ytdlp")
    func youtube() {
        #expect(classifier.classify("https://www.youtube.com/watch?v=dQw4w9WgXcQ") == .ytdlp)
        #expect(classifier.classify("https://youtu.be/dQw4w9WgXcQ") == .ytdlp)
    }

    @Test("TikTok URL → ytdlp")
    func tiktok() {
        #expect(classifier.classify("https://www.tiktok.com/@user/video/123") == .ytdlp)
    }

    @Test("Bilibili URL → ytdlp")
    func bilibili() {
        #expect(classifier.classify("https://www.bilibili.com/video/BV1xx411c7mu") == .ytdlp)
    }

    @Test("magnet link → torrent")
    func magnet() {
        #expect(classifier.classify("magnet:?xt=urn:btih:abc123") == .torrent)
    }

    @Test(".torrent file URL → torrent")
    func torrentFile() {
        #expect(classifier.classify("https://example.com/ubuntu.torrent") == .torrent)
        #expect(classifier.classify("/Users/me/ubuntu.torrent") == .torrent)
    }

    @Test("plain HTTPS → http")
    func plainHTTPS() {
        #expect(classifier.classify("https://example.com/file.zip") == .http)
        #expect(classifier.classify("https://cdn.example.com/video.mp4") == .http)
    }
}
