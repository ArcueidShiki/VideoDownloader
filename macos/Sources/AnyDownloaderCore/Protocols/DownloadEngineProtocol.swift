import Foundation

public protocol DownloadEngineProtocol: Sendable {
    func createTask(
        for item: DownloadItem,
        options: DownloadOptions,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) -> any DownloadTaskProtocol
}
