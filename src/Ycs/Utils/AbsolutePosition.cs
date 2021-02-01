// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Diagnostics;

namespace Ycs
{
    internal class AbsolutePosition
    {
        public readonly AbstractType Type;
        public readonly int Index;
        public readonly int Assoc;

        public AbsolutePosition(AbstractType type, int index, int assoc = 0)
        {
            Type = type;
            Index = index;
            Assoc = assoc;
        }

        public static AbsolutePosition TryCreateFromRelativePosition(RelativePosition rpos, YDoc doc)
        {
            var store = doc.Store;
            var rightId = rpos.Item;
            var typeId = rpos.TypeId;
            var tName = rpos.TName;
            var assoc = rpos.Assoc;
            int index = 0;
            AbstractType type;

            if (rightId != null)
            {
                if (store.GetState(rightId.Value.Client) <= rightId.Value.Clock)
                {
                    return null;
                }

                var res = store.FollowRedone(rightId.Value);
                var right = res.item as Item;
                if (right == null)
                {
                    return null;
                }

                type = right.Parent as AbstractType;
                Debug.Assert(type != null);

                if (type._item == null || !type._item.Deleted)
                {
                    // Adjust position based on the left assotiation, if necessary.
                    index = (right.Deleted || !right.Countable) ? 0 : (res.diff + (assoc >= 0 ? 0 : 1));
                    var n = right.Left as Item;
                    while (n != null)
                    {
                        if (!n.Deleted && n.Countable)
                        {
                            index += n.Length;
                        }

                        n = n.Left as Item;
                    }
                }
            }
            else
            {
                if (tName != null)
                {
                    type = doc.Get<AbstractType>(tName);
                }
                else if (typeId != null)
                {
                    if (store.GetState(typeId.Value.Client) <= typeId.Value.Clock)
                    {
                        // Type does not exist yet.
                        return null;
                    }

                    var item = store.FollowRedone(typeId.Value).item as Item;
                    if (item != null && item.Content is ContentType)
                    {
                        type = (item.Content as ContentType).Type;
                    }
                    else
                    {
                        // Struct is garbage collected.
                        return null;
                    }
                }
                else
                {
                    throw new Exception();
                }

                index = assoc >= 0 ? type.Length : 0;
            }

            return new AbsolutePosition(type, index, assoc);
        }
    }
}
