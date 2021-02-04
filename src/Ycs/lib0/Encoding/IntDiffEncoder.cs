// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

namespace Ycs
{
    /// <summary>
    /// Basic diff encoder using variable length encoding.
    /// Encodes the values <c>[3, 1100, 1101, 1050, 0]</c> to <c>[3, 1097, 1, -51, -1050]</c>.
    /// </summary>
    /// <seealso cref="IntDiffDecoder"/>
    internal class IntDiffEncoder : AbstractStreamEncoder<long>
    {
        private long _state;

        public IntDiffEncoder(long start)
        {
            _state = start;
        }

        /// <inheritdoc/>
        public override void Write(long value)
        {
            CheckDisposed();

            Stream.WriteVarInt(value - _state);
            _state = value;
        }
    }
}
