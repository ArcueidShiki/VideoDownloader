import Foundation
import Testing
@testable import AnyDownloaderCore

private final class HTTPFixture: URLProtocol, @unchecked Sendable {
    final class Scenario: @unchecked Sendable {
        let lock = NSLock()
        var heads = 0
        var gets = 0
        let failure: String?
        init(failure: String? = nil) { self.failure = failure }
    }
    private static let registryLock = NSLock()
    nonisolated(unsafe) private static var scenarios: [String: Scenario] = [:]
    private let lock = NSLock()
    private var stopped = false
    static func install(_ scenario: Scenario, host: String) {
        registryLock.withLock { scenarios[host] = scenario }
    }
    static func remove(host: String) { _ = registryLock.withLock { scenarios.removeValue(forKey: host) } }
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        guard let scenario = Self.registryLock.withLock({ Self.scenarios[request.url!.host!] }) else { return }
        let head = request.httpMethod == "HEAD"
        let number = scenario.lock.withLock {
            if head { scenario.heads += 1; return scenario.heads }
            scenario.gets += 1; return scenario.gets
        }
        let delay: TimeInterval = head && number == 1 ? 0.3 : 0
        DispatchQueue.global().asyncAfter(deadline: .now() + delay) { [self] in
            lock.withLock {
                guard !stopped else { return }
                let status = !head && scenario.failure == "404" ? 404 : 200
                var headers = ["content-length": "4"]
                if scenario.failure == "range" { headers["accept-ranges"] = "bytes" }
                let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!
                client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
                if !head { client?.urlProtocol(self, didLoad: Data((scenario.failure == "truncated" ? "ab" : "abcd").utf8)) }
                client?.urlProtocolDidFinishLoading(self)
            }
        }
    }
    override func stopLoading() { lock.withLock { stopped = true } }
}

@Suite("HTTP recovery")
struct HTTPRecoveryTests {
    @Test("pause during HEAD drains the old run before resume")
    func pauseDuringProbe() async throws {
        let host = "fixture-\(UUID().uuidString.lowercased()).test"
        let scenario = HTTPFixture.Scenario()
        HTTPFixture.install(scenario, host: host)
        defer { HTTPFixture.remove(host: host) }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [HTTPFixture.self]
        let updates = SubprocessRegressionTests.Updates()
        let task = HTTPDownloadTask(item: DownloadItem(url: "https://\(host)/media.mp4", type: .http),
                                    options: DownloadOptions(outputDirectory: root), sessionConfiguration: config, onUpdate: updates.add)
        let first = Task { try await task.start() }
        let deadline = Date().addingTimeInterval(2)
        while scenario.lock.withLock({ scenario.heads == 0 }) && Date() < deadline {
            try await Task.sleep(for: .milliseconds(5))
        }
        #expect(scenario.lock.withLock { scenario.heads } == 1)
        await task.pause()
        try await first.value
        try await Task.sleep(for: .milliseconds(350))
        #expect(updates.last?.state == .paused)
        #expect(scenario.lock.withLock { scenario.gets } == 0)
        try await task.resume()
        #expect(updates.last?.state == .completed)
        #expect(try Data(contentsOf: root.appendingPathComponent("media.mp4")) == Data("abcd".utf8))
    }

    @Test("HTTP errors, truncated bodies and ignored Range never complete", arguments: ["404", "truncated", "range"])
    func invalidResponse(failure: String) async throws {
        let host = "fixture-\(UUID().uuidString.lowercased()).test"
        HTTPFixture.install(HTTPFixture.Scenario(failure: failure), host: host)
        defer { HTTPFixture.remove(host: host) }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [HTTPFixture.self]
        let updates = SubprocessRegressionTests.Updates()
        let task = HTTPDownloadTask(item: DownloadItem(url: "https://\(host)/media.mp4", type: .http),
                                    options: DownloadOptions(outputDirectory: root), sessionConfiguration: config, onUpdate: updates.add)
        await #expect(throws: (any Error).self) { try await task.start() }
        #expect(updates.last?.state != .completed)
    }
}
