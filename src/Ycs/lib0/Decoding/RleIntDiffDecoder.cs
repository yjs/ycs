// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;
using System.IO;

namespace Ycs
{
    /// <seealso cref="RleIntDiffEncoder"/>
    internal class RleIntDiffDecoder : AbstractStreamDecoder<long>
    {
        private long _state;
        private long _count;

        public RleIntDiffDecoder(Stream input, long start, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _state = start;
        }

        /// <inheritdoc/>
        public override long Read()
        {
            CheckDisposed();

            if (_count == 0)
            {
                _state += Stream.ReadVarInt().Value;

                if (HasContent)
                {
                    // See encoder implementation for the reason why this is incremented.
                    _count = Stream.ReadVarUint() + 1;
                    Debug.Assert(_count > 0);
                }
                else
                {
                    // Read the current value forever.
                    _count = -1;
                }
            }

            _count--;
            return _state;
        }
    }
}
