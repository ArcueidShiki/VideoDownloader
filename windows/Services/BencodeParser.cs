using System;
using System.Collections.Generic;
using System.Text;

namespace AnyDownloader.Windows.Services;

// ── Bencode value type ─────────────────────────────────────────────────────
//
// Bencode is the encoding used in .torrent files:
//   integer  → i<digits>e         e.g. i42e
//   bytes    → <len>:<data>       e.g. 4:spam
//   list     → l<items>e          e.g. l4:spami42ee
//   dict     → d<key><val>...e    e.g. d4:spaml1:a1:bee
//
// Mirrors AnyDownloaderCore/Engines/Torrent/BencodeParser.swift

internal abstract record BencodeValue;
internal sealed record BInteger(long Value) : BencodeValue;
internal sealed record BBytes(byte[] Data) : BencodeValue
{
    public string? AsString =>
        TryDecode(Encoding.UTF8) ?? TryDecode(Encoding.Latin1);

    private string? TryDecode(Encoding enc)
    {
        try   { return enc.GetString(Data); }
        catch { return null; }
    }
}
internal sealed record BList(IReadOnlyList<BencodeValue> Items) : BencodeValue;
internal sealed record BDict(IReadOnlyList<(string Key, BencodeValue Value)> Pairs) : BencodeValue
{
    public BencodeValue? this[string key]
    {
        get
        {
            foreach (var (k, v) in Pairs)
                if (k == key) return v;
            return null;
        }
    }
}

// ── Parser ─────────────────────────────────────────────────────────────────

internal ref struct BencodeParser
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public BencodeParser(ReadOnlySpan<byte> data) { _data = data; _pos = 0; }

    public BencodeValue Parse()
    {
        if (_pos >= _data.Length)
            throw new FormatException("Unexpected end of bencode data");

        return _data[_pos] switch
        {
            (byte)'i' => ParseInteger(),
            (byte)'l' => ParseList(),
            (byte)'d' => ParseDict(),
            >= (byte)'0' and <= (byte)'9' => ParseByteString(),
            _ => throw new FormatException($"Invalid bencode byte 0x{_data[_pos]:X2} at offset {_pos}"),
        };
    }

    // i<digits>e
    private BInteger ParseInteger()
    {
        _pos++; // skip 'i'
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != (byte)'e') _pos++;
        if (_pos >= _data.Length) throw new FormatException("Unterminated integer");
        var numStr = Encoding.ASCII.GetString(_data[start.._pos]);
        if (!long.TryParse(numStr, out var n)) throw new FormatException($"Bad integer: {numStr}");
        _pos++; // skip 'e'
        return new BInteger(n);
    }

    // <len>:<bytes>
    private BBytes ParseByteString()
    {
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != (byte)':') _pos++;
        if (_pos >= _data.Length) throw new FormatException("No ':' in byte string length");
        var lenStr = Encoding.ASCII.GetString(_data[start.._pos]);
        if (!int.TryParse(lenStr, out var len)) throw new FormatException($"Bad byte string length: {lenStr}");
        _pos++; // skip ':'
        if (_pos + len > _data.Length) throw new FormatException("Byte string exceeds data");
        var bytes = _data.Slice(_pos, len).ToArray();
        _pos += len;
        return new BBytes(bytes);
    }

    // l<items>e
    private BList ParseList()
    {
        _pos++; // skip 'l'
        var items = new List<BencodeValue>();
        while (_pos < _data.Length && _data[_pos] != (byte)'e')
            items.Add(Parse());
        if (_pos >= _data.Length) throw new FormatException("Unterminated list");
        _pos++; // skip 'e'
        return new BList(items);
    }

    // d<key><value>...e
    private BDict ParseDict()
    {
        _pos++; // skip 'd'
        var pairs = new List<(string, BencodeValue)>();
        while (_pos < _data.Length && _data[_pos] != (byte)'e')
        {
            var keyVal = ParseByteString();
            var key    = keyVal.AsString ?? string.Empty;
            var value  = Parse();
            pairs.Add((key, value));
        }
        if (_pos >= _data.Length) throw new FormatException("Unterminated dict");
        _pos++; // skip 'e'
        return new BDict(pairs);
    }
}
