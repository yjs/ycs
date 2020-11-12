// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class ContentEmbed : IContent
    {
        internal const int _ref = 5;

        public readonly object Embed;

        public ContentEmbed(object embed)
        {
            Embed = embed;
        }

        public int Ref => _ref;
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
            encoder.WriteJson(Embed);
        }

        public static ContentEmbed Read(IUpdateDecoder decoder)
        {
            var content = decoder.ReadJson();
            return new ContentEmbed(content);
        }
    }
}
