import Foundation

// MARK: - FailFastURLProtocol

/// Intercepts all URLSession requests during tests and immediately fails them,
/// preventing real DNS resolution and network access that would slow down or
/// destabilise the test suite.
///
/// Register once per test suite that spawns background download tasks:
///   init() { FailFastURLProtocol.register() }
final class FailFastURLProtocol: URLProtocol {
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        client?.urlProtocol(self, didFailWithError: URLError(.cannotConnectToHost))
    }
    override func stopLoading() {}

    static func register() {
        URLProtocol.registerClass(FailFastURLProtocol.self)
    }
}
