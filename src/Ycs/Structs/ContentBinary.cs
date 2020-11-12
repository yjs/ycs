// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class ContentBinary : IContent
    {
        internal const int _ref = 3;

        private readonly byte[] _content;

        public ContentBinary(byte[] data)
        {
            _content = data;
        }

        public int Ref => _ref;
        public bool Countable => true;
        public int Length => 1;

        public IReadOnlyList<object> GetContent() => new object[] { _content };

        public IContent Copy() => new ContentBinary(_content);

        public IContent Splice(int offset)
        {
            throw new NotImplementedException();
        }

        public bool MergeWith(IContent right)
        {
            return false;
        }

        public void Integrate(Transaction transaction, Item item)
        {
            // Do nothing.
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
            encoder.WriteBuffer(_content);
        }

        public static ContentBinary Read(IUpdateDecoder decoder)
        {
            var content = decoder.ReadBuffer();
            return new ContentBinary(content);
        }
    }
}
