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
    /// <seealso cref="AbstractStreamDecoder{T}"/>
    internal abstract class AbstractStreamEncoder<T> : IEncoder<T>
    {
        protected AbstractStreamEncoder()
        {
            Stream = new MemoryStream();
        }

        protected MemoryStream Stream { get; private set; }
        protected bool Disposed { get; private set; }

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
            return Stream.ToArray();
        }

        /// <inheritdoc/>
        public virtual (byte[] buffer, int length) GetBuffer()
        {
            Flush();
            return (Stream.GetBuffer(), (int)Stream.Length);
        }

        protected virtual void Flush()
        {
            CheckDisposed();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing)
                {
                    Stream?.Dispose();
                }

                Stream = null;
                Disposed = true;
            }
        }

        [Conditional("DEBUG")]
        protected void CheckDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(GetType().ToString());
            }
        }
    }
}
