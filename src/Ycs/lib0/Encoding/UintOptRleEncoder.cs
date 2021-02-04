// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

namespace Ycs
{
    /// <summary>
    /// Optimized RLE encoder that does not suffer from the mentioned problem of the basic RLE encoder.
    /// Internally uses VarInt encoder to write unsigned integers.
    /// If the input occurs multiple times, we write it as a negative number. The <see cref="UintOptRleDecoder"/>
    /// then understands that it needs to read a count.
    /// </summary>
    /// <seealso cref="UintOptRleDecoder"/>
    internal class UintOptRleEncoder : AbstractStreamEncoder<uint>
    {
        private uint _state;
        private uint _count;

        public UintOptRleEncoder()
        {
            // Do nothing.
        }

        /// <inheritdoc/>
        public override void Write(uint value)
        {
            CheckDisposed();

            if (_state == value)
            {
                _count++;
            }
            else
            {
                WriteEncodedValue();

                _count = 1;
                _state = value;
            }
        }

        protected override void Flush()
        {
            WriteEncodedValue();
            base.Flush();
        }

        private void WriteEncodedValue()
        {
            if (_count > 0)
            {
                // Flush counter, unless this is the first value (count = 0).
                // Case 1: Just a single value. Set sign to positive.
                // Case 2: Write several values. Set sign to negative to indicate that there is a length coming.
                if (_count == 1)
                {
                    Stream.WriteVarInt(_state);
                }
                else
                {
                    // Specify 'treatZeroAsNegative' in case we pass the '-0'.
                    Stream.WriteVarInt(-_state, treatZeroAsNegative: _state == 0);

                    // Since count is always >1, we can decrement by one. Non-standard encoding.
                    Stream.WriteVarUint(_count - 2);
                }
            }
        }
    }
}
