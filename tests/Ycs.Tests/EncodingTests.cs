// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class EncodingTests
    {
        /// <summary>
        /// Check if binary encoding is compatible with golang binary encoding.
        /// Result: is compatible up to 32 bit: [0, 4294967295] / [0, 0xFFFFFFFF].
        /// </summary>
        [DataTestMethod]
        [DataRow(0u, new byte[] { 0 })]
        [DataRow(1u, new byte[] { 1 })]
        [DataRow(128u, new byte[] { 128, 1 })]
        [DataRow(200u, new byte[] { 200, 1 })]
        [DataRow(32u, new byte[] { 32 })]
        [DataRow(500u, new byte[] { 244, 3 })]
        [DataRow(256u, new byte[] { 128, 2 })]
        [DataRow(700u, new byte[] { 188, 5 })]
        [DataRow(1024u, new byte[] { 128, 8 })]
        [DataRow(1025u, new byte[] { 129, 8 })]
        [DataRow(4048u, new byte[] { 208, 31 })]
        [DataRow(5050u, new byte[] { 186, 39 })]
        [DataRow(1_000_000u, new byte[] { 192, 132, 61 })]
        [DataRow(34_951_959u, new byte[] { 151, 166, 213, 16 })]
        [DataRow(2_147_483_646u, new byte[] { 254, 255, 255, 255, 7 })]
        [DataRow(2_147_483_647u, new byte[] { 255, 255, 255, 255, 7 })]
        [DataRow(2_147_483_648u, new byte[] { 128, 128, 128, 128, 8 })]
        [DataRow(2_147_483_700u, new byte[] { 180, 128, 128, 128, 8 })]
        [DataRow(4_294_967_294u, new byte[] { 254, 255, 255, 255, 15 })]
        [DataRow(4_294_967_295u, new byte[] { 255, 255, 255, 255, 15 })]
        public void TestGolangBinaryEncodingCompatibility(uint value, byte[] expected)
        {
            using (var stream = new MemoryStream())
            {
                stream.WriteVarUint(value);

                var actual = stream.ToArray();
                CollectionAssert.AreEqual(expected, actual);
            }
        }

        [TestMethod]
        public void TestEncodeMax32BitUint()
        {
            DoTestEncoding<uint>("Max 32bit uint", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), Bits.Bits32);
        }

        [TestMethod]
        public void TestVarUintEncoding()
        {
            DoTestEncoding<uint>("VarUint 1 byte", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), 42);
            DoTestEncoding<uint>("VarUint 2 bytes", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), 1 << 9 | 3);
            DoTestEncoding<uint>("VarUint 3 bytes", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), 1 << 17 | 1 << 9 | 3);
            DoTestEncoding<uint>("VarUint 4 bytes", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), 1 << 25 | 1 << 17 | 1 << 9 | 3);
            DoTestEncoding<uint>("VarUint of 2839012934", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), 2_839_012_934);
        }

        [TestMethod]
        public void TestVarIntEncoding()
        {
            DoTestEncoding<long>("VarInt 1 byte", (w, v) => w.WriteVarInt(v), (r) => r.ReadVarInt().Value, -42);
            DoTestEncoding<long>("VarInt 2 bytes", (w, v) => w.WriteVarInt(v), (r) => r.ReadVarInt().Value, -(1 << 9 | 3));
            DoTestEncoding<long>("VarInt 3 bytes", (w, v) => w.WriteVarInt(v), (r) => r.ReadVarInt().Value, -(1 << 17 | 1 << 9 | 3));
            DoTestEncoding<long>("VarInt 4 bytes", (w, v) => w.WriteVarInt(v), (r) => r.ReadVarInt().Value, -(1 << 25 | 1 << 17 | 1 << 9 | 3));
            DoTestEncoding<long>("VarInt of -691529286", (w, v) => w.WriteVarInt(v), (r) => r.ReadVarInt().Value, -691_529_286);
            DoTestEncoding<long>("VarInt of 64 (-0)", (w, v) => w.WriteVarInt(v, treatZeroAsNegative: true), (r) => r.ReadVarInt().Value, 0);
        }

        [TestMethod]
        public void TestEncodeFloatingPoint()
        {
            DoTestEncoding<float>("", (w, v) => w.WriteAny(v), (r) => (float)r.ReadAny(), 2.0f);
            DoTestEncoding<double>("", (w, v) => w.WriteAny(v), (r) => (double)r.ReadAny(), 2.0);
        }

        [TestMethod]
        public void TestVarIntEncodingNegativeZero()
        {
            using (var stream = new MemoryStream())
            {
                stream.WriteVarInt(0, treatZeroAsNegative: true);

                var actual = stream.ToArray();

                // '-0' should have the 7th bit set, i.e. == 64.
                CollectionAssert.AreEqual(new byte[] { 64 }, actual);

                using (var inputStream = new MemoryStream(actual))
                {
                    var v = inputStream.ReadVarInt();
                    Assert.AreEqual(0, v.Value);
                    Assert.AreEqual(-1, v.Sign);
                }
            }
        }

        [TestMethod]
        public void TestRepeatVarUintEncoding()
        {
            var rand = new Random();
            for (int i = 0; i < 10; i++)
            {
                var n = rand.Next(0, (1 << 28) - 1);
                DoTestEncoding<uint>($"VarUint of {n}", (w, v) => w.WriteVarUint(v), (r) => r.ReadVarUint(), (uint)n);
            }
        }

        [TestMethod]
        public void TestRepeatVarIntEncoding()
        {
            var rand = new Random();
            for (int i = 0; i < 10; i++)
            {
                var n = rand.Next(0, int.MaxValue);
                DoTestEncoding<long>($"VarInt of {n}", (w, v) => w.WriteVarInt(v), (r) => r.ReadVarInt().Value, n);
            }
        }

        [TestMethod]
        public void TestStringEncoding()
        {
            DoTestEncoding<string>(string.Empty, (w, v) => w.WriteVarString(v), (r) => r.ReadVarString(), "hello");
            DoTestEncoding<string>(string.Empty, (w, v) => w.WriteVarString(v), (r) => r.ReadVarString(), string.Empty);
            DoTestEncoding<string>(string.Empty, (w, v) => w.WriteVarString(v), (r) => r.ReadVarString(), "쾟");
            DoTestEncoding<string>(string.Empty, (w, v) => w.WriteVarString(v), (r) => r.ReadVarString(), "龟"); // Surrogate length 3.
            DoTestEncoding<string>(string.Empty, (w, v) => w.WriteVarString(v), (r) => r.ReadVarString(), "😝"); // Surrogate length 4.
        }

        private void DoTestEncoding<T>(string description, Action<Stream, T> write, Func<Stream, T> read, T val)
        {
            byte[] encoded;

            using (var outputStream = new MemoryStream())
            {
                write(outputStream, val);
                encoded = outputStream.ToArray();
            }

            using (var inputStream = new MemoryStream(encoded))
            {
                var decodedValue = read(inputStream);
                Assert.AreEqual(val, decodedValue, description);
            }
        }
    }
}
