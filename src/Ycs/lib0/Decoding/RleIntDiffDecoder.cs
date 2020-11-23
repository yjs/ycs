﻿// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;
using System.IO;

namespace Ycs
{
    /// <seealso cref="RleIntDiffEncoder"/>
    internal class RleIntDiffDecoder : AbstractStreamDecoder<int>
    {
        private int _state;
        private int _count;

        public RleIntDiffDecoder(Stream input, int start, bool leaveOpen = false)
            : base(input, leaveOpen)
        {
            _state = start;
        }

        /// <inheritdoc/>
        public override int Read()
        {
            CheckDisposed();

            if (_count == 0)
            {
                _state += Stream.ReadVarInt().Value;

                if (HasContent)
                {
                    // See encoder implementation for the reason why this is incremented.
                    _count = (int)Stream.ReadVarUint() + 1;
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
