import Foundation
import Testing
@testable import AnyDownloaderCore

/// Opt-in real network validation. Sintel © Blender Foundation, CC BY 3.0.
/// License: https://durian.blender.org/sharing/ — the Blender Foundation hosts the trailer fixture.
@Suite("Live licensed media", .enabled(if: ProcessInfo.processInfo.environment["ANYDOWNLOADER_LIVE_TESTS"] == "1"))
struct LiveMediaTests {
    @Test("GUI store downloads and persists real HTTP media")
    @MainActor
    func liveStoreDownload() async throws {
        let root = URL(fileURLWithPath: ProcessInfo.processInfo.environment["ANYDOWNLOADER_LIVE_OUTPUT"]
                       ?? FileManager.default.temporaryDirectory.appendingPathComponent("AnyDownloader-store-\(UUID())").path)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let database = root.appendingPathComponent("gui-downloads.sqlite").path
        let store = try DownloadStore(persistencePath: database)
        let url = "https://download.blender.org/durian/trailer/sintel_trailer-480p.mp4"
        try await store.add(url: url, options: DownloadOptions(outputDirectory: root))
        let item = try #require(store.items.last)
        await store.startAllQueued(outputDirectory: root, ids: [item.id])
        let deadline = Date().addingTimeInterval(90)
        while Date() < deadline {
            let state = store.items.first(where: { $0.id == item.id })?.state
            if state == .completed || state == .failed { break }
            try await Task.sleep(for: .milliseconds(100))
        }
        let result = try #require(store.items.first(where: { $0.id == item.id }))
        #expect(result.state == .completed, "\(result.errorMessage ?? "no error")")
        let path = try #require(result.filePath)
        #expect((try FileManager.default.attributesOfItem(atPath: path)[.size] as? NSNumber)?.intValue == 4_372_373)
        let restored = try DownloadStore(persistencePath: database)
        #expect(restored.items.first(where: { $0.id == item.id })?.state == .completed)
        print("LIVE_GUI_DATABASE=\(database)")
    }

    @Test("real metadata, pause/resume, completed file, repeat download and ffprobe")
    func liveDownload() async throws {
        let issues = await DependencyStatus.healthIssues()
        #expect(issues.isEmpty, "\(issues)")
        let url = "https://download.blender.org/durian/trailer/sintel_trailer-480p.mp4"
        let info = try await URLInfoFetcher.fetch(url: url, ytDlpPath: YtDlpTask.findYtDlp())
        #expect(!info.title.isEmpty)
        let root = URL(fileURLWithPath: ProcessInfo.processInfo.environment["ANYDOWNLOADER_LIVE_OUTPUT"]
                       ?? FileManager.default.temporaryDirectory.appendingPathComponent("AnyDownloader-live-\(UUID())").path)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let updates = SubprocessRegressionTests.Updates()
        let options = DownloadOptions(outputDirectory: root, speedLimitBytesPerSec: 262144,
                                      customFilename: "Sintel-CC-BY-3.0.mp4")
        let task = YtDlpTask(item: DownloadItem(url: url, type: .ytdlp), options: options, onUpdate: updates.add)
        let first = Task { try await task.start() }
        try await Task.sleep(for: .seconds(3))
        await task.pause()
        try await first.value
        #expect(updates.last?.state == .paused)
        try await task.resume()
        #expect(updates.last?.state == .completed, "\(updates.last?.errorMessage ?? "no error")")
        let path = try #require(updates.last?.filePath)
        let attributes = try FileManager.default.attributesOfItem(atPath: path)
        #expect((attributes[.size] as? NSNumber)?.int64Value == 4_372_373)
        try await task.resume()
        #expect(updates.last?.state == .completed)
        let process = Process(), stdout = Pipe(), stderr = Pipe()
        process.executableURL = URL(fileURLWithPath: try #require(DependencyStatus.find("ffprobe")))
        process.arguments = ["-v", "error", "-show_entries", "format=duration", "-of", "json", path]
        process.standardOutput = stdout; process.standardError = stderr
        let result = try await ProcessCapture.run(process, stdout: stdout, stderr: stderr)
        #expect(result.status == 0)
        let json = try JSONSerialization.jsonObject(with: result.stdout) as? [String: Any]
        let format = json?["format"] as? [String: Any]
        #expect(Double(format?["duration"] as? String ?? "0") ?? 0 > 50)
        print("LIVE_MEDIA_PATH=\(path)")
        print("LIVE_FFPROBE=\(String(decoding: result.stdout, as: UTF8.self))")
    }
}
