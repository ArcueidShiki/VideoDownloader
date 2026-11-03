import Foundation
import GRDB

public final class PersistenceManager: Sendable {
    private let dbQueue: DatabaseQueue

    public init(path: String) throws {
        if path == ":memory:" {
            dbQueue = try DatabaseQueue()
        } else {
            dbQueue = try DatabaseQueue(path: path)
        }
        try migrate()
    }

    private func migrate() throws {
        var migrator = DatabaseMigrator()

        migrator.registerMigration("v1") { db in
            try db.create(table: "download_item", ifNotExists: true) { t in
                t.column("id", .text).primaryKey()
                t.column("url", .text).notNull()
                t.column("type", .text).notNull()
                t.column("state", .text).notNull()
                t.column("progress", .double).notNull().defaults(to: 0)
                t.column("speed_bytes_per_sec", .integer).notNull().defaults(to: 0)
                t.column("eta_seconds", .integer)
                t.column("file_path", .text)
                t.column("error_message", .text)
                t.column("total_bytes", .integer)
                t.column("received_bytes", .integer).notNull().defaults(to: 0)
                t.column("title", .text)
                t.column("created_at", .datetime).notNull()
            }
        }

        // v2: persist rich metadata so it survives app restarts
        migrator.registerMigration("v2_rich_metadata") { db in
            try db.alter(table: "download_item") { t in
                t.add(column: "torrent_files_json", .text)
                t.add(column: "video_info_json", .text)
                t.add(column: "selected_format_id", .text)
                t.add(column: "selected_container_format", .text)
                t.add(column: "item_output_directory", .text)
                t.add(column: "custom_filename", .text)
            }
        }

        try migrator.migrate(dbQueue)
    }

    public func save(_ item: DownloadItem) throws {
        let encoder = JSONEncoder()
        let torrentFilesJSON = item.torrentFiles
            .flatMap { try? encoder.encode($0) }
            .flatMap { String(data: $0, encoding: .utf8) }
        let videoInfoJSON = item.videoInfo
            .flatMap { try? encoder.encode($0) }
            .flatMap { String(data: $0, encoding: .utf8) }

        try dbQueue.write { db in
            try db.execute(sql: """
                INSERT OR REPLACE INTO download_item
                (id, url, type, state, progress, speed_bytes_per_sec, eta_seconds,
                 file_path, error_message, total_bytes, received_bytes, title, created_at,
                 torrent_files_json, video_info_json, selected_format_id,
                 selected_container_format, item_output_directory, custom_filename)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, arguments: [
                item.id.uuidString,
                item.url,
                item.type.rawValue,
                item.state.rawValue,
                item.progress,
                item.speedBytesPerSec,
                item.etaSeconds,
                item.filePath,
                item.errorMessage,
                item.totalBytes,
                item.receivedBytes,
                item.title,
                item.createdAt,
                torrentFilesJSON,
                videoInfoJSON,
                item.selectedFormatId,
                item.selectedContainerFormat,
                item.itemOutputDirectory?.path,
                item.customFilename
            ])
        }
    }

    public func delete(id: UUID) throws {
        try dbQueue.write { db in
            try db.execute(
                sql: "DELETE FROM download_item WHERE id = ?",
                arguments: [id.uuidString]
            )
        }
    }

    public func loadAll() throws -> [DownloadItem] {
        try dbQueue.read { db in
            try Row.fetchAll(db, sql: "SELECT * FROM download_item ORDER BY created_at ASC")
                .compactMap { Self.itemFromRow($0) }
        }
    }

    public func loadUnfinished() throws -> [DownloadItem] {
        try dbQueue.read { db in
            try Row.fetchAll(db, sql: """
                SELECT * FROM download_item
                WHERE state NOT IN ('completed','failed')
                ORDER BY created_at ASC
                """).compactMap { Self.itemFromRow($0) }
        }
    }

    private static func itemFromRow(_ row: Row) -> DownloadItem? {
        guard
            let idStr: String = row["id"],
            let id = UUID(uuidString: idStr),
            let url: String = row["url"],
            let typeStr: String = row["type"],
            let type_ = DownloadType(rawValue: typeStr),
            let stateStr: String = row["state"],
            let state = DownloadState(rawValue: stateStr),
            let createdAt: Date = row["created_at"]
        else { return nil }

        let base = DownloadItem(id: id, url: url, type: type_, createdAt: createdAt)

        let progress: Double? = row["progress"]
        let speed: Int64 = row["speed_bytes_per_sec"] ?? 0
        let eta: Int? = row["eta_seconds"]
        let filePath: String? = row["file_path"]
        let errorMessage: String? = row["error_message"]
        let totalBytes: Int64? = row["total_bytes"]
        let received: Int64 = row["received_bytes"] ?? 0
        let title: String? = row["title"]

        // Decode rich metadata persisted in v2
        let decoder = JSONDecoder()
        let torrentFilesJSON: String? = row["torrent_files_json"]
        let torrentFiles = torrentFilesJSON
            .flatMap { $0.data(using: .utf8) }
            .flatMap { try? decoder.decode([TorrentFileEntry].self, from: $0) }

        let videoInfoJSON: String? = row["video_info_json"]
        let videoInfo = videoInfoJSON
            .flatMap { $0.data(using: .utf8) }
            .flatMap { try? decoder.decode(VideoInfo.self, from: $0) }

        let selectedFormatId: String? = row["selected_format_id"]
        let selectedContainerFormat: String? = row["selected_container_format"]
        let outputDirPath: String? = row["item_output_directory"]
        let itemOutputDirectory = outputDirPath.map { URL(fileURLWithPath: $0) }
        let customFilename: String? = row["custom_filename"]

        return base.with(
            state: state,
            progress: progress,
            speedBytesPerSec: speed,
            etaSeconds: eta,
            filePath: filePath,
            errorMessage: errorMessage,
            totalBytes: totalBytes,
            receivedBytes: received,
            title: title,
            torrentFiles: torrentFiles,
            videoInfo: videoInfo,
            selectedFormatId: selectedFormatId,
            selectedContainerFormat: selectedContainerFormat,
            itemOutputDirectory: itemOutputDirectory,
            customFilename: customFilename
        )
    }

    public static var defaultPath: String {
        if let path = ProcessInfo.processInfo.environment["ANYDOWNLOADER_STATE_DIRECTORY"], !path.isEmpty {
            let dir = URL(fileURLWithPath: path, isDirectory: true)
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            return dir.appendingPathComponent("downloads.sqlite").path
        }
        let appSupport = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first!
        let dir = appSupport.appendingPathComponent("AnyDownloader")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("downloads.sqlite").path
    }
}
