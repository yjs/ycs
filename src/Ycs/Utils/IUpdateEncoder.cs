// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.IO;

namespace Ycs
{
    internal interface IDSEncoder : IDisposable
    {
        Stream RestWriter { get; }

        byte[] ToArray();

        /// <summary>
        /// Resets the ds value to 0.
        /// The v2 encoder uses this information to reset the initial diff value.
        /// </summary>
        void ResetDsCurVal();

        void WriteDsClock(long clock);
        void WriteDsLength(long length);
    }

    internal interface IUpdateEncoder : IDSEncoder
    {
        void WriteLeftId(ID id);
        void WriteRightId(ID id);

        /// <summary>
        /// NOTE: Use 'writeClient' and 'writeClock' instead of writeID if possible.
        /// </summary>
        void WriteClient(long client);

        void WriteInfo(byte info);
        void WriteString(string s);
        void WriteParentInfo(bool isYKey);
        void WriteTypeRef(uint info);

        /// <summary>
        /// Write len of a struct - well suited for Opt RLE encoder.
        /// </summary>
        void WriteLength(int len);

        void WriteAny(object any);
        void WriteBuffer(byte[] buf);
        void WriteKey(string key);
        void WriteJson<T>(T any);
    }
}
