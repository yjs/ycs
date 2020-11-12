// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ycs
{
    public class ContentDeleted : IContent
    {
        internal const int _ref = 1;

        public ContentDeleted(int length)
        {
            Length = length;
        }

        public int Ref => _ref;
        public bool Countable => false;

        public int Length { get; private set; }

        public IReadOnlyList<object> GetContent() => throw new NotImplementedException();

        public IContent Copy() => new ContentDeleted(Length);

        public IContent Splice(int offset)
        {
            var right = new ContentDeleted(Length - offset);
            Length = offset;
            return right;
        }

        public bool MergeWith(IContent right)
        {
            Debug.Assert(right is ContentDeleted);
            Length += right.Length;
            return true;
        }

        public void Integrate(Transaction transaction, Item item)
        {
            transaction.DeleteSet.Add(item.Id.Client, item.Id.Clock, Length);
            item.MarkDeleted();
        }

        public void Delete(Transaction transaction)
        {
            // Do nothing.
        }

        public void Gc(StructStore store)
        {
            // Do nothing.
        }

        public void Write(IUpdateEncoder encoder, int offset)
        {
            encoder.WriteLength(Length - offset);
        }

        public static ContentDeleted Read(IUpdateDecoder decoder)
        {
            var length = decoder.ReadLength();
            return new ContentDeleted(length);
        }
    }
}
