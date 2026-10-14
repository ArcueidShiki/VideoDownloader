import Foundation
import Testing
@testable import AnyDownloaderCore

@Suite("Real subprocess regressions")
struct SubprocessRegressionTests {
    final class Updates: @unchecked Sendable {
        let lock = NSLock()
        var items: [DownloadItem] = []
        func add(_ item: DownloadItem) { lock.withLock { items.append(item) } }
        var last: DownloadItem? { lock.withLock { items.last } }
    }

    func fixture(_ script: String) throws -> (URL, String) {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let executable = root.appendingPathComponent("extractor")
        try ("#!/bin/sh\n" + script).write(to: executable, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: executable.path)
        return (root, executable.path)
    }

    @Test("captures split final filename without newline and verifies the real file")
    func finalFile() async throws {
        let (root, executable) = try fixture("printf media > '测试 clip.mp4'\nprintf '__ANYDOWNLOADER_'\nsleep 0.1\nprintf 'FILE__\"%s/测试 clip.mp4\"' \"$PWD\"\n")
        defer { try? FileManager.default.removeItem(at: root) }
        let updates = Updates()
        let task = YtDlpTask(item: DownloadItem(url: "https://example.com/clip", type: .ytdlp),
                             options: DownloadOptions(outputDirectory: root), executablePath: executable,
                             onUpdate: updates.add)
        try await task.start()
        #expect(updates.last?.state == .completed)
        #expect(try Data(contentsOf: URL(fileURLWithPath: updates.last!.filePath!)) == Data("media".utf8))
    }

    @Test("zero exit without an output file fails, then retry recovers")
    func failureAndRetry() async throws {
        let (root, executable) = try fixture("exit 0\n")
        defer { try? FileManager.default.removeItem(at: root) }
        let updates = Updates()
        let task = YtDlpTask(item: DownloadItem(url: "https://example.com/clip", type: .ytdlp),
                             options: DownloadOptions(outputDirectory: root), executablePath: executable,
                             onUpdate: updates.add)
        try await task.start()
        #expect(updates.last?.state == .failed)
        #expect(updates.last?.errorMessage?.contains("non-empty") == true)
        try "#!/bin/sh\nprintf media > clip.mp4\nprintf '__ANYDOWNLOADER_FILE__\"%s/clip.mp4\"' \"$PWD\"\n"
            .write(toFile: executable, atomically: false, encoding: .utf8)
        try await task.resume()
        #expect(updates.last?.state == .completed)
    }

    @Test("missing executable publishes failure rather than leaving active state")
    func missingExecutable() async throws {
        let updates = Updates()
        let task = YtDlpTask(item: DownloadItem(url: "https://example.com/clip", type: .ytdlp),
                             options: .default, executablePath: "/missing/yt-dlp", onUpdate: updates.add)
        try await task.start()
        #expect(updates.last?.state == .failed)
    }

    @Test("metadata drains large stderr concurrently and returns actionable failure")
    func stderrFlood() async throws {
        let (root, executable) = try fixture("/usr/bin/head -c 200000 /dev/zero >&2\nprintf 'Extractor failed: retry later' >&2\nexit 7\n")
        defer { try? FileManager.default.removeItem(at: root) }
        do {
            _ = try await URLInfoFetcher.fetch(url: "https://example.com/clip", ytDlpPath: executable)
            Issue.record("expected extraction failure")
        } catch {
            #expect(error.localizedDescription.contains("retry later"))
            #expect(error.localizedDescription.utf8.count < 66000)
        }
    }

    @Test("pause drains the process before a new attempt starts")
    func pauseDrain() async throws {
        let (root, executable) = try fixture("exec /bin/sleep 30\n")
        defer { try? FileManager.default.removeItem(at: root) }
        let updates = Updates()
        let task = YtDlpTask(item: DownloadItem(url: "https://example.com/clip", type: .ytdlp),
                             options: DownloadOptions(outputDirectory: root), executablePath: executable,
                             onUpdate: updates.add)
        let run = Task { try await task.start() }
        let deadline = Date().addingTimeInterval(5)
        while updates.last?.state != .active && Date() < deadline { try await Task.sleep(for: .milliseconds(10)) }
        try #require(updates.last?.state == .active)
        await task.pause()
        try await run.value
        #expect(updates.last?.state == .paused)
    }
    @Test("pause stops a signal-resistant descendant writer before retry")
    func descendantWriter() async throws {
        let (root, executable) = try fixture("""
        if [ -e first-attempt ]; then
          printf media > clip.mp4
          printf '__ANYDOWNLOADER_FILE__"%s/clip.mp4"' "$PWD"
          exit 0
        fi
        touch first-attempt
        (
          trap '' INT TERM
          touch child.ready
          while :; do printf x >> writer.part; sleep 0.05; done
        ) &
        echo $! > child.pid
        wait
        """)
        let updates = Updates()
        let task = YtDlpTask(item: DownloadItem(url: "https://example.com/clip", type: .ytdlp),
                             options: DownloadOptions(outputDirectory: root), executablePath: executable,
                             onUpdate: updates.add)
        let run = Task { try await task.start() }
        let deadline = Date().addingTimeInterval(5)
        while !FileManager.default.fileExists(atPath: root.appendingPathComponent("writer.part").path) && Date() < deadline {
            try await Task.sleep(for: .milliseconds(10))
        }
        let pid = try #require(Int32(try String(contentsOf: root.appendingPathComponent("child.pid"), encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines)))
        defer { kill(pid, SIGKILL); try? FileManager.default.removeItem(at: root) }
        await task.pause()
        try await run.value
        #expect(updates.last?.state == .paused)
        let size = try Data(contentsOf: root.appendingPathComponent("writer.part")).count
        try await Task.sleep(for: .milliseconds(300))
        #expect(try Data(contentsOf: root.appendingPathComponent("writer.part")).count == size)
        try await task.resume()
        #expect(updates.last?.state == .completed)
        #expect(try Data(contentsOf: root.appendingPathComponent("clip.mp4")) == Data("media".utf8))
    }

}
