// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace Ycs
{
    public abstract class AbstractStreamDecoder<T> : IDecoder<T>
    {
        private Stream _stream;
        private bool _disposed;

        protected AbstractStreamDecoder(Stream input, bool leaveOpen = false)
        {
            _stream = input;
            Reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: leaveOpen);
        }

        protected BinaryReader Reader { get; private set; }

        protected bool HasContent => _stream.Position < _stream.Length;

        /// <inheritdoc/>
        public abstract T Read();

        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Reader.Dispose();
                    _stream.Dispose();
                }

                Reader = null;
                _stream = null;
                _disposed = true;
            }
        }
    }
}
