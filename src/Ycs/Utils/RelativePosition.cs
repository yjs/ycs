// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;

namespace Ycs
{
    /// <summary>
    /// A relative position is based on the YUjs model and is not affected by document changes.
    /// E.g. if you place a relative position before a certain character, it will always point to this character.
    /// If you place a relative position at the end of a type, it will always point to the end of the type.
    /// <br/>
    /// A numberic position is often unsuited for user selections, because it does not change when content is inserted
    /// before or after.
    /// <br/>
    /// <c>Insert(0, 'x')('a|bc') = 'xa|bc'</c> Where | is tehre relative position.
    /// <br/>
    /// Only one property must be defined.
    /// </summary>
    public class RelativePosition : IEquatable<RelativePosition>
    {
        public readonly ID? Item;
        public readonly ID? TypeId;
        public readonly string TName;

        public RelativePosition(AbstractType type, ID? item)
        {
            Item = item;

            if (type._item == null)
            {
                TName = type.FindRootTypeKey();
            }
            else
            {
                TypeId = new ID(type._item.Id.Client, type._item.Id.Clock);
            }
        }

        /*
        public RelativePosition(dynamic json)
        {
            TypeId = json.type == null ? (ID?)null : new ID((int)json.type.client, (int)json.type.clock);
            TName = json.tname ?? null;
            Item = json.item == null ? (ID?)null : new ID((int)json.item.client, (int)json.item.clock);
        }
        */

        private RelativePosition(ID? typeId, string tname, ID? item)
        {
            TypeId = typeId;
            TName = tname;
            Item = item;
        }

        public bool Equals(RelativePosition other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other != null
                && string.Equals(TName, other.TName)
                && ID.Equals(Item, other.Item)
                && ID.Equals(TypeId, other.TypeId);
        }

        /// <summary>
        /// Create a relative position based on an absolute position.
        /// </summary>
        public static RelativePosition FromTypeIndex(AbstractType type, int index)
        {
            var t = type._start;
            while (t != null)
            {
                if (!t.Deleted && t.Countable)
                {
                    if (t.Length > index)
                    {
                        // Case 1: found position somewhere in the linked list.
                        return new RelativePosition(type, new ID(t.Id.Client, t.Id.Clock + index));
                    }

                    index -= t.Length;
                }

                t = t.Right as Item;
            }

            return new RelativePosition(type, null);
        }

        public void Write(BinaryWriter encoder)
        {
            if (Item != null)
            {
                // Case 1: Found position somewhere in the linked list.
                encoder.WriteVarUint(0);
                Item.Value.Write(encoder);
            }
            else if (TName != null)
            {
                // Case 2: Found position at the end of the list and type is stored in y.share.
                encoder.WriteVarUint(1);
                encoder.WriteVarString(TName);
            }
            else if (TypeId != null)
            {
                // Case 3: Found position at the end of the list and type is attached to an item.
                encoder.WriteVarUint(2);
                TypeId.Value.Write(encoder);
            }
            else
            {
                throw new Exception();
            }
        }

        public static RelativePosition Read(BinaryReader reader)
        {
            switch (reader.ReadVarUint())
            {
                case 0:
                    // Case 1: Found position somewhere in the linked list.
                    var itemId = ID.Read(reader);
                    return new RelativePosition(null, null, itemId);
                case 1:
                    // Case 2: Found position at the end of the list and type is stored in y.share.
                    var tName = reader.ReadVarString();
                    return new RelativePosition(null, tName, null);
                case 2:
                    // Case 3: Found position at the end of the list and type is attached to an item.
                    var typeId = ID.Read(reader);
                    return new RelativePosition(typeId, null, null);
                default:
                    throw new Exception();
            }
        }

        public byte[] ToArray()
        {
            using var encoder = new DSEncoderV2();
            Write(encoder.RestWriter);
            return encoder.ToArray();
        }

        public static RelativePosition FromStream(Stream input)
        {
            using var decoder = new DSDecoderV2(input);
            return Read(decoder.Reader);
        }
    }

    public class AbsolutePosition
    {
        public readonly AbstractType Type;
        public readonly int Index;

        public AbsolutePosition(AbstractType type, int index)
        {
            Type = type;
            Index = index;
        }

        public static AbsolutePosition TryCreateFromAbsolutePosition(RelativePosition rpos, YDoc doc)
        {
            var store = doc.Store;
            var rightId = rpos.Item;
            var typeId = rpos.TypeId;
            var tName = rpos.TName;
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
                    index = right.Deleted || !right.Countable ? 0 : res.diff;
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

                index = type.Length;
            }

            return new AbsolutePosition(type, index);
        }
    }
}
