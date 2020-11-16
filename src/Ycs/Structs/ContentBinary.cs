// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class ContentBinary : IContentEx
    {
        internal const int _ref = 3;

        private readonly byte[] _content;

        internal ContentBinary(byte[] data)
        {
            _content = data;
        }

        int IContentEx.Ref => _ref;

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

        void IContentEx.Integrate(Transaction transaction, Item item)
        {
            // Do nothing.
        }

        void IContentEx.Delete(Transaction transaction)
        {
            // Do nothing.
        }

        void IContentEx.Gc(StructStore store)
        {
            // Do nothing.
        }

        void IContentEx.Write(IUpdateEncoder encoder, int offset)
        {
            encoder.WriteBuffer(_content);
        }

        internal static ContentBinary Read(IUpdateDecoder decoder)
        {
            var content = decoder.ReadBuffer();
            return new ContentBinary(content);
        }
    }
}
