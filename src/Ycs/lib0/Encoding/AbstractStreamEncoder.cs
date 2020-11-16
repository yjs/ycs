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
    internal abstract class AbstractStreamEncoder<T> : IEncoder<T>, IDisposable
    {
        private MemoryStream _stream;
        private bool _disposed;

        public AbstractStreamEncoder()
        {
            _stream = new MemoryStream();
            Writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: false);
        }

        protected BinaryWriter Writer { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public abstract void Write(T value);

        /// <inheritdoc/>
        public virtual byte[] ToArray()
        {
            Flush();
            return _stream.ToArray();
        }

        protected virtual void Flush()
        {
            Writer.Flush();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Writer.Dispose();
                    _stream.Dispose();
                }

                Writer = null;
                _stream = null;

                _disposed = true;
            }
        }
    }
}
