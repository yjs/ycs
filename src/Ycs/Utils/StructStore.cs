// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Ycs
{
    internal sealed class StructStore
    {
        private class PendingClientStructRef
        {
            public int NextReadOperation { get; set; }
            public List<AbstractStruct> Refs { get; set; } = new List<AbstractStruct>(1);
        }

        // TODO: [alekseyk] To private?
        public readonly IDictionary<long, List<AbstractStruct>> Clients = new Dictionary<long, List<AbstractStruct>>();

        /// <summary>
        /// Store incompleted struct reads here.
        /// </summary>
        private readonly IDictionary<long, PendingClientStructRef> _pendingClientStructRefs = new Dictionary<long, PendingClientStructRef>();

        /// <summary>
        /// Stack of pending structs waiting for struct dependencies.
        /// </summary>
        private readonly Stack<AbstractStruct> _pendingStack = new Stack<AbstractStruct>();

        private readonly IList<DSDecoderV2> _pendingDeleteReaders = new List<DSDecoderV2>();

        /// <summary>
        /// Return the states as a Map<int,int>.
        /// Note that clock refers to the next expected clock id.
        /// </summary>
        public IDictionary<long, long> GetStateVector()
        {
            var result = new Dictionary<long, long>(Clients.Count);

            foreach (var kvp in Clients)
            {
                var str = kvp.Value[kvp.Value.Count - 1];
                result[kvp.Key] = str.Id.Clock + str.Length;
            }

            return result;
        }

        public long GetState(long clientId)
        {
            if (Clients.TryGetValue(clientId, out var structs))
            {
                var lastStruct = structs[structs.Count - 1];
                return lastStruct.Id.Clock + lastStruct.Length;
            }

            return 0;
        }

        public void IntegrityCheck()
        {
            foreach (var structs in Clients.Values)
            {
                if (structs.Count == 0)
                {
                    throw new Exception($"{nameof(StructStore)} failed integrity check: no structs for client");
                }

                for (int i = 1; i < structs.Count; i++)
                {
                    var left = structs[i - 1];
                    var right = structs[i];

                    if (left.Id.Clock + left.Length != right.Id.Clock)
                    {
                        throw new Exception($"{nameof(StructStore)} failed integrity check: missing struct");
                    }
                }
            }

            if (_pendingDeleteReaders.Count != 0 || _pendingStack.Count != 0 || _pendingClientStructRefs.Count != 0)
            {
                throw new Exception($"{nameof(StructStore)} failed integrity check: still have pending items");
            }
        }

        public void CleanupPendingStructs()
        {
            var clientsToRemove = new List<long>();

            // Cleanup pendingCLientsStructs if not fully finished.
            foreach (var kvp in _pendingClientStructRefs)
            {
                var client = kvp.Key;
                var refs = kvp.Value;

                if (refs.NextReadOperation == refs.Refs.Count)
                {
                    clientsToRemove.Add(client);
                }
                else
                {
                    refs.Refs.RemoveRange(0, refs.NextReadOperation);
                    refs.NextReadOperation = 0;
                }
            }

            if (clientsToRemove.Count > 0)
            {
                foreach (var key in clientsToRemove)
                {
                    _pendingClientStructRefs.Remove(key);
                }
            }
        }

        public void AddStruct(AbstractStruct str)
        {
            if (!Clients.TryGetValue(str.Id.Client, out var structs))
            {
                structs = new List<AbstractStruct>();
                Clients[str.Id.Client] = structs;
            }
            else
            {
                var lastStruct = structs[structs.Count - 1];
                if (lastStruct.Id.Clock + lastStruct.Length != str.Id.Clock)
                {
                    throw new Exception("Unexpected");
                }
            }

            structs.Add(str);
        }

        /// <summary>
        /// Perform a binary search on a sorted array.
        /// </summary>
        // TODO: [alekseyk] IList<AbstractStruct> to custom class, and move this method there?
        public static int FindIndexSS(IList<AbstractStruct> structs, long clock)
        {
            Debug.Assert(structs.Count > 0);

            var left = 0;
            var right = structs.Count - 1;
            var mid = structs[right];
            var midClock = mid.Id.Clock;

            if (midClock == clock)
            {
                return right;
            }

            // @todo does it even make sense to pivot the search?
            // If a good split misses, it might actually increase the time to find the correct item.
            // Currently, the only advantage is that search with pivoting might find the item on the first try.
            int midIndex = (int)((clock * right) / (midClock + mid.Length - 1));
            while (left <= right)
            {
                mid = structs[midIndex];
                midClock = mid.Id.Clock;

                if (midClock <= clock)
                {
                    if (clock < midClock + mid.Length)
                    {
                        return midIndex;
                    }

                    left = midIndex + 1;
                }
                else
                {
                    right = midIndex - 1;
                }

                midIndex = (left + right) / 2;
            }

            // Always check state before looking for a struct in StructStore
            // Therefore the case of not finding a struct is unexpected.
            throw new Exception("Unexpected");
        }

        /// <summary>
        /// Expects that id is actually in store. This function throws or is an infinite loop otherwise.
        /// </summary>
        public AbstractStruct Find(ID id)
        {
            if (!Clients.TryGetValue(id.Client, out var structs))
            {
                throw new Exception($"No structs for client: {id.Client}");
            }

            int index = FindIndexSS(structs, id.Clock);
            if (index < 0 || index >= structs.Count)
            {
                throw new Exception($"Invalid struct index: {index}, max: {structs.Count}");
            }

            return structs[index];
        }

        public int FindIndexCleanStart(Transaction transaction, List<AbstractStruct> structs, long clock)
        {
            int index = FindIndexSS(structs, clock);
            var str = structs[index];
            if (str.Id.Clock < clock && str is Item item)
            {
                structs.Insert(index + 1, item.SplitItem(transaction, (int)(clock - item.Id.Clock)));
                return index + 1;
            }

            return index;
        }

        public AbstractStruct GetItemCleanStart(Transaction transaction, ID id)
        {
            if (!Clients.TryGetValue(id.Client, out var structs))
            {
                throw new Exception();
            }

            int indexCleanStart = FindIndexCleanStart(transaction, structs, id.Clock);
            Debug.Assert(indexCleanStart >= 0 && indexCleanStart < structs.Count);
            return structs[indexCleanStart];
        }

        public AbstractStruct GetItemCleanEnd(Transaction transaction, ID id)
        {
            if (!Clients.TryGetValue(id.Client, out var structs))
            {
                throw new Exception();
            }

            int index = FindIndexSS(structs, id.Clock);
            var str = structs[index];

            if ((id.Clock != str.Id.Clock + str.Length - 1) && !(str is GC))
            {
                structs.Insert(index + 1, (str as Item).SplitItem(transaction, (int)(id.Clock - str.Id.Clock + 1)));
            }

            return str;
        }

        public void ReplaceStruct(AbstractStruct oldStruct, AbstractStruct newStruct)
        {
            if (!Clients.TryGetValue(oldStruct.Id.Client, out var structs))
            {
                throw new Exception();
            }

            int index = FindIndexSS(structs, oldStruct.Id.Clock);
            structs[index] = newStruct;
        }

        public void IterateStructs(Transaction transaction, List<AbstractStruct> structs, long clockStart, long length, Predicate<AbstractStruct> fun)
        {
            if (length <= 0)
            {
                return;
            }

            var clockEnd = clockStart + length;

            var index = FindIndexCleanStart(transaction, structs, clockStart);
            AbstractStruct str;

            do
            {
                str = structs[index];

                if (clockEnd < str.Id.Clock + str.Length)
                {
                    FindIndexCleanStart(transaction, structs, clockEnd);
                }

                if (!fun(str))
                {
                    break;
                }

                index++;
            } while (index < structs.Count && structs[index].Id.Clock < clockEnd);
        }

        public (AbstractStruct item, int diff) FollowRedone(ID id)
        {
            ID? nextId = id;
            int diff = 0;
            AbstractStruct item;

            do
            {
                if (diff > 0)
                {
                    nextId = new ID(nextId.Value.Client, nextId.Value.Clock + diff);
                }

                item = Find(nextId.Value);
                diff = (int)(nextId.Value.Clock - item.Id.Clock);
                nextId = (item as Item)?.Redone;
            } while (nextId != null && item is Item);

            return (item, diff);
        }

        public void ReadAndApplyDeleteSet(IDSDecoder decoder, Transaction transaction)
        {
            var unappliedDs = new DeleteSet();
            var numClients = decoder.Reader.ReadVarUint();

            for (int i = 0; i < numClients; i++)
            {
                decoder.ResetDsCurVal();

                var client = decoder.Reader.ReadVarUint();
                var numberOfDeletes = decoder.Reader.ReadVarUint();

                if (!Clients.TryGetValue(client, out var structs))
                {
                    structs = new List<AbstractStruct>();
                    // NOTE: Clients map is not updated.
                }

                var state = GetState(client);

                for (int deleteIndex = 0; deleteIndex < numberOfDeletes; deleteIndex++)
                {
                    var clock = decoder.ReadDsClock();
                    var clockEnd = clock + decoder.ReadDsLength();
                    if (clock < state)
                    {
                        if (state < clockEnd)
                        {
                            unappliedDs.Add(client, state, clockEnd - state);
                        }

                        var index = StructStore.FindIndexSS(structs, clock);

                        // We can ignore the case of GC and Delete structs, because we are going to skip them.
                        var str = structs[index];

                        // Split the first item if necessary.
                        if (!str.Deleted && str.Id.Clock < clock)
                        {
                            var splitItem = (str as Item).SplitItem(transaction, (int)(clock - str.Id.Clock));
                            structs.Insert(index + 1, splitItem);

                            // Increase, we now want to use the next struct.
                            index++;
                        }

                        while (index < structs.Count)
                        {
                            str = structs[index++];
                            if (str.Id.Clock < clockEnd)
                            {
                                if (!str.Deleted)
                                {
                                    if (clockEnd < str.Id.Clock + str.Length)
                                    {
                                        var splitItem = (str as Item).SplitItem(transaction, (int)(clockEnd - str.Id.Clock));
                                        structs.Insert(index, splitItem);
                                    }

                                    str.Delete(transaction);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        unappliedDs.Add(client, clock, clockEnd - clock);
                    }
                }
            }

            if (unappliedDs.Clients.Count > 0)
            {
                // @TODO: No need for encoding+decoding ds anymore.
                using (var unappliedDsEncoder = new DSEncoderV2())
                {
                    unappliedDs.Write(unappliedDsEncoder);
                    _pendingDeleteReaders.Add(new DSDecoderV2(new MemoryStream(unappliedDsEncoder.ToArray())));
                }
            }
        }

        internal void MergeReadStructsIntoPendingReads(IDictionary<long, List<AbstractStruct>> clientStructsRefs)
        {
            var pendingClientStructRefs = _pendingClientStructRefs;
            foreach (var kvp in clientStructsRefs)
            {
                var client = kvp.Key;
                var structRefs = kvp.Value;

                if (!pendingClientStructRefs.TryGetValue(client, out var pendingStructRefs))
                {
                    pendingClientStructRefs[client] = new PendingClientStructRef { Refs = structRefs };
                }
                else
                {
                    // Merge into existing structRefs.
                    if (pendingStructRefs.NextReadOperation > 0)
                    {
                        pendingStructRefs.Refs.RemoveRange(0, pendingStructRefs.NextReadOperation);
                    }

                    var merged = pendingStructRefs.Refs;
                    for (int i = 0; i < structRefs.Count; i++)
                    {
                        merged.Add(structRefs[i]);
                    }

                    merged.Sort((a, b) => a.Id.Clock.CompareTo(b.Id.Clock));

                    pendingStructRefs.NextReadOperation = 0;
                    pendingStructRefs.Refs = merged;
                }
            }
        }

        /// <summary>
        /// Resume computing structs generated by struct readers.
        /// <br/>
        /// While there is something to do, we integrate structs in this order:
        /// 1. Top element on stack, if stack is not empty.
        /// 2. Next element from current struct reader (if empty, use next struct reader).
        /// <br/>
        /// If struct causally depends on another struct (ref.missing), we put next reader of
        /// 'ref.id.client' on top of stack.
        /// <br/>
        /// At some point we find a struct that has no causal dependencies, then we start
        /// emptying the stack.
        /// <br/>
        /// It is not possible to have circles: i.e. struct1 (from client1) depends on struct2 (from client2)
        /// depends on struct3 (from client1). Therefore, the max stack size is equal to 'structReaders.length'.
        /// <br/>
        /// This method is implemented in a way so that we can resume computation if this update causally
        /// depends on another update.
        /// </summary>
        internal void ResumeStructIntegration(Transaction transaction)
        {
            // @todo: Don't forget to append stackhead at the end.
            var stack = _pendingStack;
            var clientsStructRefs = _pendingClientStructRefs;
            if (clientsStructRefs.Count == 0)
            {
                return;
            }

            // Sort them so taht we take the higher id first, in case of conflicts the lower id will probably not conflict with the id from the higher user.
            var clientsStructRefsIds = clientsStructRefs.Keys.ToList();
            clientsStructRefsIds.Sort();

            PendingClientStructRef getNextStructTarget()
            {
                var nextStructsTarget = clientsStructRefs[clientsStructRefsIds[clientsStructRefsIds.Count - 1]];

                while (nextStructsTarget.Refs.Count == nextStructsTarget.NextReadOperation)
                {
                    clientsStructRefsIds.RemoveAt(clientsStructRefsIds.Count - 1);
                    if (clientsStructRefsIds.Count > 0)
                    {
                        nextStructsTarget = clientsStructRefs[clientsStructRefsIds[clientsStructRefsIds.Count - 1]];
                    }
                    else
                    {
                        _pendingClientStructRefs.Clear();
                        return null;
                    }
                }

                return nextStructsTarget;
            }

            var curStructsTarget = getNextStructTarget();
            if (curStructsTarget == null && stack.Count == 0)
            {
                return;
            }

            var stackHead = stack.Count > 0 ? stack.Pop() : curStructsTarget.Refs[curStructsTarget.NextReadOperation++];
            // Caching the state because it is used very often.
            var state = new Dictionary<long, long>();

            // Iterate over all struct readers until we are done.
            while (true)
            {
                if (!state.TryGetValue(stackHead.Id.Client, out var localClock))
                {
                    localClock = GetState(stackHead.Id.Client);
                    state[stackHead.Id.Client] = localClock;
                }

                var offset = stackHead.Id.Clock < localClock ? localClock - stackHead.Id.Clock : 0;
                if (stackHead.Id.Clock + offset != localClock)
                {
                    // A previous message from this client is missing.
                    // Check if there is a pending structRef with a smaller clock and switch them.
                    if (!clientsStructRefs.TryGetValue(stackHead.Id.Client, out var structRefs))
                    {
                        structRefs = new PendingClientStructRef();
                    }

                    if (structRefs.Refs.Count != structRefs.NextReadOperation)
                    {
                        var r = structRefs.Refs[structRefs.NextReadOperation];
                        if (r.Id.Clock < stackHead.Id.Clock)
                        {
                            // Put ref with smaller clock on stack instead and continue.
                            structRefs.Refs[structRefs.NextReadOperation] = stackHead;
                            stackHead = r;

                            // Sort the set because this approach might bring the list out of order.
                            structRefs.Refs.RemoveRange(0, structRefs.NextReadOperation);
                            structRefs.Refs.Sort((a, b) => a.Id.Clock.CompareTo(b.Id.Clock));

                            structRefs.NextReadOperation = 0;
                            continue;
                        }
                    }

                    // Wait until missing struct is available.
                    stack.Push(stackHead);
                    return;
                }

                var missing = stackHead.GetMissing(transaction, this);
                if (missing == null)
                {
                    if (offset == 0 || offset < stackHead.Length)
                    {
                        stackHead.Integrate(transaction, (int)offset);
                        state[stackHead.Id.Client] = stackHead.Id.Clock + stackHead.Length;
                    }

                    // Iterate to next stackHead.
                    if (stack.Count > 0)
                    {
                        stackHead = stack.Pop();
                    }
                    else if (curStructsTarget != null && curStructsTarget.NextReadOperation < curStructsTarget.Refs.Count)
                    {
                        stackHead = curStructsTarget.Refs[curStructsTarget.NextReadOperation++];
                    }
                    else
                    {
                        curStructsTarget = getNextStructTarget();
                        if (curStructsTarget == null)
                        {
                            // We are done!
                            break;
                        }
                        else
                        {
                            stackHead = curStructsTarget.Refs[curStructsTarget.NextReadOperation++];
                        }
                    }
                }
                else
                {
                    // Get the struct reader that has the missing struct.
                    if (!clientsStructRefs.TryGetValue(missing.Value, out var structRefs))
                    {
                        structRefs = new PendingClientStructRef();
                    }

                    if (structRefs.Refs.Count == structRefs.NextReadOperation)
                    {
                        // This update message causally depends on another update message.
                        stack.Push(stackHead);
                        return;
                    }

                    stack.Push(stackHead);
                    stackHead = structRefs.Refs[structRefs.NextReadOperation++];
                }
            }

            _pendingClientStructRefs.Clear();
        }

        internal void TryResumePendingDeleteReaders(Transaction transaction)
        {
            var pendingReaders = _pendingDeleteReaders.ToArray();
            _pendingDeleteReaders.Clear();

            for (int i = 0; i < pendingReaders.Length; i++)
            {
                ReadAndApplyDeleteSet(pendingReaders[i], transaction);
            }
        }
    }
}
