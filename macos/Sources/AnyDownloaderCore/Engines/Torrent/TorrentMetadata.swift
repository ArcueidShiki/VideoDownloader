import Foundation

// MARK: - TorrentMetadata

public struct TorrentMetadata: Sendable {
    public let name: String
    public let files: [TorrentFileEntry]

    public enum MetadataError: Error, LocalizedError {
        case noInfoDict
        case fetchFailed(String)

        public var errorDescription: String? {
            switch self {
            case .noInfoDict: return "Invalid torrent: missing info dictionary"
            case .fetchFailed(let msg): return "Metadata fetch failed: \(msg)"
            }
        }
    }

    // MARK: Parse from .torrent file path

    public static func parse(torrentPath: String) throws -> TorrentMetadata {
        let data = try Data(contentsOf: URL(fileURLWithPath: torrentPath))
        return try parse(data: data)
    }

    // MARK: Parse from raw Data

    public static func parse(data: Data) throws -> TorrentMetadata {
        var parser = BencodeParser(data)
        let root = try parser.parse()
        guard let info = root["info"] else { throw MetadataError.noInfoDict }
        let name = info["name"]?.string ?? "Unknown"

        // Multi-file torrent: info.files is a list of {path: [...], length: N}
        if let files = info["files"]?.list {
            let entries = files.enumerated().map { idx, fileDict -> TorrentFileEntry in
                let pathComponents = fileDict["path"]?.list?.compactMap { $0.string } ?? []
                let filePath = pathComponents.joined(separator: "/")
                let length = fileDict["length"]?.integer ?? 0
                return TorrentFileEntry(index: idx + 1, path: filePath.isEmpty ? name : filePath, length: length)
            }
            return TorrentMetadata(name: name, files: entries)
        }

        // Single-file torrent: info.length is the file size
        let length = info["length"]?.integer ?? 0
        return TorrentMetadata(name: name, files: [TorrentFileEntry(index: 1, path: name, length: length)])
    }
}

// MARK: - MagnetMetadataFetcher

/// Fetches torrent file metadata from a magnet link by running aria2c
/// with `--bt-metadata-only --bt-save-metadata`, then parses the saved .torrent.
public struct MagnetMetadataFetcher {
    public static func fetch(
        magnetURL: String,
        aria2Path: String,
        timeout: TimeInterval = 45
    ) async throws -> TorrentMetadata {
        let tmpDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("anydownloader-meta-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tmpDir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: tmpDir) }

        let outPipe = Pipe()
        let errPipe = Pipe()
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: aria2Path)
        proc.arguments = [
            "--bt-metadata-only",
            "--bt-save-metadata",
            "--dir=\(tmpDir.path)",
            "--console-log-level=notice",
            "--enable-dht=true",
            "--enable-dht6=false",
            "--enable-peer-exchange=true",
            "--bt-enable-lpd=true",
            "--check-certificate=false",
            "--dht-entry-point=router.bittorrent.com:6881",
            "--dht-entry-point=router.utorrent.com:6881",
            "--dht-entry-point=dht.transmissionbt.com:6881",
            "--bt-tracker=udp://tracker.opentrackr.org:1337/announce",
            "--bt-tracker=udp://open.tracker.cl:1337/announce",
            "--bt-tracker=udp://tracker.openbittorrent.com:6969/announce",
            "--bt-tracker=udp://9.rarbg.com:2810/announce",
            "--bt-tracker=udp://tracker.torrent.eu.org:451/announce",
            "--bt-tracker-connect-timeout=20",
            "--bt-tracker-timeout=\(Int(timeout))",
            "--bt-max-peers=500",
            "--stop-with-process=\(ProcessInfo.processInfo.processIdentifier)",
            magnetURL,
        ]
        proc.standardOutput = outPipe
        proc.standardError  = errPipe

        try proc.run()

        // Poll for the .torrent file to appear, or until timeout
        let deadline = Date().addingTimeInterval(timeout + 5)
        while proc.isRunning, Date() < deadline {
            // Check early if the file appeared — no need to wait the full timeout
            let already = (try? FileManager.default.contentsOfDirectory(
                at: tmpDir, includingPropertiesForKeys: nil))?.first { $0.pathExtension == "torrent" }
            if already != nil { break }
            try await Task.sleep(nanoseconds: 500_000_000)
        }
        if proc.isRunning { proc.terminate() }

        // Find the .torrent file aria2c saved
        let contents = (try? FileManager.default.contentsOfDirectory(
            at: tmpDir,
            includingPropertiesForKeys: nil
        )) ?? []
        guard let torrentFile = contents.first(where: { $0.pathExtension == "torrent" }) else {
            let stderr = String(data: errPipe.fileHandleForReading.readDataToEndOfFile(),
                                encoding: .utf8)?
                .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            let hint = stderr.isEmpty
                ? "Peer discovery timed out. Check your internet connection or use a .torrent file instead."
                : stderr
            throw TorrentMetadata.MetadataError.fetchFailed(hint)
        }

        return try TorrentMetadata.parse(torrentPath: torrentFile.path)
    }
}
