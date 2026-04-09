import Foundation

// MARK: - Bencode value type

indirect enum Bencode: Sendable {
    case integer(Int64)
    case bytes(Data)
    case list([Bencode])
    case dict([(key: String, value: Bencode)])
}

extension Bencode {
    subscript(key: String) -> Bencode? {
        guard case .dict(let pairs) = self else { return nil }
        return pairs.first { $0.key == key }?.value
    }

    var string: String? {
        guard case .bytes(let d) = self else { return nil }
        return String(data: d, encoding: .utf8) ?? String(data: d, encoding: .isoLatin1)
    }

    var integer: Int64? {
        guard case .integer(let n) = self else { return nil }
        return n
    }

    var list: [Bencode]? {
        guard case .list(let items) = self else { return nil }
        return items
    }
}

// MARK: - Parser

struct BencodeParser {
    enum ParseError: Error, LocalizedError {
        case unexpectedEnd
        case invalidFormat(offset: Int)

        var errorDescription: String? {
            switch self {
            case .unexpectedEnd: return "Unexpected end of data"
            case .invalidFormat(let o): return "Invalid bencode at offset \(o)"
            }
        }
    }

    private let data: Data
    private var pos: Int = 0

    init(_ data: Data) { self.data = data }

    mutating func parse() throws -> Bencode {
        guard pos < data.count else { throw ParseError.unexpectedEnd }
        switch data[pos] {
        case UInt8(ascii: "i"): return try parseInteger()
        case UInt8(ascii: "l"): return try parseList()
        case UInt8(ascii: "d"): return try parseDict()
        case UInt8(ascii: "0")...UInt8(ascii: "9"): return try parseByteString()
        default: throw ParseError.invalidFormat(offset: pos)
        }
    }

    // "i<digits>e"
    private mutating func parseInteger() throws -> Bencode {
        pos += 1 // skip 'i'
        let start = pos
        while pos < data.count, data[pos] != UInt8(ascii: "e") { pos += 1 }
        guard pos < data.count else { throw ParseError.unexpectedEnd }
        let numStr = String(data: data[start..<pos], encoding: .ascii) ?? ""
        guard let n = Int64(numStr) else { throw ParseError.invalidFormat(offset: start) }
        pos += 1 // skip 'e'
        return .integer(n)
    }

    // "<length>:<bytes>"
    private mutating func parseByteString() throws -> Bencode {
        let start = pos
        while pos < data.count, data[pos] != UInt8(ascii: ":") { pos += 1 }
        guard pos < data.count else { throw ParseError.unexpectedEnd }
        let lenStr = String(data: data[start..<pos], encoding: .ascii) ?? ""
        guard let len = Int(lenStr) else { throw ParseError.invalidFormat(offset: start) }
        pos += 1 // skip ':'
        guard pos + len <= data.count else { throw ParseError.unexpectedEnd }
        let bytes = data[pos..<(pos + len)]
        pos += len
        return .bytes(bytes)
    }

    // "l<items>e"
    private mutating func parseList() throws -> Bencode {
        pos += 1 // skip 'l'
        var items: [Bencode] = []
        while pos < data.count, data[pos] != UInt8(ascii: "e") {
            items.append(try parse())
        }
        guard pos < data.count else { throw ParseError.unexpectedEnd }
        pos += 1 // skip 'e'
        return .list(items)
    }

    // "d<key><value>...e"
    private mutating func parseDict() throws -> Bencode {
        pos += 1 // skip 'd'
        var pairs: [(key: String, value: Bencode)] = []
        while pos < data.count, data[pos] != UInt8(ascii: "e") {
            guard case .bytes(let keyBytes) = try parseByteString() else {
                throw ParseError.invalidFormat(offset: pos)
            }
            let key = String(data: keyBytes, encoding: .utf8)
                ?? String(data: keyBytes, encoding: .isoLatin1)
                ?? ""
            let value = try parse()
            pairs.append((key: key, value: value))
        }
        guard pos < data.count else { throw ParseError.unexpectedEnd }
        pos += 1 // skip 'e'
        return .dict(pairs)
    }
}
