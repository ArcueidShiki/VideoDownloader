import Testing
@testable import AnyDownloaderCore

@Suite("InputValidator — strict input validation")
struct InputValidatorTests {
    let validator = InputValidator()

    // MARK: - Valid inputs

    @Test("accepts HTTPS URL")
    func acceptsHTTPS() throws {
        #expect(throws: Never.self) { try validator.validate("https://example.com/file.zip") }
    }

    @Test("accepts HTTP URL")
    func acceptsHTTP() throws {
        #expect(throws: Never.self) { try validator.validate("http://example.com/file.mp4") }
    }

    @Test("accepts well-formed magnet link")
    func acceptsMagnet() throws {
        // 40-char hex info-hash (SHA-1 length)
        let hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        #expect(throws: Never.self) {
            try validator.validate("magnet:?xt=urn:btih:\(hash)&dn=test&tr=udp://tracker.example.com:6969")
        }
    }

    @Test("returns trimmed string")
    func returnsTrimmed() throws {
        let result = try validator.validate("  https://example.com/file.zip  ")
        #expect(result == "https://example.com/file.zip")
    }

    // MARK: - Empty / whitespace

    @Test("rejects empty string")
    func rejectsEmpty() {
        #expect(throws: ValidationError.emptyInput) { try validator.validate("") }
    }

    @Test("rejects whitespace-only input")
    func rejectsWhitespace() {
        #expect(throws: ValidationError.emptyInput) { try validator.validate("   ") }
    }

    // MARK: - Length limits

    @Test("rejects URL exceeding max length")
    func rejectsTooLong() {
        let longURL = "https://example.com/" + String(repeating: "a", count: InputValidator.maxInputLength)
        #expect(throws: ValidationError.self) { try validator.validate(longURL) }
    }

    @Test("accepts URL exactly at max length boundary")
    func acceptsAtMaxLength() throws {
        let base = "https://example.com/"
        let padding = String(repeating: "a", count: InputValidator.maxInputLength - base.count)
        #expect(throws: Never.self) { try validator.validate(base + padding) }
    }

    // MARK: - Null byte injection

    @Test("rejects URL containing null byte")
    func rejectsNullByte() {
        #expect(throws: ValidationError.nullBytesDetected) {
            try validator.validate("https://example.com/\0file.zip")
        }
    }

    @Test("rejects null byte at start")
    func rejectsNullByteAtStart() {
        #expect(throws: ValidationError.nullBytesDetected) {
            try validator.validate("\0https://example.com/")
        }
    }

    // MARK: - Shell injection

    @Test("rejects backtick (shell substitution)")
    func rejectsBacktick() {
        #expect(throws: ValidationError.shellInjectionCharacters) {
            try validator.validate("https://example.com/`whoami`")
        }
    }

    @Test("rejects dollar sign with braces (shell expansion)")
    func rejectsDollarBrace() {
        #expect(throws: ValidationError.shellInjectionCharacters) {
            try validator.validate("https://example.com/${HOME}")
        }
    }

    @Test("rejects semicolon (command separator)")
    func rejectsSemicolon() {
        #expect(throws: ValidationError.shellInjectionCharacters) {
            try validator.validate("https://example.com/file;rm -rf /")
        }
    }

    @Test("rejects pipe character")
    func rejectsPipe() {
        #expect(throws: ValidationError.shellInjectionCharacters) {
            try validator.validate("https://example.com/file|cat /etc/passwd")
        }
    }

    @Test("rejects curly braces")
    func rejectsCurlyBraces() {
        #expect(throws: ValidationError.shellInjectionCharacters) {
            try validator.validate("https://example.com/{evil}")
        }
    }

    // MARK: - Dangerous schemes

    @Test("rejects javascript: scheme (XSS vector)")
    func rejectsJavascript() {
        #expect(throws: ValidationError.invalidScheme("javascript")) {
            try validator.validate("javascript:alert(document.cookie)")
        }
    }

    @Test("rejects file:// scheme (local file read)")
    func rejectsFileScheme() {
        #expect(throws: ValidationError.invalidScheme("file")) {
            try validator.validate("file:///etc/passwd")
        }
    }

    @Test("rejects data: scheme")
    func rejectsDataScheme() {
        #expect(throws: ValidationError.invalidScheme("data")) {
            try validator.validate("data:text/html,<script>alert(1)</script>")
        }
    }

    @Test("rejects ftp: scheme")
    func rejectsFTP() {
        #expect(throws: ValidationError.invalidScheme("ftp")) {
            try validator.validate("ftp://files.example.com/file.zip")
        }
    }

    // MARK: - SSRF — private / internal network

    @Test("rejects loopback 127.x.x.x")
    func rejectsLoopback() {
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://127.0.0.1/secret")
        }
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://127.1.2.3/secret")
        }
    }

    @Test("rejects RFC-1918 192.168.x.x")
    func rejectsClassC() {
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://192.168.1.1/secret")
        }
    }

    @Test("rejects RFC-1918 10.x.x.x")
    func rejectsClassA() {
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://10.0.0.1/secret")
        }
    }

    @Test("rejects RFC-1918 172.16–31.x.x")
    func rejectsClassB() {
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://172.16.0.1/secret")
        }
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://172.31.255.255/secret")
        }
    }

    @Test("rejects localhost hostname")
    func rejectsLocalhost() {
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://localhost/secret")
        }
    }

    @Test("rejects link-local 169.254.x.x (AWS metadata endpoint)")
    func rejectsLinkLocal() {
        #expect(throws: ValidationError.privateIPAddress) {
            try validator.validate("https://169.254.169.254/latest/meta-data/")
        }
    }

    // MARK: - Malformed URLs

    @Test("rejects completely malformed input")
    func rejectsMalformed() {
        #expect(throws: ValidationError.malformedURL) {
            try validator.validate("not a url at all %%%")
        }
    }

    @Test("rejects bare domain without scheme")
    func rejectsBaredomainNoScheme() {
        #expect(throws: ValidationError.malformedURL) {
            try validator.validate("example.com/file.zip")
        }
    }

    // MARK: - Magnet URI edge cases

    @Test("rejects magnet link missing xt=urn:btih:")
    func rejectsMagnetMissingHash() {
        #expect(throws: ValidationError.invalidMagnetURI) {
            try validator.validate("magnet:?dn=somefilm")
        }
    }

    @Test("rejects magnet link with wrong URN type")
    func rejectsMagnetWrongUrn() {
        #expect(throws: ValidationError.invalidMagnetURI) {
            try validator.validate("magnet:?xt=urn:sha1:abc123")
        }
    }

    @Test("rejects magnet link with hash too short")
    func rejectsMagnetHashTooShort() {
        #expect(throws: ValidationError.invalidMagnetURI) {
            try validator.validate("magnet:?xt=urn:btih:abc")
        }
    }

    @Test("accepts magnet with full 40-char SHA-1 hash")
    func acceptsMagnetFullHash() throws {
        #expect(throws: Never.self) {
            try validator.validate("magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&dn=test")
        }
    }
}
