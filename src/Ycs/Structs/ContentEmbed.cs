// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class ContentEmbed : IContentEx
    {
        internal const int _ref = 5;

        public readonly object Embed;

        internal ContentEmbed(object embed)
        {
            Embed = embed;
        }

        int IContentEx.Ref => _ref;

        public bool Countable => true;
        public int Length => 1;

        public IReadOnlyList<object> GetContent() => new object[] { Embed };

        public IContent Copy() => new ContentEmbed(Embed);

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
            encoder.WriteJson(Embed);
        }

        internal static ContentEmbed Read(IUpdateDecoder decoder)
        {
            var content = decoder.ReadJson();
            return new ContentEmbed(content);
        }
    }
}
