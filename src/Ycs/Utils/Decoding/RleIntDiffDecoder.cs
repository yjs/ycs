// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;
using System.IO;

namespace Ycs
{
    public sealed class RleIntDiffDecoder : AbstractStreamDecoder<int>
    {
        public int _state;
        public int _count;

        public RleIntDiffDecoder(Stream input, int start, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _state = start;
        }

        public override int Read()
        {
            if (_count == 0)
            {
                _state += Reader.ReadVarInt().value;

                if (HasContent)
                {
                    // See encoder implementation for the reason why this is incremented.
                    _count = (int)Reader.ReadVarUint() + 1;
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
