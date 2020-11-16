// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Ycs
{
    internal static class BinaryReaderExtensinos
    {
        /// <summary>
        /// Read 2 bytes as unsigned integer.
        /// </summary>
        public static ushort ReadUint16(this BinaryReader reader)
        {
            return (ushort)(reader.ReadByte() + (reader.ReadByte() << 8));
        }

        /// <summary>
        /// Read 4 bytes as unsigned integer.
        /// </summary>
        public static uint ReadUint32(this BinaryReader reader)
        {
            return (uint)((reader.ReadByte() + (reader.ReadByte() << 8) + (reader.ReadByte() << 16) + (reader.ReadByte() << 24)) >> 0);
        }

        /// <summary>
        /// Read unsigned integer (32-bit) with variable length.
        /// 1/8th of the storage is used as encoding overhead.
        /// * Values &lt; 2^7 are stored in one byte.
        /// * Values &lt; 2^14 are stored in two bytes.
        /// </summary>
        public static uint ReadVarUint(this BinaryReader reader)
        {
            uint num = 0;
            int len = 0;

            while (true)
            {
                byte r = reader.ReadByte();
                num |= (r & Bits.Bits7) << len;
                len += 7;

                if (r < Bit.Bit8)
                {
                    return num;
                }

                if (len > 35)
                {
                    throw new Exception("Integer out of range!");
                }
            }
        }

        /// <summary>
        /// Read signed integer (32-bit) with variable length.
        /// 1/8th of storage is used as encoding overhead.
        /// * Values &lt; 2^7 are stored in one byte.
        /// * Values &lt; 2^14 are stored in two bytes.
        /// </summary>
        public static (int Value, int Sign) ReadVarInt(this BinaryReader reader)
        {
            byte r = reader.ReadByte();
            uint num = r & Bits.Bits6;
            int len = 6;
            int sign = (r & Bit.Bit7) > 0 ? -1 : 1;

            if ((r & Bit.Bit8) == 0)
            {
                // Don't continue reading.
                return (sign * (int)num, sign);
            }

            while (true)
            {
                r = reader.ReadByte();
                num |= (r & Bits.Bits7) << len;
                len += 7;

                if (r < Bit.Bit8)
                {
                    return (sign * (int)num, sign);
                }

                if (len > 41)
                {
                    throw new Exception("Integer out of range!");
                }
            }
        }

        /// <summary>
        /// Read string of variable length.
        /// VarUint is used to store the length of the string.
        /// </summary>
        public static string ReadVarString(this BinaryReader reader)
        {
            uint remainingLen = reader.ReadVarUint();
            if (remainingLen == 0)
            {
                return string.Empty;
            }

            var data = reader.ReadBytes((int)remainingLen);
            var str = Encoding.UTF8.GetString(data);
            return str;
        }

        /// <summary>
        /// Read variable length byte array.
        /// </summary>
        public static byte[] ReadVarUint8Array(this BinaryReader reader)
        {
            uint len = reader.ReadVarUint();
            return reader.ReadBytes((int)len);
        }

        /// <summary>
        /// Read variable length byte array as a <see cref="MemoryStream"/>.
        /// </summary>
        public static MemoryStream ReadVarUint8ArrayAsStream(this BinaryReader reader)
        {
            var data = reader.ReadVarUint8Array();
            return new MemoryStream(data, writable: false);
        }

        /// <summary>
        /// Decodes data from the reader.
        /// </summary>
        public static object ReadAny(this BinaryReader reader)
        {
            byte type = reader.ReadByte();
            switch (type)
            {
                case 119: // String
                    return reader.ReadVarString();
                case 120: // boolean true
                    return true;
                case 121: // boolean false
                    return false;
                case 123: // Float64
                    return BitConverter.Int64BitsToDouble(reader.ReadInt64());
                case 124: // Float32
                    return BitConverter.Int32BitsToSingle(reader.ReadInt32());
                case 125: // integer
                    return reader.ReadVarInt().Value;
                case 126: // null
                case 127: // undefined
                    return null;
                case 116: // ArrayBuffer
                    return reader.ReadVarUint8Array();
                case 117: // Array<object>
                    {
                        var len = (int)reader.ReadVarUint();
                        var arr = new List<object>(len);

                        for (int i = 0; i < len; i++)
                        {
                            arr.Add(reader.ReadAny());
                        }

                        return arr;
                    }
                case 118: // object (Dictionary<string, object>)
                    {
                        var len = (int)reader.ReadVarUint();
                        var obj = new Dictionary<string, object>(len);

                        for (int i = 0; i < len; i++)
                        {
                            var key = reader.ReadVarString();
                            obj[key] = reader.ReadAny();
                        }

                        return obj;
                    }
                default:
                    Debug.Assert(false, $"Unknown object type: {type}");
                    return null;
            }
        }
    }
}
