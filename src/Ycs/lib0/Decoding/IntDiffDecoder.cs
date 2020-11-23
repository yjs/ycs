// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.IO;

namespace Ycs
{
    /// <seealso cref="IntDiffEncoder"/>
    internal class IntDiffDecoder : AbstractStreamDecoder<int>
    {
        private int _state;

        public IntDiffDecoder(Stream input, int start, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _state = start;
        }

        /// <inheritdoc/>
        public override int Read()
        {
            CheckDisposed();

            _state += Stream.ReadVarInt().Value;
            return _state;
        }
    }
}
