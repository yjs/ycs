// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.IO;

namespace Ycs
{
    internal sealed class IntDiffDecoder : AbstractStreamDecoder<int>
    {
        private int _state;

        public IntDiffDecoder(Stream input, int start, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _state = start;
        }

        public override int Read()
        {
            _state += Reader.ReadVarInt().Value;
            return _state;
        }
    }
}
