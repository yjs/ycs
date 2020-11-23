// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Ycs
{
    public interface IContent
    {
        bool Countable { get; }
        int Length { get; }
        IReadOnlyList<object> GetContent();
        IContent Copy();
        IContent Splice(int offset);
        bool MergeWith(IContent right);
    }

    internal interface IContentEx : IContent
    {
        int Ref { get; }

        void Integrate(Transaction transaction, Item item);
        void Delete(Transaction transaction);
        void Gc(StructStore store);
        void Write(IUpdateEncoder encoder, int offset);
    }
}
