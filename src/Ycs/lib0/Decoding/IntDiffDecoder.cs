// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.IO;

namespace Ycs
{
    /// <seealso cref="IntDiffEncoder"/>
    internal class IntDiffDecoder : AbstractStreamDecoder<long>
    {
        private long _state;

        public IntDiffDecoder(Stream input, long start, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _state = start;
        }

        /// <inheritdoc/>
        public override long Read()
        {
            CheckDisposed();

            _state += Stream.ReadVarInt().Value;
            return _state;
        }
    }
}
