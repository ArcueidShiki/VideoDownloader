import Foundation

public struct YtDlpEngine: DownloadEngineProtocol {
    public init() {}

    public func createTask(
        for item: DownloadItem,
        options: DownloadOptions,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) -> any DownloadTaskProtocol {
        YtDlpTask(item: item, options: options, onUpdate: onUpdate)
    }

    /// Chunked downloads make yt-dlp send `Range: bytes=…` on every media request.
    /// Bilibili's overseas CDN (Tencent EdgeOne, e.g. upos-sz-mirrorcosov.bilivideo.com)
    /// rejects range-less GETs with HTTP 567 "Restricted Access". Servers that ignore
    /// Range simply reply 200, so this is safe for every site.
    public static let httpChunkSize = "10M"

    public static func buildArguments(url: String, options: DownloadOptions) -> [String] {
        var args: [String] = runtimeArguments() + [
            "--ignore-config",
            "--print", "after_move:__ANYDOWNLOADER_FILE__%(filepath)j",
            "--format", "\(options.quality)/best",
        ]
        // Only force a container when merging is desired.
        // "native" means let yt-dlp choose the container (or download separate streams).
        if options.format != "native" {
            args += ["--merge-output-format", options.format]
        }
        args += [
            "--output", options.customFilename.map {
                options.outputDirectory.appendingPathComponent($0).path
            } ?? options.outputDirectory.appendingPathComponent("%(title)s.%(ext)s").path,
            "--progress", "--newline",
            "--no-playlist",
            "--continue",                   // resume partial files instead of restarting
            "--concurrent-fragments", "1",  // disable child-process spawning; avoids
                                            // "task name port right" Mach errors on macOS
            "--retries", "10",              // retry failed requests (covers nw_connection drops)
            "--fragment-retries", "10",     // retry failed fragment fetches
            "--extractor-retries", "3",     // retry extractor on network errors
            "--socket-timeout", "30",       // don't hang indefinitely
            "--http-chunk-size", httpChunkSize, // always send Range (Bilibili CDN 567)
            "--user-agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        ]
        if options.downloadSubtitles {
            args += ["--write-auto-sub", "--sub-lang", "en"]
        }
        if options.speedLimitBytesPerSec > 0 {
            args += ["--limit-rate", "\(options.speedLimitBytesPerSec)"]
        }
        args += ["--", url]
        return args
    }
    public static func runtimeArguments() -> [String] {
        var args = [String]()
        if let deno = DependencyStatus.find("deno") {
            args += ["--js-runtimes", "deno:\(deno)"]
        }
        if let ffmpeg = DependencyStatus.find("ffmpeg") {
            args += ["--ffmpeg-location", URL(fileURLWithPath: ffmpeg).deletingLastPathComponent().path]
        }
        return args
    }

}
