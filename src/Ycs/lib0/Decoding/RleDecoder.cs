// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;
using System.IO;

namespace Ycs
{
    /// <seealso cref="RleEncoder"/>
    internal class RleDecoder : AbstractStreamDecoder<byte>
    {
        private byte _state;
        private long _count;

        public RleDecoder(Stream input, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            // Do nothing.
        }

        /// <inheritdoc/>
        public override byte Read()
        {
            CheckDisposed();

            if (_count == 0)
            {
                _state = Stream._ReadByte();

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
