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
    internal sealed class IntDiffEncoder : AbstractStreamEncoder<int>
    {
        private int _state;

        public IntDiffEncoder(int start)
        {
            _state = start;
        }

        public override void Write(int value)
        {
            Writer.WriteVarInt(value - _state);
            _state = value;
        }
    }
}
