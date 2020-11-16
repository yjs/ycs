// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

namespace Ycs
{
    /// <summary>
    /// Basic Run Length Encoder - a basic compression implementation.
    /// Encodes <c>[1, 1, 1, 7]</c> to <c>[1, 3, 7, 1]</c> (3 times '1', 1 time '7').
    /// <br/>
    /// This encoder might do more harm than good if there are a lot of values that are not repeated.
    /// <br/>
    /// It was originally used for image compression.
    /// </summary>
    internal sealed class RleEncoder : AbstractStreamEncoder<byte>
    {
        private byte? _state = null;
        private uint _count = 0;

        public override void Write(byte value)
        {
            if (_state == value)
            {
                _count++;
            }
            else
            {
                if (_count > 0)
                {
                    // Flush counter, unless this is the first value (count = 0).
                    // Since 'count' is always >0, we can decrement by one. Non-standard encoding.
                    Writer.WriteVarUint(_count - 1);
                }

                Writer.Write(value);

                _count = 1;
                _state = value;
            }
        }
    }
}
