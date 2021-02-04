// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ycs
{
    /// <summary>
    /// Contains <see cref="Stream"/> extensions compatible with the <c>lib0</c>:
    /// <see href="https://github.com/dmonad/lib0"/>.
    /// </summary>
    internal static class StreamDecodingExtensions
    {
        /// <summary>
        /// Reads two bytes as an unsigned integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUint16(this Stream stream)
        {
            return (ushort)(stream._ReadByte() + (stream._ReadByte() << 8));
        }

        /// <summary>
        /// Reads four bytes as an unsigned integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUint32(this Stream stream)
        {
            return (uint)((stream._ReadByte() + (stream._ReadByte() << 8) + (stream._ReadByte() << 16) + (stream._ReadByte() << 24)) >> 0);
        }

        /// <summary>
        /// Reads unsigned integer (32-bit) with variable length.
        /// 1/8th of the storage is used as encoding overhead.
        /// * Values &lt; 2^7 are stored in one byte.
        /// * Values &lt; 2^14 are stored in two bytes.
        /// </summary>
        /// <exception cref="InvalidDataException">Invalid binary format.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadVarUint(this Stream stream)
        {
            uint num = 0;
            int len = 0;

            while (true)
            {
                byte r = stream._ReadByte();
                num |= (r & Bits.Bits7) << len;
                len += 7;

                if (r < Bit.Bit8)
                {
                    return num;
                }

                if (len > 35)
                {
                    throw new InvalidDataException("Integer out of range.");
                }
            }
        }

        /// <summary>
        /// Reads a 32-bit variable length signed integer.
        /// 1/8th of storage is used as encoding overhead.
        /// * Values &lt; 2^7 are stored in one byte.
        /// * Values &lt; 2^14 are stored in two bytes.
        /// </summary>
        /// <exception cref="InvalidDataException">Invalid binary format.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (long Value, int Sign) ReadVarInt(this Stream stream)
        {
            byte r = stream._ReadByte();
            uint num = r & Bits.Bits6;
            int len = 6;
            int sign = (r & Bit.Bit7) > 0 ? -1 : 1;

            if ((r & Bit.Bit8) == 0)
            {
                // Don't continue reading.
                return (sign * num, sign);
            }

            while (true)
            {
                r = stream._ReadByte();
                num |= (r & Bits.Bits7) << len;
                len += 7;

                if (r < Bit.Bit8)
                {
                    return (sign * num, sign);
                }

                if (len > 41)
                {
                    throw new InvalidDataException("Integer out of range");
                }
            }
        }

        /// <summary>
        /// Reads a variable length string.
        /// </summary>
        /// <remarks>
        /// <see cref="StreamEncodingExtensions.WriteVarUint(Stream, uint)"/> is used to store the length of the string.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ReadVarString(this Stream stream)
        {
            uint remainingLen = stream.ReadVarUint();
            if (remainingLen == 0)
            {
                return string.Empty;
            }

            var data = stream._ReadBytes((int)remainingLen);
            var str = Encoding.UTF8.GetString(data);
            return str;
        }

        /// <summary>
        /// Reads a variable length byte array.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] ReadVarUint8Array(this Stream stream)
        {
            uint len = stream.ReadVarUint();
            return stream._ReadBytes((int)len);
        }

        /// <summary>
        /// Reads variable length byte array as a readable <see cref="MemoryStream"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MemoryStream ReadVarUint8ArrayAsStream(this Stream stream)
        {
            var data = stream.ReadVarUint8Array();
            return new MemoryStream(data, writable: false);
        }

        /// <summary>
        /// Decodes data from the stream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object ReadAny(this Stream stream)
        {
            byte type = stream._ReadByte();
            switch (type)
            {
                case 119: // String
                    return stream.ReadVarString();
                case 120: // boolean true
                    return true;
                case 121: // boolean false
                    return false;
                case 123: // Float64
#if NETSTANDARD2_0
                    var dBytes = new byte[8];
                    stream._ReadBytes(dBytes);

                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(dBytes);
                    }

                    return BitConverter.ToDouble(dBytes, 0);
#elif NETSTANDARD2_1
                    Span<byte> dBytes = stackalloc byte[8];
                    stream._ReadBytes(dBytes);

                    if (BitConverter.IsLittleEndian)
                    {
                        dBytes.Reverse();
                    }

                    return BitConverter.ToDouble(dBytes);
#endif // NETSTANDARD2_0
                case 124: // Float32
#if NETSTANDARD2_0
                    var fBytes = new byte[4];
                    stream._ReadBytes(fBytes);

                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(fBytes);
                    }

                    return BitConverter.ToSingle(fBytes, 0);
#elif NETSTANDARD2_1
                    Span<byte> fBytes = stackalloc byte[4];
                    stream._ReadBytes(fBytes);

                    if (BitConverter.IsLittleEndian)
                    {
                        fBytes.Reverse();
                    }

                    return BitConverter.ToSingle(fBytes);
#endif // NETSTANDARD2_0
                case 125: // integer
                    return (int)stream.ReadVarInt().Value;
                case 126: // null
                case 127: // undefined
                    return null;
                case 116: // ArrayBuffer
                    return stream.ReadVarUint8Array();
                case 117: // Array<object>
                    {
                        var len = (int)stream.ReadVarUint();
                        var arr = new List<object>(len);

                        for (int i = 0; i < len; i++)
                        {
                            arr.Add(stream.ReadAny());
                        }

                        return arr;
                    }
                case 118: // object (Dictionary<string, object>)
                    {
                        var len = (int)stream.ReadVarUint();
                        var obj = new Dictionary<string, object>(len);

                        for (int i = 0; i < len; i++)
                        {
                            var key = stream.ReadVarString();
                            obj[key] = stream.ReadAny();
                        }

                        return obj;
                    }
                default:
                    throw new InvalidDataException($"Unknown object type: {type}");
            }
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte.
        /// </summary>
        /// <exception cref="EndOfStreamException">End of stream reached.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte _ReadByte(this Stream stream)
        {
            int v = stream.ReadByte();
            if (v < 0)
            {
                throw new EndOfStreamException();
            }

            return Convert.ToByte(v);
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position
        /// within the stream by the number of bytes read.
        /// </summary>
        /// <exception cref="EndOfStreamException">End of stream reached.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte[] _ReadBytes(this Stream stream, int count)
        {
            Debug.Assert(count >= 0);

            var result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                int v = stream.ReadByte();
                if (v < 0)
                {
                    throw new EndOfStreamException();
                }

                result[i] = Convert.ToByte(v);
            }

            return result;
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position
        /// within the stream by the number of bytes read.
        /// </summary>
        /// <exception cref="EndOfStreamException">End of stream reached.</exception>
#if NETSTANDARD2_0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _ReadBytes(this Stream stream, byte[] buffer)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            if (buffer.Length != stream.Read(buffer, 0, buffer.Length))
            {
                throw new EndOfStreamException();
            }
        }
#elif NETSTANDARD2_1
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void _ReadBytes(this Stream stream, Span<byte> buffer)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            if (buffer.Length != stream.Read(buffer))
            {
                throw new EndOfStreamException();
            }
        }
#endif // NETSTANDARD2_0
    }
}
