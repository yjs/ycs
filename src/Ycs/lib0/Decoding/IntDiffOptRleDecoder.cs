// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.IO;

namespace Ycs
{
    /// <seealso cref="IntDiffOptRleEncoder"/>
    internal class IntDiffOptRleDecoder : AbstractStreamDecoder<long>
    {
        private long _state;
        private uint _count;
        private long _diff;

        public IntDiffOptRleDecoder(Stream input, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            // Do nothing.
        }

        /// <inheritdoc/>
        public override long Read()
        {
            CheckDisposed();

            if (_count == 0)
            {
                var diff = Stream.ReadVarInt().Value;

                // If the first bit is set, we read more data.
                bool hasCount = (diff & Bit.Bit1) > 0;

                if (diff < 0)
                {
                    _diff = -((-diff) >> 1);
                }
                else
                {
                    _diff = diff >> 1;
                }

                _count = hasCount ? Stream.ReadVarUint() + 2 : 1;
            }

            _state += _diff;
            _count--;
            return _state;
        }
    }
}
