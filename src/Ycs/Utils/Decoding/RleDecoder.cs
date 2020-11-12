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
    public sealed class RleDecoder : AbstractStreamDecoder<byte>
    {
        private byte _state;
        private int _count;

        public RleDecoder(Stream input, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            // Do nothing.
        }

        public override byte Read()
        {
            if (_count == 0)
            {
                _state = Reader.ReadByte();

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
