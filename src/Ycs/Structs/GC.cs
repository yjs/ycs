// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;

namespace Ycs
{
    public class GC : AbstractStruct
    {
        public const byte StructGCRefNumber = 0;

        public GC(ID id, int length)
            : base(id, length)
        {
            // Do nothing.
        }

        public override bool Deleted => true;

        public override bool MergeWith(AbstractStruct right)
        {
            Debug.Assert(right is GC);
            Length += right.Length;
            return true;
        }

        public override void Delete(Transaction transaction)
        {
            // Do nothing.
        }

        public override void Integrate(Transaction transaction, int offset)
        {
            if (offset > 0)
            {
                Id = new ID(Id.Client, Id.Clock + offset);
                Length -= offset;
            }

            transaction.Doc.Store.AddStruct(this);
        }

        public override int? GetMissing(Transaction transaction, StructStore store)
        {
            return null;
        }

        public override void Write(IUpdateEncoder encoder, int offset)
        {
            encoder.WriteInfo(StructGCRefNumber);
            encoder.WriteLength(Length - offset);
        }
    }
}
