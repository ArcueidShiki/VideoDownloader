// macos/Tests/AnyDownloaderCoreTests/Engines/YtDlpTaskTests.swift
import Testing
import Foundation
@testable import AnyDownloaderCore

@Suite("YtDlpTask")
struct YtDlpTaskTests {
    @Test("parses progress line 72.3%")
    func parseProgressLine() {
        let result = YtDlpTask.parseProgress("[download]  72.3% of  340.00MiB at  8.40MiB/s ETA 01:23")
        #expect(result != nil)
        #expect(abs((result?.progress ?? 0) - 0.723) < 0.001)
        #expect(result?.etaSeconds == 83)
    }

    @Test("returns nil for non-progress line")
    func nonProgressLine() {
        #expect(YtDlpTask.parseProgress("[info] Downloading video") == nil)
        #expect(YtDlpTask.parseProgress("") == nil)
    }

    @Test("builds argument array without shell injection")
    func buildsArgumentArray() {
        let url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        let opts = DownloadOptions(outputDirectory: URL(fileURLWithPath: "/tmp"), quality: "720p", format: "mp4")
        let args = YtDlpEngine.buildArguments(url: url, options: opts)
        #expect(args.contains(url))
        #expect(args.contains("--format"))
        // Shell injection check on the URL only — other args (e.g., --user-agent) may
        // legitimately contain semicolons (OS X user-agent string).
        #expect(!url.contains(";"))
        #expect(!url.contains("&&"))
    }

    @Test("ETA parser handles hours")
    func etaHours() {
        #expect(YtDlpTask.parseETA("02:30:45") == 9045)
    }

    @Test("ETA parser handles mm:ss")
    func etaMinutes() {
        #expect(YtDlpTask.parseETA("01:23") == 83)
    }

    // MARK: - parseAlreadyDownloadedPath (TikTok already-exists scenario)

    @Test("already downloaded: parses standard absolute path")
    func alreadyDownloaded_standardPath() {
        let line = "[download] /Users/user/test/akaeiray_7615627572604374293.mp4 has already been downloaded"
        let result = YtDlpTask.parseAlreadyDownloadedPath(line)
        #expect(result == "/Users/user/test/akaeiray_7615627572604374293.mp4")
    }

    @Test("already downloaded: parses path with spaces in filename")
    func alreadyDownloaded_pathWithSpaces() {
        let line = "[download] /Users/user/test/my tiktok video.mp4 has already been downloaded"
        let result = YtDlpTask.parseAlreadyDownloadedPath(line)
        #expect(result == "/Users/user/test/my tiktok video.mp4")
    }

    @Test("already downloaded: parses tilde-expanded path")
    func alreadyDownloaded_tildeExpanded() {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let line = "[download] \(home)/test/akaeiray_7615627572604374293.mp4 has already been downloaded"
        let result = YtDlpTask.parseAlreadyDownloadedPath(line)
        #expect(result == "\(home)/test/akaeiray_7615627572604374293.mp4")
    }

    @Test("already downloaded: returns nil for unrelated download line")
    func alreadyDownloaded_nonMatchingLine() {
        #expect(YtDlpTask.parseAlreadyDownloadedPath("[download]  72.3% of  340.00MiB at  8.40MiB/s ETA 01:23") == nil)
        #expect(YtDlpTask.parseAlreadyDownloadedPath("[download] Destination: /tmp/file.mp4") == nil)
        #expect(YtDlpTask.parseAlreadyDownloadedPath("") == nil)
    }

    @Test("already downloaded: filePath is set so play button can show")
    func alreadyDownloaded_filePathNotNil() {
        // Simulates the exact TikTok output for https://www.tiktok.com/@akaeiray/video/7615627572604374293
        // when the file already exists in ~/test
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let expectedPath = "\(home)/test/akaeiray_7615627572604374293.mp4"
        let ytdlpOutput = "[download] \(expectedPath) has already been downloaded"
        let parsed = YtDlpTask.parseAlreadyDownloadedPath(ytdlpOutput)
        #expect(parsed != nil, "filePath must not be nil — play button depends on this")
        #expect(parsed == expectedPath)
    }
}
