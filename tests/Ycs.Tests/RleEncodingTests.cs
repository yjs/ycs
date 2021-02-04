// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class RleEncodingTests
    {
        [TestMethod]
        public void TestRleEncoder()
        {
            TestEncoder(new RleEncoder(), s => new RleDecoder(s));
        }

        [TestMethod]
        public void TestRleIntDiffEncoder()
        {
            TestEncoder(new RleIntDiffEncoder(0), s => new RleIntDiffDecoder(s, 0));
        }

        [TestMethod]
        public void TestIntDiffOptRleEncoder()
        {
            TestEncoder(new IntDiffOptRleEncoder(), s => new IntDiffOptRleDecoder(s));
        }

        [TestMethod]
        public void TestIntDiffEncoder()
        {
            TestEncoder(new IntDiffEncoder(0), s => new IntDiffDecoder(s, 0));
            TestEncoder(new IntDiffEncoder(42), s => new IntDiffDecoder(s, 42));
        }

        [TestMethod]
        public void TestStringEncoder()
        {
            const int n = 100;
            var words = new List<string>(n);
            var encoder = new StringEncoder();

            for (int i = 0; i < n; i++)
            {
                var v = Guid.NewGuid().ToString();
                words.Add(v);
                encoder.Write(v);
            }

            var data = encoder.ToArray();
            using (var stream = new MemoryStream(data))
            {
                var decoder = new StringDecoder(stream);

                for (int i = 0; i < words.Count; i++)
                {
                    Assert.AreEqual(words[i], decoder.Read());
                }
            }
        }

        [TestMethod]
        public void TestStringEncoderEmptyString()
        {
            const int n = 10;
            var encoder = new StringEncoder();

            for (int i = 0; i < n; i++)
            {
                encoder.Write(string.Empty);
            }

            var data = encoder.ToArray();
            using (var stream = new MemoryStream(data))
            {
                var decoder = new StringDecoder(stream);

                for (int i = 0; i < n; i++)
                {
                    Assert.AreEqual(string.Empty, decoder.Read());
                }
            }
        }

        private void TestEncoder<TEncoder, TDecoder>(TEncoder encoder, Func<Stream, TDecoder> createDecoder, int n = 100)
            where TEncoder : IEncoder<long>
            where TDecoder : IDecoder<long>
        {
            for (int i = -n; i < n; i++)
            {
                encoder.Write(i);

                // Write additional 'i' times.
                for (int j = 0; j < i; j++)
                {
                    encoder.Write(i);
                }
            }

            var data = encoder.ToArray();
            using (var stream = new MemoryStream(data))
            {
                var decoder = createDecoder(stream);

                for (int i = -n; i < n; i++)
                {
                    Assert.AreEqual(i, decoder.Read());

                    // Read additional 'i' times.
                    for (int j = 0; j < i; j++)
                    {
                        Assert.AreEqual(i, decoder.Read());
                    }
                }
            }
        }

        private void TestEncoder<TEncoder, TDecoder>(TEncoder encoder, Func<Stream, TDecoder> createDecoder, byte n = 100)
            where TEncoder : IEncoder<byte>
            where TDecoder : IDecoder<byte>
        {
            for (byte i = 0; i < n; i++)
            {
                encoder.Write(i);

                // Write additional 'i' times.
                for (byte j = 0; j < i; j++)
                {
                    encoder.Write(i);
                }
            }

            var data = encoder.ToArray();
            using (var stream = new MemoryStream(data))
            {
                var decoder = createDecoder(stream);

                for (byte i = 0; i < n; i++)
                {
                    Assert.AreEqual(i, decoder.Read());

                    // Read additional 'i' times.
                    for (byte j = 0; j < i; j++)
                    {
                        Assert.AreEqual(i, decoder.Read());
                    }
                }
            }
        }
    }
}
