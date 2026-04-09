import Foundation

public protocol DownloadTaskProtocol: AnyObject, Sendable {
    var itemID: UUID { get }
    func start() async throws
    func pause() async
    func resume() async throws
    func cancel() async
}
