import Foundation

/// Fetches video/audio metadata from a URL without downloading the media.
/// For yt-dlp-supported sites: runs `yt-dlp --dump-json --no-download`.
/// For HTTP URLs: uses a HEAD request to get filename + content-length.
public struct URLInfoFetcher {

    // MARK: - Entry point

    public static func fetch(
        url: String,
        ytDlpPath: String
    ) async throws -> VideoInfo {
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: ytDlpPath)
        proc.arguments = YtDlpEngine.runtimeArguments() + [
            "--ignore-config",
            "--dump-json",
            "--no-playlist",
            "--no-warnings",
            "--socket-timeout", "30",
            "--retries", "3",
            "--extractor-retries", "3",
            "--user-agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "--", url,
        ]

        let outPipe = Pipe()
        let errPipe = Pipe()
        proc.standardOutput = outPipe
        proc.standardError  = errPipe
        // Prevent yt-dlp from blocking on stdin (e.g., age-gating prompts, login prompts)
        proc.standardInput  = FileHandle.nullDevice
        // Ensure ffmpeg is on PATH (needed for some yt-dlp metadata operations)
        proc.environment = YtDlpTask.enrichedEnvironment()

        let output = try await ProcessCapture.run(proc, stdout: outPipe, stderr: errPipe)
        guard output.status == 0 else {
            throw FetchError.ytDlpFailed(String(decoding: output.stderr, as: UTF8.self))
        }
        let data = output.stdout

        guard !data.isEmpty else { throw FetchError.invalidJSON }

        // yt-dlp may emit multiple newline-delimited JSON objects (e.g., playlist
        // entries even with --no-playlist for some sites). Take the first valid object.
        let text = String(data: data, encoding: .utf8) ?? ""
        let parseData: Data
        if let firstLine = text.components(separatedBy: "\n").first(where: { $0.hasPrefix("{") }),
           let lineData = firstLine.data(using: .utf8) {
            parseData = lineData
        } else {
            parseData = data
        }

        guard let json = try? JSONSerialization.jsonObject(with: parseData) as? [String: Any] else {
            throw FetchError.invalidJSON
        }

        return try VideoInfo.parse(from: json)
    }

    // MARK: - Errors

    public enum FetchError: Error, LocalizedError {
        case ytDlpFailed(String)
        case invalidJSON

        public var errorDescription: String? {
            switch self {
            case .ytDlpFailed(let msg): return "yt-dlp error: \(msg)"
            case .invalidJSON: return "Could not parse video metadata"
            }
        }
    }
}
