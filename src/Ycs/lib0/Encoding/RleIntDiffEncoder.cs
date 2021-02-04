// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

namespace Ycs
{
    /// <summary>
    /// A combination of <see cref="IntDiffEncoder"/> and <see cref="RleEncoder"/>.
    /// <br/>
    /// Basically first writes the <see cref="IntDiffEncoder"/> and then counts duplicate
    /// diffs using the <see cref="RleEncoder"/>.
    /// <br/>
    /// Encodes values <c>[1, 1, 1, 2, 3, 4, 5, 6]</c> as <c>[1, 1, 0, 2, 1, 5]</c>.
    /// </summary>
    /// <seealso cref="RleIntDiffDecoder"/>
    internal sealed class RleIntDiffEncoder : AbstractStreamEncoder<long>
    {
        private long _state;
        private uint _count;

        public RleIntDiffEncoder(long start)
        {
            _state = start;
        }

        /// <inheritdoc/>
        public override void Write(long value)
        {
            CheckDisposed();

            if (_state == value && _count > 0)
            {
                _count++;
            }
            else
            {
                if (_count > 0)
                {
                    Stream.WriteVarUint(_count - 1);
                }

                Stream.WriteVarInt(value - _state);

                _count = 1;
                _state = value;
            }
        }
    }
}
