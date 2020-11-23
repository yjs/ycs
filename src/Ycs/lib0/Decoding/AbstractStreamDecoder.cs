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
    /// <seealso cref="AbstractStreamEncoder{T}"/>
    internal abstract class AbstractStreamDecoder<T> : IDecoder<T>
    {
        private readonly bool _leaveOpen;

        protected AbstractStreamDecoder(Stream input, bool leaveOpen = false)
        {
            Debug.Assert(input != null);

            Stream = input;
            _leaveOpen = leaveOpen;
        }

        protected Stream Stream { get; private set; }
        protected bool Disposed { get; private set; }

        protected bool HasContent => Stream.Position < Stream.Length;

        /// <inheritdoc/>
        public abstract T Read();

        public void Dispose()
        {
            Dispose(disposing: true);
            System.GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed)
            {
                if (disposing && !_leaveOpen)
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
