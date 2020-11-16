// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.IO;

namespace Ycs
{
    internal sealed class IncUintOptRleDecoder : AbstractStreamDecoder<uint>
    {
        private uint _state;
        private uint _count;

        public IncUintOptRleDecoder(Stream input, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            // Do nothing.
        }

        public override uint Read()
        {
            if (_count == 0)
            {
                var v = Reader.ReadVarInt();

                // If the sign is negative, we read the count too; otherwise. count is 1.
                bool isNegative = v.Sign < 0;
                if (isNegative)
                {
                    _state = (uint)(-v.Value);
                    _count = Reader.ReadVarUint() + 2;
                }
                else
                {
                    _state = (uint)v.Value;
                    _count = 1;
                }
            }

            _count--;
            return _state++;
        }
    }
}
