// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Ycs
{
    /// <summary>
    /// Optimized String Encoder.
    /// <br/>
    /// The lengths are encoded using the <see cref="UintOptRleEncoder"/>.
    /// </summary>
    /// <seealso cref="StringDecoder"/>
    internal class StringEncoder : IEncoder<string>
    {
        private StringBuilder _sb;
        private UintOptRleEncoder _lengthEncoder;
        private bool _disposed;

        public StringEncoder()
        {
            _sb = new StringBuilder();
            _lengthEncoder = new UintOptRleEncoder();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
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
            using (var stream = new MemoryStream())
            {
                stream.WriteVarString(_sb.ToString());

                var (buffer, length) = _lengthEncoder.GetBuffer();
                stream.Write(buffer, 0, length);

                return stream.ToArray();
            }
        }

        public (byte[] buffer, int length) GetBuffer()
        {
            throw new NotSupportedException($"{nameof(StringEncoder)} doesn't use temporary byte buffers");
        }

        protected virtual void Dispose(bool disposing)
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

        [Conditional("DEBUG")]
        protected void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().ToString());
            }
        }
    }
}
