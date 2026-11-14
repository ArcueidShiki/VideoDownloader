import Testing
import Foundation
@testable import AnyDownloaderCore

/// Records which URLs the updater downloads; lets each test fake GitHub.
private final class FakeGitHub: @unchecked Sendable {
    let lock = NSLock()
    var requested: [String] = []
    let tag: String
    let zip: URL
    var sums: String

    init(tag: String, zip: URL, sums: String) { self.tag = tag; self.zip = zip; self.sums = sums }

    var source: YtDlpUpdater.Source {
        YtDlpUpdater.Source(
            latestTag: { self.tag },
            download: { url in
                self.lock.withLock { self.requested.append(url.lastPathComponent) }
                let copy = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
                if url.lastPathComponent == YtDlpUpdater.archiveName {
                    try FileManager.default.copyItem(at: self.zip, to: copy)
                } else {
                    try Data(self.sums.utf8).write(to: copy)
                }
                return copy
            }
        )
    }
}

/// Builds a zip shaped like yt-dlp's onedir release: `yt-dlp_macos` + `_internal/` at the root.
private func makeFakeRelease(printing version: String, in dir: URL) throws -> URL {
    let pkg = dir.appendingPathComponent("pkg-\(version)")
    try FileManager.default.createDirectory(at: pkg.appendingPathComponent("_internal"), withIntermediateDirectories: true)
    let exe = pkg.appendingPathComponent(YtDlpUpdater.executableName)
    try Data("#!/bin/sh\necho \(version)\n".utf8).write(to: exe)
    try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: exe.path)
    let zip = dir.appendingPathComponent("release-\(version).zip")
    let ditto = Process()
    ditto.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
    ditto.arguments = ["-c", "-k", pkg.path, zip.path]
    try ditto.run(); ditto.waitUntilExit()
    return zip
}

private func sumsLine(for zip: URL) throws -> String {
    "\(try YtDlpUpdater.sha256(of: zip))  \(YtDlpUpdater.archiveName)\nabc  yt-dlp.exe\n"
}

private func tempDir() throws -> URL {
    let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ytdlp-updater-\(UUID().uuidString)")
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    return dir
}

@Suite("YtDlpUpdater")
struct YtDlpUpdaterTests {
    @Test("tag validation accepts release tags and rejects path tricks")
    func tagValidation() {
        #expect(YtDlpUpdater.isValidTag("2026.10.26"))
        #expect(YtDlpUpdater.isValidTag("2026.10.26.1"))
        #expect(!YtDlpUpdater.isValidTag("../2026.10.26"))
        #expect(!YtDlpUpdater.isValidTag("2026.10.26/../../x"))
        #expect(!YtDlpUpdater.isValidTag(""))
    }

    @Test("checksum lookup matches the exact file name")
    func checksumLookup() {
        let text = "aaa  yt-dlp_macos\n\(String(repeating: "b", count: 64))  yt-dlp_macos.zip\n"
        #expect(YtDlpUpdater.checksum(in: text, for: "yt-dlp_macos.zip") == String(repeating: "b", count: 64))
        #expect(YtDlpUpdater.checksum(in: text, for: "yt-dlp_linux") == nil)
    }

    @Test("installs a verified release and exposes the resolved executable")
    func installsVerifiedRelease() async throws {
        let dir = try tempDir(); defer { try? FileManager.default.removeItem(at: dir) }
        let zip = try makeFakeRelease(printing: "2026.10.26", in: dir)
        let github = FakeGitHub(tag: "2026.10.26", zip: zip, sums: try sumsLine(for: zip))
        let updater = YtDlpUpdater(root: dir.appendingPathComponent("managed"), source: github.source)

        #expect(try await updater.update() == .installed("2026.10.26"))
        #expect(updater.installedVersion == "2026.10.26")
        let exe = try #require(updater.currentExecutable)
        // Bound to the versioned directory, not the swappable `current` link.
        #expect(exe.hasSuffix("/2026.10.26/\(YtDlpUpdater.executableName)"))
        #expect(!exe.contains("/current/"))
    }

    @Test("an up-to-date install downloads nothing")
    func upToDateSkipsDownload() async throws {
        let dir = try tempDir(); defer { try? FileManager.default.removeItem(at: dir) }
        let zip = try makeFakeRelease(printing: "2026.10.26", in: dir)
        let github = FakeGitHub(tag: "2026.10.26", zip: zip, sums: try sumsLine(for: zip))
        let updater = YtDlpUpdater(root: dir.appendingPathComponent("managed"), source: github.source)
        _ = try await updater.update()
        github.lock.withLock { github.requested.removeAll() }

        #expect(try await updater.update() == .upToDate("2026.10.26"))
        #expect(github.lock.withLock { github.requested.isEmpty })
    }

    @Test("checksum mismatch is rejected and leaves no install")
    func checksumMismatch() async throws {
        let dir = try tempDir(); defer { try? FileManager.default.removeItem(at: dir) }
        let zip = try makeFakeRelease(printing: "2026.10.26", in: dir)
        let bad = String(repeating: "0", count: 64) + "  \(YtDlpUpdater.archiveName)\n"
        let github = FakeGitHub(tag: "2026.10.26", zip: zip, sums: bad)
        let updater = YtDlpUpdater(root: dir.appendingPathComponent("managed"), source: github.source)

        await #expect(throws: YtDlpUpdater.UpdateError.checksumMismatch) { try await updater.update() }
        #expect(updater.currentExecutable == nil)
    }

    @Test("a release whose binary reports the wrong version keeps the previous install")
    func validationFailureKeepsPrevious() async throws {
        let dir = try tempDir(); defer { try? FileManager.default.removeItem(at: dir) }
        let root = dir.appendingPathComponent("managed")
        let good = try makeFakeRelease(printing: "2026.10.26", in: dir)
        _ = try await YtDlpUpdater(root: root, source: FakeGitHub(tag: "2026.10.26", zip: good, sums: try sumsLine(for: good)).source).update()

        let broken = try makeFakeRelease(printing: "garbage", in: dir)
        let next = YtDlpUpdater(root: root, source: FakeGitHub(tag: "2026.11.30", zip: broken, sums: try sumsLine(for: broken)).source)
        await #expect(throws: YtDlpUpdater.UpdateError.self) { try await next.update() }
        #expect(next.installedVersion == "2026.10.26")
        #expect(!FileManager.default.fileExists(atPath: root.appendingPathComponent("2026.11.30").path))
    }

    @Test("updating swaps current and prunes versions older than the previous one")
    func updatePrunesOldVersions() async throws {
        let dir = try tempDir(); defer { try? FileManager.default.removeItem(at: dir) }
        let root = dir.appendingPathComponent("managed")
        for tag in ["2026.08.01", "2026.09.01", "2026.10.26"] {
            let zip = try makeFakeRelease(printing: tag, in: dir)
            _ = try await YtDlpUpdater(root: root, source: FakeGitHub(tag: tag, zip: zip, sums: try sumsLine(for: zip)).source).update()
        }
        let dirs = try FileManager.default.contentsOfDirectory(atPath: root.path).filter { $0.first?.isNumber == true }.sorted()
        // The previous version survives so a download started before the swap keeps its files.
        #expect(dirs == ["2026.09.01", "2026.10.26"])
        #expect(YtDlpUpdater(root: root, source: .github).installedVersion == "2026.10.26")
    }
}
