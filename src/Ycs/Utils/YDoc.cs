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
    public class YDocOptions
    {
        private static Predicate<Item> DefaultPredicate = (item) => true;

        public bool Gc { get; set; } = true;
        public Predicate<Item> GcFilter { get; set; } = DefaultPredicate;
        public string Guid { get; set; } = System.Guid.NewGuid().ToString("D");
        public IDictionary<string, string> Meta { get; set; } = null;
        public bool AutoLoad { get; set; } = false;

        internal YDocOptions Clone()
        {
            return new YDocOptions
            {
                Gc = Gc,
                GcFilter = GcFilter,
                Guid = Guid,
                Meta = Meta == null ? null : new Dictionary<string, string>(Meta),
                AutoLoad = AutoLoad
            };
        }

        internal void Write(IUpdateEncoder encoder, int offset)
        {
            var dict = new Dictionary<string, object>();
            dict["gc"] = Gc;
            dict["guid"] = Guid;
            dict["autoLoad"] = AutoLoad;

            if (Meta != null)
            {
                dict["meta"] = Meta;
            }

            encoder.WriteAny(dict);
        }

        internal static YDocOptions Read(IUpdateDecoder decoder)
        {
            var dict = (IDictionary<string, object>)decoder.ReadAny();

            var result = new YDocOptions();
            result.Gc = dict.ContainsKey("gc") ? (bool)dict["gc"] : true;
            result.Guid = dict.ContainsKey("guid") ? dict["guid"].ToString() : System.Guid.NewGuid().ToString("D");
            result.Meta = dict.ContainsKey("meta") ? dict["meta"] as Dictionary<string, string> : null;
            result.AutoLoad = dict.ContainsKey("autoLoad") ? (bool)dict["autoLoad"] : false;

            return result;
        }
    }

    /// <summary>
    /// Yjs instance handles the state of shared data.
    /// </summary>
    public class YDoc
    {
        private readonly YDocOptions _opts;

        public string Guid => _opts.Guid;
        public bool Gc => _opts.Gc;
        public Predicate<Item> GcFilter => _opts.GcFilter;
        public bool AutoLoad => _opts.AutoLoad;
        public IDictionary<string, string> Meta => _opts.Meta;

        internal bool ShouldLoad;
        internal readonly IList<Transaction> _transactionCleanups;
        internal Transaction _transaction;
        internal readonly ISet<YDoc> Subdocs;

        // If this document is a subdocument - a document integrated into another document - them _item is defined.
        internal Item _item;

        private readonly IDictionary<string, AbstractType> _share;

        internal static int GenerateNewClientId()
        {
            return new Random().Next(0, int.MaxValue);
        }

        /// <param name="gc">Disable garbage collection.</param>
        /// <param name="gcFilter">WIll be called before an Item is garbage collected. Return false to keep the item.</param>
        public YDoc(YDocOptions opts = null)
        {
            _opts = opts ?? new YDocOptions();
            _transactionCleanups = new List<Transaction>();

            ClientId = GenerateNewClientId();
            _share = new Dictionary<string, AbstractType>();
            Store = new StructStore();
            Subdocs = new HashSet<YDoc>();
            ShouldLoad = _opts.AutoLoad;
        }

        /// <summary>
        /// Notify the parent document that you request to load data into this subdocument (if it is a subdocument).
        /// 'load()' might be used in the future to request any provider to load the most current data.
        /// It is safe to call 'Load()' multiple times.
        /// </summary>
        public void Load()
        {
            var item = _item;
            if (item != null && !ShouldLoad)
            {
                Debug.Assert(item.Parent is AbstractType);
                (item.Parent as AbstractType).Doc.Transact(tr =>
                {
                    tr.SubdocsLoaded.Add(this);
                }, origin: null, local: true);
            }
            ShouldLoad = true;
        }

        public Snapshot CreateSnapshot() => new Snapshot(new DeleteSet(Store), Store.GetStateVector());

        public IEnumerable<string> GetSubdocGuids()
        {
            return new HashSet<string>(Subdocs.Select(sd => sd.Guid));
        }

        public void Destroy()
        {
            foreach (var sd in Subdocs)
            {
                sd.Destroy();
            }

            var item = _item;
            if (item != null)
            {
                _item = null;
                var content = item.Content as ContentDoc;

                if (item.Deleted)
                {
                    if (content != null)
                    {
                        content.Doc = null;
                    }
                }
                else
                {
                    Debug.Assert(content != null);
                    var newOpts = content.Opts;
                    newOpts.Guid = Guid;

                    content.Doc = new YDoc(newOpts);
                    content.Doc._item = item;
                }

                (item.Parent as AbstractType).Doc.Transact(tr =>
                {
                    if (!item.Deleted)
                    {
                        Debug.Assert(content != null);
                        tr.SubdocsAdded.Add(content.Doc);
                    }

                    tr.SubdocsRemoved.Add(this);
                }, origin: null, local: true);
            }

            InvokeDestroyed();
        }

        public event EventHandler<Transaction> BeforeObserverCalls;
        public event EventHandler<Transaction> BeforeTransaction;
        public event EventHandler<Transaction> AfterTransaction;
        public event EventHandler<Transaction> AfterTransactionCleanup;
        public event EventHandler BeforeAllTransactions;
        public event EventHandler<IList<Transaction>> AfterAllTransactions;
        public event EventHandler<(byte[] data, object origin, Transaction transaction)> UpdateV2;
        public event EventHandler Destroyed;
        public event EventHandler<(ISet<YDoc> Loaded, ISet<YDoc> Added, ISet<YDoc> Removed)> SubdocsChanged;

        public int ClientId { get; internal set; }
        internal StructStore Store { get; private set; }

        /// <summary>
        /// Changes that happen inside of a transaction are bundled.
        /// This means that the observer fires _after_ the transaction is finished and that
        /// all changes that happened inside of the transaction are sent as one message to the
        /// other peers.
        /// </summary>
        /// <param name="fun">The function that should be executed as a transaction.</param>
        /// <param name="origin">Transaction owner. Will be stored in 'transaction.origin'.</param>
        /// <param name="local"></param>
        public void Transact(Action<Transaction> fun, object origin = null, bool local = true)
        {
            bool initialCall = false;
            if (_transaction == null)
            {
                initialCall = true;
                _transaction = new Transaction(this, origin, local);
                _transactionCleanups.Add(_transaction);
                if (_transactionCleanups.Count == 1)
                {
                    InvokeBeforeAllTransactions();
                }

                InvokeOnBeforeTransaction(_transaction);
            }

            try
            {
                fun(_transaction);
            }
            finally
            {
                if (initialCall && _transactionCleanups[0] == _transaction)
                {
                    // The first transaction ended, now process observer calls.
                    // Observer call may create new transacations for which we need to call the observers and do cleanup.
                    // We don't want to nest these calls, so we execute these calls one after another.
                    // Also we need to ensure that all cleanups are called, even if the observers throw errors.
                    Transaction.CleanupTransactions(_transactionCleanups, 0);
                }
            }
        }

        public YArray GetArray(string name = "")
        {
            return Get<YArray>(name);
        }

        public YMap GetMap(string name = "")
        {
            return Get<YMap>(name);
        }

        public YText GetText(string name = "")
        {
            return Get<YText>(name);
        }

        public T Get<T>(string name)
            where T : AbstractType, new()
        {
            if (!_share.TryGetValue(name, out var type))
            {
                type = new T();
                type.Integrate(this, null);
                _share[name] = type;
            }

            // Remote type is realized when this method is called.
            if (typeof(T) != typeof(AbstractType) && !typeof(T).IsAssignableFrom(type.GetType()))
            {
                if (type.GetType() == typeof(AbstractType))
                {
                    var t = new T();
                    t._map = type._map;

                    foreach (var kvp in type._map)
                    {
                        var n = kvp.Value;
                        for (; n != null; n = n.Left as Item)
                        {
                            n.Parent = t;
                        }
                    }

                    t._start = type._start;
                    for (var n = t._start; n != null; n = n.Right as Item)
                    {
                        n.Parent = t;
                    }

                    t.Length = type.Length;

                    _share[name] = t;
                    t.Integrate(this, null);
                    return t;
                }
                else
                {
                    throw new Exception($"Type with the name {name} has already been defined with a different constructor");
                }
            }

            return (T)type;
        }

        /// <summary>
        /// Read and apply a document update.
        /// <br/>
        /// This function has the same effect as 'applyUpdate' but accepts a decoder.
        /// </summary>
        public void ApplyUpdateV2(Stream input, object transactionOrigin = null, bool local = false)
        {
            Transact(tr =>
            {
                using (var structDecoder = new UpdateDecoderV2(input))
                {
                    EncodingUtils.ReadStructs(structDecoder, tr, Store);
                    Store.ReadAndApplyDeleteSet(structDecoder, tr);
                }
            }, transactionOrigin, local);
        }

        public void ApplyUpdateV2(byte[] update, object transactionOrigin = null, bool local = false)
        {
            using (var input = new MemoryStream(update, writable: false))
            {
                ApplyUpdateV2(input, transactionOrigin, local);
            }
        }

        /// <summary>
        /// Write all the document as a single update message that can be applied on the remote document.
        /// If you specify the state of the remote client, it will only write the operations that are missing.
        /// <br/>
        /// Use 'WriteStateAsUpdate' instead if you are working with Encoder.
        /// </summary>
        public byte[] EncodeStateAsUpdateV2(byte[] encodedTargetStateVector = null)
        {
            using (var encoder = new UpdateEncoderV2())
            {
                var targetStateVector = encodedTargetStateVector == null
                    ? new Dictionary<long, long>()
                    : EncodingUtils.DecodeStateVector(new MemoryStream(encodedTargetStateVector, writable: false));
                WriteStateAsUpdate(encoder, targetStateVector);
                return encoder.ToArray();
            }
        }

        public byte[] EncodeStateVectorV2()
        {
            using (var encoder = new DSEncoderV2())
            {
                WriteStateVector(encoder);
                return encoder.ToArray();
            }
        }

        /// <summary>
        /// Write all the document as a single update message. If you specify the satte of the remote client, it will only
        /// write the operations that are missing.
        /// </summary>
        internal void WriteStateAsUpdate(IUpdateEncoder encoder, IDictionary<long, long> targetStateVector)
        {
            EncodingUtils.WriteClientsStructs(encoder, Store, targetStateVector);
            new DeleteSet(Store).Write(encoder);
        }

        internal void WriteStateVector(IDSEncoder encoder)
        {
            EncodingUtils.WriteStateVector(encoder, Store.GetStateVector());
        }

        internal void InvokeSubdocsChanged(ISet<YDoc> loaded, ISet<YDoc> added, ISet<YDoc> removed)
        {
            SubdocsChanged?.Invoke(this, (loaded, added, removed));
        }

        internal void InvokeOnBeforeObserverCalls(Transaction transaction)
        {
            BeforeObserverCalls?.Invoke(this, transaction);
        }

        internal void InvokeAfterAllTransactions(IList<Transaction> transactions)
        {
            AfterAllTransactions?.Invoke(this, transactions);
        }

        internal void InvokeOnBeforeTransaction(Transaction transaction)
        {
            BeforeTransaction?.Invoke(this, transaction);
        }

        internal void InvokeOnAfterTransaction(Transaction transaction)
        {
            AfterTransaction?.Invoke(this, transaction);
        }

        internal void InvokeOnAfterTransactionCleanup(Transaction transaction)
        {
            AfterTransactionCleanup?.Invoke(this, transaction);
        }

        internal void InvokeBeforeAllTransactions()
        {
            BeforeAllTransactions?.Invoke(this, null);
        }

        internal void InvokeDestroyed()
        {
            Destroyed?.Invoke(this, null);
        }

        internal void InvokeUpdateV2(Transaction transaction)
        {
            var handler = UpdateV2;
            if (handler != null)
            {
                using (var encoder = new UpdateEncoderV2())
                {
                    var hasContent = transaction.WriteUpdateMessageFromTransaction(encoder);
                    if (hasContent)
                    {
                        handler.Invoke(this, (encoder.ToArray(), transaction.Origin, transaction));
                    }
                }
            }
        }

        internal YDocOptions CloneOptionsWithNewGuid()
        {
            var newOpts = _opts.Clone();
            newOpts.Guid = System.Guid.NewGuid().ToString("D");
            return newOpts;
        }

        internal string FindRootTypeKey(AbstractType type)
        {
            foreach (var kvp in _share)
            {
                if (type?.Equals(kvp.Value) ?? false)
                {
                    return kvp.Key;
                }
            }

            throw new Exception();
        }
    }
}
