// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.IO;
using System.Text;

namespace Ycs
{
    internal sealed class StringDecoder : IDecoder<string>
    {
        private UintOptRleDecoder _decoder;
        private string _value;
        private int _pos;
        private bool _disposed;

        public StringDecoder(Stream input, bool leaveOpen = false)
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            _value = reader.ReadVarString();

            // Allow the rest data to be read.
            _decoder = new UintOptRleDecoder(input, leaveOpen);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        public string Read()
        {
            int length = (int)_decoder.Read();
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

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _decoder.Dispose();
                }

                _decoder = null;
                _disposed = true;
            }
        }
    }
}
