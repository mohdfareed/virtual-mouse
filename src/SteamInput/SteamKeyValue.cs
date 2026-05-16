using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SteamInput;

internal sealed class SteamKeyValue
{
    public Dictionary<string, SteamKeyValue> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; set; }

    public SteamKeyValue? GetChild(string key)
    {
        return Children.TryGetValue(key, out SteamKeyValue? child) ? child : null;
    }

    public string? GetValue(string key)
    {
        return GetChild(key)?.Value;
    }
}

internal static class SteamKeyValueParser
{
    public static SteamKeyValue ParseText(string text)
    {
        TextParser parser = new(text);
        return parser.Parse();
    }

    public static SteamKeyValue ParseBinary(byte[] data)
    {
        BinaryParser parser = new(data);
        return parser.Parse();
    }

    private sealed class TextParser(string text)
    {
        private int _position;

        public SteamKeyValue Parse()
        {
            SteamKeyValue root = new();
            ParseObject(root, stopOnCloseBrace: false);
            return root;
        }

        private void ParseObject(SteamKeyValue target, bool stopOnCloseBrace)
        {
            while (true)
            {
                SkipTrivia();
                if (_position >= text.Length)
                {
                    return;
                }

                if (text[_position] == '}')
                {
                    _position++;
                    if (stopOnCloseBrace)
                    {
                        return;
                    }

                    throw new FormatException("Unexpected closing brace in Steam KeyValues text.");
                }

                string key = ReadToken();
                SkipTrivia();
                if (_position < text.Length && text[_position] == '{')
                {
                    _position++;
                    SteamKeyValue child = new();
                    ParseObject(child, stopOnCloseBrace: true);
                    target.Children[key] = child;
                    continue;
                }

                string value = ReadToken();
                target.Children[key] = new SteamKeyValue { Value = value };
            }
        }

        private string ReadToken()
        {
            SkipTrivia();
            return _position >= text.Length
                ? throw new FormatException("Unexpected end of Steam KeyValues text.")
                : text[_position] == '"'
                ? ReadQuotedToken()
                : ReadBareToken();
        }

        private string ReadQuotedToken()
        {
            _position++;
            StringBuilder builder = new();
            while (_position < text.Length)
            {
                char current = text[_position++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current == '\\' && _position < text.Length)
                {
                    char escaped = text[_position++];
                    _ = builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped,
                    });
                    continue;
                }

                _ = builder.Append(current);
            }

            throw new FormatException("Unterminated quoted token in Steam KeyValues text.");
        }

        private string ReadBareToken()
        {
            int start = _position;
            while (_position < text.Length &&
                !char.IsWhiteSpace(text[_position]) &&
                text[_position] is not '{' and not '}')
            {
                _position++;
            }

            return start == _position
                ? throw new FormatException("Expected token in Steam KeyValues text.")
                : text[start.._position];
        }

        private void SkipTrivia()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]))
                {
                    _position++;
                    continue;
                }

                if (_position + 1 < text.Length && text[_position] == '/' && text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < text.Length && text[_position] is not '\r' and not '\n')
                    {
                        _position++;
                    }

                    continue;
                }

                return;
            }
        }
    }

    private sealed class BinaryParser(byte[] data)
    {
        private const byte ObjectType = 0x00;
        private const byte StringType = 0x01;
        private const byte Int32Type = 0x02;
        private const byte Float32Type = 0x03;
        private const byte UInt64Type = 0x07;
        private const byte EndType = 0x08;
        private const byte Int64Type = 0x0a;
        private int _position;

        public SteamKeyValue Parse()
        {
            SteamKeyValue root = new();
            ParseObject(root);
            return root;
        }

        private void ParseObject(SteamKeyValue target)
        {
            while (_position < data.Length)
            {
                byte type = data[_position++];
                if (type == EndType)
                {
                    return;
                }

                string key = ReadNullTerminatedString();
                if (type == ObjectType)
                {
                    SteamKeyValue child = new();
                    ParseObject(child);
                    target.Children[key] = child;
                    continue;
                }

                target.Children[key] = new SteamKeyValue { Value = ReadValue(type) };
            }
        }

        private string ReadValue(byte type)
        {
            return type switch
            {
                StringType => ReadNullTerminatedString(),
                Int32Type => ReadUInt32().ToString(CultureInfo.InvariantCulture),
                Float32Type => ReadSingle().ToString(CultureInfo.InvariantCulture),
                UInt64Type => ReadUInt64().ToString(CultureInfo.InvariantCulture),
                Int64Type => ReadInt64().ToString(CultureInfo.InvariantCulture),
                _ => throw new FormatException($"Unsupported Steam binary KeyValues type 0x{type:x2}."),
            };
        }

        private string ReadNullTerminatedString()
        {
            int start = _position;
            while (_position < data.Length && data[_position] != 0)
            {
                _position++;
            }

            if (_position >= data.Length)
            {
                throw new FormatException("Unterminated string in Steam binary KeyValues data.");
            }

            string value = Encoding.UTF8.GetString(data, start, _position - start);
            _position++;
            return value;
        }

        private uint ReadUInt32()
        {
            EnsureAvailable(sizeof(uint));
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_position, sizeof(uint)));
            _position += sizeof(uint);
            return value;
        }

        private ulong ReadUInt64()
        {
            EnsureAvailable(sizeof(ulong));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(_position, sizeof(ulong)));
            _position += sizeof(ulong);
            return value;
        }

        private long ReadInt64()
        {
            EnsureAvailable(sizeof(long));
            long value = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(_position, sizeof(long)));
            _position += sizeof(long);
            return value;
        }

        private float ReadSingle()
        {
            EnsureAvailable(sizeof(float));
            float value = BitConverter.ToSingle(data, _position);
            _position += sizeof(float);
            return value;
        }

        private void EnsureAvailable(int size)
        {
            if (_position + size > data.Length)
            {
                throw new FormatException("Unexpected end of Steam binary KeyValues data.");
            }
        }
    }
}
