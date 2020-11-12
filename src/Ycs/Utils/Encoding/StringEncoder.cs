// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ycs
{
    /// <summary>
    /// Optimized String Encoder.
    /// <br/>
    /// The lengths are encoded using the <see cref="UintOptRleEncoder"/>.
    /// </summary>
    public sealed class StringEncoder : IEncoder<string>, IDisposable
    {
        private StringBuilder _sb = new StringBuilder();
        private UintOptRleEncoder _lengthEncoder = new UintOptRleEncoder();
        private bool _disposed;

        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        public void Write(string value)
        {
            _sb.Append(value);
            _lengthEncoder.Write((uint)value.Length);
        }

        public void Write(char[] value, int offset, int count)
        {
            _sb.Append(value, offset, count);
            _lengthEncoder.Write((uint)count);
        }

        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.WriteVarString(_sb.ToString());
                writer.Write(_lengthEncoder.ToArray());
            }

            return stream.ToArray();
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _sb.Clear();
                    _lengthEncoder.Dispose();
                }

                _sb = null;
                _lengthEncoder = null;

                _disposed = true;
            }
        }
    }
}
