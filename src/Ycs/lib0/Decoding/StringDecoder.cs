// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;

namespace Ycs
{
    /// <seealso cref="StringEncoder"/>
    internal class StringDecoder : IDecoder<string>
    {
        private UintOptRleDecoder _lengthDecoder;
        private string _value;
        private int _pos;
        private bool _disposed;

        public StringDecoder(Stream input, bool leaveOpen = false)
        {
            Debug.Assert(input != null);

            _value = input.ReadVarString();
            _lengthDecoder = new UintOptRleDecoder(input, leaveOpen);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public string Read()
        {
            CheckDisposed();

            var length = (int)_lengthDecoder.Read();
            if (length == 0)
            {
                return string.Empty;
            }

            var result = _value.Substring(_pos, length);
            _pos += length;

            // No need to keep the string in memory anymore.
            // This also covers the case when nothing but empty strings are left.
            if (_pos >= _value.Length)
            {
                _value = null;
            }

            return result;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _lengthDecoder?.Dispose();
                }

                _lengthDecoder = null;
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
