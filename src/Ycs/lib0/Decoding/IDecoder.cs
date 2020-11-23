// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;

namespace Ycs
{
    /// <seealso cref="IEncoder{T}"/>
    internal interface IDecoder<T> : IDisposable
    {
        /// <summary>
        /// Reads the next element from the underlying data stream.
        /// </summary>
        T Read();
    }
}
