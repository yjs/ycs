// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections;
using System.IO;
using System.Text;

namespace Ycs
{
    internal static class BinaryWriterExtensions
    {
        /// <summary>
        /// Write two bytes as an unsigned unteger.
        /// </summary>
        public static void WriteUint16(this BinaryWriter writer, ushort num)
        {
            writer.Write((byte)(num & Bits.Bits8));
            writer.Write((byte)((num >> 8) & Bits.Bits8));
        }

        /// <summary>
        /// Write four bytes as an unsigned integer.
        /// </summary>
        public static void WriteUint32(this BinaryWriter writer, uint num)
        {
            for (int i = 0; i < 4; i++)
            {
                writer.Write((byte)(num & Bits.Bits8));
                num >>= 8;
            }
        }

        /// <summary>
        /// Write a variable length unsigned integer.
        /// Encodes integers in the range from <c>[0, 4294967295] / [0, 0xFFFFFFFF]</c>.
        /// </summary>
        public static void WriteVarUint(this BinaryWriter writer, uint num)
        {
            while (num > Bits.Bits7)
            {
                writer.Write((byte)(Bit.Bit8 | (Bits.Bits7 & num)));
                num >>= 7;
            }

            writer.Write((byte)(Bits.Bits7 & num));
        }

        /// <summary>
        /// Write a variable length integer.
        /// <br/>
        /// Encodes integers in the range from <c>[-2147483648, -2147483647]</c>.
        /// <br/>
        /// We don't use zig-zag encoding because we want to keep the option open
        /// to use the same function for BigInt and 53bit integers (doubles).
        /// <br/>
        /// We use the 7th bit instead for signalling that this is a negative number.
        /// </summary>
        public static void WriteVarInt(this BinaryWriter writer, int num, bool? treatZeroAsNegative = null)
        {
            bool isNegative = num == 0 ? (treatZeroAsNegative ?? false) : num < 0;
            if (isNegative)
            {
                num = -num;
            }

            //                  |   whether to continue reading   |         is negative         | value.
            writer.Write((byte)((num > Bits.Bits6 ? Bit.Bit8 : 0) | (isNegative ? Bit.Bit7 : 0) | (Bits.Bits6 & num)));
            num >>= 6;

            // We don't need to consider the case of num == 0 so we can use a different pattern here than above.
            while (num > 0)
            {
                writer.Write((byte)((num > Bits.Bits7 ? Bit.Bit8 : 0) | (Bits.Bits7 & num)));
                num >>= 7;
            }
        }

        /// <summary>
        /// Write a variable length string.
        /// </summary>
        public static void WriteVarString(this BinaryWriter writer, string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            writer.WriteVarUint8Array(data);
        }

        /// <summary>
        /// Append a byte array to encoder.
        /// </summary>
        public static void WriteVarUint8Array(this BinaryWriter writer, byte[] array)
        {
            writer.WriteVarUint((uint)array.Length);
            writer.Write(array);
        }

        /// <summary>
        /// Encode data with efficient binary format.
        ///
        /// Differences to JSON:
        /// * Transforms data to a binary format (not to a string).
        /// * Encodes undefined, NaN, and ArrayBuffer (these can't be represented in JSON).
        /// * Numbers are efficiently encoded either as a variable length integer, as a 32-bit
        ///   float, or as a 64-bit float.
        ///
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
        ///
        /// Reasons for the decreasing prefix:
        /// We need the first bit for extendability (later we may want to encode the prefix with <see cref="WriteVarUint(BinaryWriter, uint)"/>).
        /// The remaining 7 bits are divided as follows:
        /// [0-30]   The beginning of the data range is used for custom purposes
        ///          (defined by the function that uses this library).
        /// [31-127] The end of the data range is used for data encoding.
        /// </summary>
        public static void WriteAny(this BinaryWriter writer, object o)
        {
            switch (o)
            {
                case string str: // TYPE 119: STRING
                    writer.Write((byte)119);
                    writer.WriteVarString(str);
                    break;
                case bool b: // TYPE 120/121: boolean (true/false)
                    writer.Write((byte)(b ? 120 : 121));
                    break;
                case double d: // TYPE 123: FLOAT64
                    writer.Write((byte)123);
                    writer.Write(BitConverter.DoubleToInt64Bits(d));
                    break;
                case float f: // TYPE 124: FLOAT32
                    //writer.Write((byte)124);
                    //writer.Write(BitConverter.SingleToInt32Bits(f));
                    //break;
                    throw new NotImplementedException("Needs unsafe or netstandard2.1");
                case int i: // TYPE 125: INTEGER
                    writer.Write((byte)125);
                    writer.WriteVarInt(i);
                    break;
                case null: // TYPE 126: null
                    writer.Write((byte)126);
                    break;
                case byte[] ba: // TYPE 116: ArrayBuffer
                    writer.Write((byte)116);
                    writer.WriteVarUint8Array(ba);
                    break;
                case IDictionary dict: // TYPE 118: object (Dictionary<string, object>)
                    writer.Write((byte)118);
                    writer.WriteVarUint((uint)dict.Count);
                    foreach (var key in dict.Keys)
                    {
                        writer.WriteVarString(key.ToString());
                        writer.WriteAny(dict[key]);
                    }
                    break;
                case ICollection col: // TYPE 117: Array
                    writer.Write((byte)117);
                    writer.WriteVarUint((uint)col.Count);
                    foreach (var item in col)
                    {
                        writer.WriteAny(item);
                    }
                    break;
                default:
                    // TYPE 127: undefined
                    writer.Write((byte)127);
                    break;
            }
        }
    }
}
