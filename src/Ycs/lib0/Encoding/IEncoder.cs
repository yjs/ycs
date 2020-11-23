// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;

namespace Ycs
{
    /// <seealso cref="IDecoder{T}"/>
    internal interface IEncoder<T> : IDisposable
    {
        void Write(T value);

        /// <summary>
        /// Returns a copy of the encode contents.
        /// </summary>
        byte[] ToArray();

        /// <summary>
        /// Returns the current raw buffer of the encoder.
        /// This buffer is valid only until the encoder is not disposed.
        /// </summary>
        /// <returns></returns>
        (byte[] buffer, int length) GetBuffer();
    }
}
