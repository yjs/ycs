// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.IO;

namespace Ycs
{
    internal interface IDSDecoder : IDisposable
    {
        Stream Reader { get; }

        void ResetDsCurVal();
        long ReadDsClock();
        long ReadDsLength();
    }

    internal interface IUpdateDecoder : IDSDecoder
    {
        ID ReadLeftId();
        ID ReadRightId();
        long ReadClient();
        byte ReadInfo();
        string ReadString();
        bool ReadParentInfo();
        uint ReadTypeRef();
        int ReadLength();
        object ReadAny();
        byte[] ReadBuffer();
        string ReadKey();
        object ReadJson();
    }
}
