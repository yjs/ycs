// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ycs
{
    /// <summary>
    /// Contains <see cref="Stream"/> extensions compatible with the <c>lib0</c>:
    /// <see href="https://github.com/dmonad/lib0"/>.
    /// </summary>
    internal static class StreamEncodingExtensions
    {
        /// <summary>
        /// Writes two bytes as an unsigned unteger.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUint16(this Stream stream, ushort num)
        {
            stream.WriteByte((byte)(num & Bits.Bits8));
            stream.WriteByte((byte)((num >> 8) & Bits.Bits8));
        }

        /// <summary>
        /// Writes four bytes as an unsigned integer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUint32(this Stream stream, uint num)
        {
            for (int i = 0; i < 4; i++)
            {
                stream.WriteByte((byte)(num & Bits.Bits8));
                num >>= 8;
            }
        }

        /// <summary>
        /// Writes a variable length unsigned integer.
        /// Encodes integers in the range <c>[0, 4294967295] / [0, 0xFFFFFFFF]</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarUint(this Stream stream, uint num)
        {
            while (num > Bits.Bits7)
            {
                stream.WriteByte((byte)(Bit.Bit8 | (Bits.Bits7 & num)));
                num >>= 7;
            }

            stream.WriteByte((byte)(Bits.Bits7 & num));
        }

        /// <summary>
        /// Writes a variable length integer.
        /// <br/>
        /// Encodes integers in the range <c>[-2147483648, -2147483647]</c>.
        /// <br/>
        /// We don't use zig-zag encoding because we want to keep the option open
        /// to use the same function for <c>BigInt</c> and 53-bit integers (doubles).
        /// <br/>
        /// We use the 7-th bit instead for signalling that this is a negative number.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarInt(this Stream stream, long num, bool? treatZeroAsNegative = null)
        {
            bool isNegative = num == 0 ? (treatZeroAsNegative ?? false) : num < 0;
            if (isNegative)
            {
                num = -num;
            }

            //                      |   whether to continue reading   |         is negative         | value.
            stream.WriteByte((byte)((num > Bits.Bits6 ? Bit.Bit8 : 0) | (isNegative ? Bit.Bit7 : 0) | (Bits.Bits6 & num)));
            num >>= 6;

            // We don't need to consider the case of num == 0 so we can use a different pattern here than above.
            while (num > 0)
            {
                stream.WriteByte((byte)((num > Bits.Bits7 ? Bit.Bit8 : 0) | (Bits.Bits7 & num)));
                num >>= 7;
            }
        }

        /// <summary>
        /// Writes a variable length string.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarString(this Stream stream, string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            stream.WriteVarUint8Array(data);
        }

        /// <summary>
        /// Appends a byte array to the stream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarUint8Array(this Stream stream, byte[] array)
        {
            stream.WriteVarUint((uint)array.Length);
            stream.Write(array, 0, array.Length);
        }

        /// <summary>
        /// Encodes data with efficient binary format.
        /// <br/>
        /// Differences to JSON:
        /// * Transforms data to a binary format (not to a string).
        /// * Encodes undefined, NaN, and ArrayBuffer (these can't be represented in JSON).
        /// * Numbers are efficiently encoded either as a variable length integer, as a 32-bit
        ///   float, or as a 64-bit float.
        /// <br/>
        /// Encoding table:
        /// | Data Type                      | Prefix | Encoding method   | Comment                                                        |
        /// | ------------------------------ | ------ | ----------------- | -------------------------------------------------------------- |
        /// | undefined                      | 127    |                   | Functions, symbol, and everything that cannot be identified    |
        /// |                                |        |                   | is encdoded as undefined.                                      |
        /// | null                           | 126    |                   |                                                                |
        /// | integer                        | 125    | WriteVarInt       | Only encodes 32-bit signed integers.                           |
        /// | float                          | 124    | SingleToInt32Bits |                                                                |
        /// | double                         | 123    | DoubleToInt64Bits |                                                                |
        /// | boolean (false)                | 121    |                   | True and false are different data types so we save the         |
        /// | boolean (true)        |        | 120    |                   | following byte (0b_01111000) so the last bit determines value. |
        /// | string                         | 119    | WriteVarString    |                                                                |
        /// | IDictionary&lt;string, any&gt; | 118    | custom            | Writes length, then key-value pairs.                           |
        /// | ICollection&lt;any&gt;         | 117    | custom            | Writes length, then values.                                    |
        /// | byte[]                         | 116    |                   | We use byte[] for any kind of binary data.                     |
        /// <br/>
        /// Reasons for the decreasing prefix:
        /// We need the first bit for extendability (later we may want to encode the prefix with <see cref="WriteVarUint(BinaryWriter, uint)"/>).
        /// The remaining 7 bits are divided as follows:
        /// [0-30]   The beginning of the data range is used for custom purposes
        ///          (defined by the function that uses this library).
        /// [31-127] The end of the data range is used for data encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteAny(this Stream stream, object o)
        {
            switch (o)
            {
                case string str: // TYPE 119: STRING
                    stream.WriteByte(119);
                    stream.WriteVarString(str);
                    break;
                case bool b: // TYPE 120/121: boolean (true/false)
                    stream.WriteByte((byte)(b ? 120 : 121));
                    break;
                case double d: // TYPE 123: FLOAT64
#if NETSTANDARD2_0
                    var dBytes = BitConverter.GetBytes(d);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(dBytes);
                    }
                    stream.WriteByte(123);
                    stream.Write(dBytes, 0, dBytes.Length);
                    break;
#elif NETSTANDARD2_1
                    Span<byte> dBytes = stackalloc byte[8];
                    if (!BitConverter.TryWriteBytes(dBytes, d))
                    {
                        throw new InvalidDataException("Unable to write a double value.");
                    }
                    if (BitConverter.IsLittleEndian)
                    {
                        dBytes.Reverse();
                    }
                    stream.WriteByte(123);
                    stream.Write(dBytes);
                    break;
#endif // NETSTANDARD2_0
                case float f: // TYPE 124: FLOAT32
#if NETSTANDARD2_0
                    var fBytes = BitConverter.GetBytes(f);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(fBytes);
                    }
                    stream.WriteByte(124);
                    stream.Write(fBytes, 0, fBytes.Length);
                    break;
#elif NETSTANDARD2_1
                    Span<byte> fBytes = stackalloc byte[4];
                    if (!BitConverter.TryWriteBytes(fBytes, f))
                    {
                        throw new InvalidDataException("Unable to write a float value.");
                    }
                    if (BitConverter.IsLittleEndian)
                    {
                        fBytes.Reverse();
                    }
                    stream.WriteByte(124);
                    stream.Write(fBytes);
                    break;
#endif // NETSTANDARD2_0
                case int i: // TYPE 125: INTEGER
                    stream.WriteByte(125);
                    stream.WriteVarInt(i);
                    break;
                case long l: // Special case: treat LONG as INTEGER.
                    stream.WriteByte(125);
                    stream.WriteVarInt(l);
                    break;
                case null: // TYPE 126: null
                           // TYPE 127: undefined
                    stream.WriteByte(126);
                    break;
                case byte[] ba: // TYPE 116: ArrayBuffer
                    stream.WriteByte(116);
                    stream.WriteVarUint8Array(ba);
                    break;
                case IDictionary dict: // TYPE 118: object (Dictionary<string, object>)
                    stream.WriteByte(118);
                    stream.WriteVarUint((uint)dict.Count);
                    foreach (var key in dict.Keys)
                    {
                        stream.WriteVarString(key.ToString());
                        stream.WriteAny(dict[key]);
                    }
                    break;
                case ICollection col: // TYPE 117: Array
                    stream.WriteByte(117);
                    stream.WriteVarUint((uint)col.Count);
                    foreach (var item in col)
                    {
                        stream.WriteAny(item);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported object type: {o?.GetType()}");
            }
        }
    }
}
