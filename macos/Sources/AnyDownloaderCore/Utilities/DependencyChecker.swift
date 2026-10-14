import Foundation

public struct DependencyStatus: Sendable {
    public let aria2cPath: String?
    public let ytDlpPath: String?
    public let denoPath: String?
    public let ffmpegPath: String?
    public let ffprobePath: String?

    public var aria2cMissing: Bool { aria2cPath == nil }
    public var ytDlpMissing: Bool  { ytDlpPath == nil }
    public var allPresent: Bool    { !aria2cMissing && !ytDlpMissing && denoPath != nil && ffmpegPath != nil && ffprobePath != nil }

    public static func check() -> DependencyStatus {
        DependencyStatus(
            aria2cPath: Self.find("aria2c"),
            ytDlpPath: Self.find("yt-dlp"),
            denoPath: Self.find("deno"),
            ffmpegPath: Self.find("ffmpeg"),
            ffprobePath: Self.find("ffprobe")
        )
    }

    /// Existence is insufficient: Homebrew executables can have missing dylibs.
    public static func healthIssues() async -> [String] {
        var issues: [String] = []
        for (name, argument) in [("yt-dlp", "--version"), ("deno", "--version"),
                                 ("ffmpeg", "-version"), ("ffprobe", "-version"), ("aria2c", "--version")] {
            guard let path = find(name) else { issues.append("\(name) is missing."); continue }
            let process = Process(), stdout = Pipe(), stderr = Pipe()
            process.executableURL = URL(fileURLWithPath: path)
            process.arguments = [argument]
            process.standardOutput = stdout
            process.standardError = stderr
            process.standardInput = FileHandle.nullDevice
            process.environment = YtDlpTask.enrichedEnvironment()
            do {
                let result = try await ProcessCapture.run(process, stdout: stdout, stderr: stderr, launch: {
                    try process.run()
                    DispatchQueue.global().asyncAfter(deadline: .now() + 5) {
                        if process.isRunning { process.terminate() }
                    }
                })
                if result.status != 0 {
                    issues.append("\(name) cannot start. Repair it with Homebrew (exit \(result.status)).")
                }
            } catch { issues.append("\(name): \(error.localizedDescription)") }
        }
        return issues
    }

    /// Read-only startup checks. Offline failures never disable working tools.
    /// Homebrew owns these executables; the application never replaces its files.
    public static func availableUpdates() async -> [String] {
        var updates: [String] = []
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 8
        configuration.timeoutIntervalForResource = 10
        let session = URLSession(configuration: configuration)
        defer { session.invalidateAndCancel() }
        for (name, repository) in [("yt-dlp", "yt-dlp/yt-dlp"), ("deno", "denoland/deno")] {
            guard let path = find(name) else { continue }
            let process = Process(), stdout = Pipe(), stderr = Pipe()
            process.executableURL = URL(fileURLWithPath: path)
            process.arguments = ["--version"]
            process.standardOutput = stdout; process.standardError = stderr
            process.standardInput = FileHandle.nullDevice
            do {
                let local = try await ProcessCapture.run(process, stdout: stdout, stderr: stderr, launch: {
                    try process.run()
                    DispatchQueue.global().asyncAfter(deadline: .now() + 5) {
                        if process.isRunning { process.terminate() }
                    }
                })
                guard local.status == 0 else { continue }
                let words = String(decoding: local.stdout, as: UTF8.self).split(whereSeparator: \.isWhitespace)
                guard let current = words.first(where: { $0.first?.isNumber == true }) else { continue }
                var request = URLRequest(url: URL(string: "https://api.github.com/repos/\(repository)/releases/latest")!)
                request.setValue("AnyDownloader-macOS", forHTTPHeaderField: "User-Agent")
                let (data, response) = try await session.data(for: request)
                guard (response as? HTTPURLResponse)?.statusCode == 200, data.count < 1_048_576,
                      let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                      let tag = json["tag_name"] as? String else { continue }
                let latest = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
                if latest.compare(String(current), options: .numeric) == .orderedDescending {
                    updates.append("\(name) \(latest) (installed \(current))")
                }
            } catch { continue }
        }
        return updates
    }

    public static func find(_ name: String) -> String? {
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
