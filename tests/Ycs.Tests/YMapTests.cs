// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class YMapTests : YTestBase
    {
        [TestMethod]
        public void TestMapHavingIterableAsConstructorParamTests()
        {
            Init(users: 1);
            var map0 = Maps[Users[0]];

            var m1 = new YMap(new Dictionary<string, object> { { "number", 1 }, { "string", "hello" } });
            map0.Set("m1", m1);
            Assert.AreEqual(1, m1.Get("number"));
            Assert.AreEqual("hello", m1.Get("string"));

            var obj = new object[] { 1 };
            var m2 = new YMap(new Dictionary<string, object> { { "object", obj }, { "boolean", true } });
            map0.Set("m2", m2);
            Assert.AreEqual(obj, m2.Get("object"));
            Assert.AreEqual(true, m2.Get("boolean"));

            CompareUsers();
        }

        [TestMethod]
        public void TestBasicMapTests()
        {
            Init(users: 3);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];
            var map2 = Maps[Users[2]];

            Users[2].Disconnect();

            map0.Set("number", 1);
            map0.Set("string", "hello Y");
            map0.Set("object", new List<object> { "value" });
            map0.Set("y-map", new YMap());
            map0.Set("boolean1", true);
            map0.Set("boolean0", false);

            var map = map0.Get("y-map") as YMap;
            Assert.IsNotNull(map);
            map.Set("y-array", new YArray());
            var array = map.Get("y-array") as YArray;
            Assert.IsNotNull(array);

            array.Insert(0, new object[] { 0 });
            array.Insert(0, new object[] { -1 });

            Assert.AreEqual(1, map0.Get("number"));
            Assert.AreEqual("hello Y", map0.Get("string"));
            Assert.AreEqual(false, map0.Get("boolean0"));
            Assert.AreEqual(true, map0.Get("boolean1"));
            Assert.AreEqual("value", (map0.Get("object") as IList<object>)[0]);
            Assert.AreEqual(-1, ((map0.Get("y-map") as YMap).Get("y-array") as YArray).Get(0));
            Assert.AreEqual(6, map0.Keys().Count());

            Users[2].Connect();
            Connector.FlushAllMessages();

            Assert.AreEqual(1, map1.Get("number"));
            Assert.AreEqual("hello Y", map1.Get("string"));
            Assert.AreEqual(false, map1.Get("boolean0"));
            Assert.AreEqual(true, map1.Get("boolean1"));

            Assert.AreEqual("value", (map1.Get("object") as IList<object>)[0]);
            Assert.AreEqual(-1, ((map1.Get("y-map") as YMap).Get("y-array") as YArray).Get(0));
            Assert.AreEqual(6, map1.Keys().Count());

            // Compare disconnected user.
            Assert.AreEqual(1, map2.Get("number"));
            Assert.AreEqual("hello Y", map2.Get("string"));
            Assert.AreEqual(false, map2.Get("boolean0"));
            Assert.AreEqual(true, map2.Get("boolean1"));
            Assert.AreEqual("value", (map2.Get("object") as IList<object>)[0]);
            Assert.AreEqual(-1, ((map2.Get("y-map") as YMap).Get("y-array") as YArray).Get(0));
            Assert.AreEqual(6, map2.Keys().Count());

            CompareUsers();
        }

        [TestMethod]
        public void TestGetAndSetOfMapProperty()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];

            map0.Set("stuff", "stuffy");
            map0.Set("null", null);
            Assert.AreEqual("stuffy", map0.Get("stuff"));
            Assert.AreEqual(null, map0.Get("null"));

            Connector.FlushAllMessages();

            foreach (var user in Users)
            {
                var u = user.GetMap("map");
                Assert.AreEqual("stuffy", u.Get("stuff"));
                Assert.AreEqual(null, u.Get("null"));
            }

            CompareUsers();
        }

        [TestMethod]
        public void TestYMapSetsYMap()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];
            var map = new YMap();
            map0.Set("map", map);

            Assert.AreEqual(map, map0.Get("map"));
            map.Set("one", 1);
            Assert.AreEqual(1, map.Get("one"));

            CompareUsers();
        }

        [TestMethod]
        public void TestYMapSetsYArray()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];

            var array = new YArray();
            map0.Set("array", array);
            Assert.AreEqual(array, map0.Get("array"));

            array.Insert(0, new object[] { 1, 2, 3 });
            CollectionAssert.AreEqual(new object[] { 1, 2, 3 }, (ICollection)(map0.Get("array") as YArray).ToArray());

            CompareUsers();
        }

        [TestMethod]
        public void TestGetAndSetOfMapPropertySyncs()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];

            map0.Set("stuff", "stuffy");
            Assert.AreEqual("stuffy", map0.Get("stuff"));
            Connector.FlushAllMessages();

            foreach (var user in Users)
            {
                var u = user.GetMap("map");
                Assert.AreEqual("stuffy", u.Get("stuff"));
            }

            CompareUsers();
        }

        [TestMethod]
        public void TestGetAndSetOfMapPropertyWithConflict()
        {
            Init(users: 3);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];

            map0.Set("stuff", "c0");
            map1.Set("stuff", "c1");
            Connector.FlushAllMessages();

            foreach (var user in Users)
            {
                var u = user.GetMap("map");
                Assert.AreEqual("c1", u.Get("stuff"));
            }

            CompareUsers();
        }

        [TestMethod]
        public void TestSizeAndDeleteOfMapProperty()
        {
            Init(users: 1);
            var map0 = Maps[Users[0]];

            map0.Set("stuff", "c0");
            map0.Set("otherStuff", "c1");
            Assert.AreEqual(2, map0.Keys().Count());

            map0.Delete("stuff");
            Assert.AreEqual(1, map0.Keys().Count());

            map0.Delete("otherStuff");
            Assert.AreEqual(0, map0.Keys().Count());

            CompareUsers();
        }

        [TestMethod]
        public void TestGetAndSetAndDeleteOfMapProperty()
        {
            Init(users: 3);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];

            map0.Set("stuff", "c0");
            map1.Set("stuff", "c1");
            map1.Delete("stuff");

            Connector.FlushAllMessages();

            foreach (var user in Users)
            {
                var u = user.GetMap("map");
                Assert.IsFalse(u.ContainsKey("stuff"));
            }

            CompareUsers();
        }

        [TestMethod]
        public void TestGetAndSetOfMapPropertyWithThreeConflicts()
        {
            Init(users: 3);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];
            var map2 = Maps[Users[2]];

            map0.Set("stuff", "c0");
            map1.Set("stuff", "c1");
            map1.Set("stuff", "c2");
            map2.Set("stuff", "c3");

            Connector.FlushAllMessages();

            foreach (var user in Users)
            {
                var u = user.GetMap("map");
                Assert.AreEqual("c3", u.Get("stuff"));
            }

            CompareUsers();
        }

        [TestMethod]
        public void TestGetAndSetAndDeleteOfMapPropertyWithThreeConflicts()
        {
            Init(users: 4);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];
            var map2 = Maps[Users[2]];
            var map3 = Maps[Users[3]];

            map0.Set("stuff", "c0");
            map1.Set("stuff", "c1");
            map1.Set("stuff", "c2");
            map2.Set("stuff", "c3");

            Connector.FlushAllMessages();

            map0.Set("stuff", "deleteMe");
            map1.Set("stuff", "c1");
            map2.Set("stuff", "c2");
            map3.Set("stuff", "c3");
            map3.Delete("stuff");

            Connector.FlushAllMessages();

            foreach (var user in Users)
            {
                var u = user.GetMap("map");
                Assert.IsFalse(u.ContainsKey("stuff"));
            }

            CompareUsers();
        }

        [TestMethod]
        public void TestObserveDeepProperties()
        {
            Init(users: 4);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];
            var map2 = Maps[Users[2]];
            var map3 = Maps[Users[3]];

            var _map1 = new YMap();
            map1.Set("map", _map1);

            int calls = 0;
            ID? dmapId = null;

            map1.DeepEventHandler += (s, e) =>
            {
                foreach (var evt in e.Events)
                {
                    calls++;

                    Assert.IsTrue(evt.Changes.Keys.ContainsKey("deepmap"));
                    Assert.AreEqual(1, evt.Path.Count);
                    Assert.AreEqual("map", evt.Path.First());
                    dmapId = ((evt.Target as YMap).Get("deepmap") as YMap)._item?.Id;
                }
            };

            Connector.FlushAllMessages();

            var _map3 = map3.Get("map") as YMap;
            _map3.Set("deepmap", new YMap());
            Connector.FlushAllMessages();

            var _map2 = map2.Get("map") as YMap;
            _map2.Set("deepmap", new YMap());
            Connector.FlushAllMessages();

            var dmap1 = _map1.Get("deepmap") as YMap;
            var dmap2 = _map2.Get("deepmap") as YMap;
            var dmap3 = _map3.Get("deepmap") as YMap;

            Assert.IsTrue(calls > 0);
            Assert.IsTrue(ID.Equals(dmap1._item?.Id, dmap2._item?.Id));
            Assert.IsTrue(ID.Equals(dmap1._item?.Id, dmap3._item?.Id));
            Assert.IsTrue(ID.Equals(dmap1._item?.Id, dmapId));

            CompareUsers();
        }

        [TestMethod]
        public void TestObserversUsingObserveDeep()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];

            var paths = new List<IReadOnlyCollection<object>>();
            int calls = 0;

            map0.DeepEventHandler += (s, e) =>
            {
                foreach (var evt in e.Events)
                {
                    paths.Add(evt.Path);
                }

                calls++;
            };

            map0.Set("map", new YMap());
            (map0.Get("map") as YMap).Set("array", new YArray());
            ((map0.Get("map") as YMap).Get("array") as YArray).Insert(0, new object[] { "content" });
            Assert.AreEqual(3, calls);
            Assert.AreEqual(3, paths.Count);
            Assert.AreEqual(0, paths[0].Count);
            CollectionAssert.AreEqual(new object[] { "map" }, (ICollection)paths[1]);
            CollectionAssert.AreEqual(new object[] { "map", "array" }, (ICollection)paths[2]);

            CompareUsers();
        }

        [TestMethod]
        public void TestThrowsAddAndUpdateAndDeleteEvents()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];

            YMapEvent evt = null;

            map0.EventHandler += (s, e) =>
            {
                evt = e.Event as YMapEvent;

                // Collect changes while data is not GC'ed.
                var changes = evt.Changes;
                Assert.IsNotNull(changes);
            };

            map0.Set("stuff", 4);
            Assert.AreEqual(map0, evt.Target);
            CollectionAssert.AreEqual(new[] { "stuff" }, evt.KeysChanged.ToList());
            evt = null;

            // Update, oldValue is in contents.
            map0.Set("stuff", new YArray());
            Assert.AreEqual(map0, evt.Target);
            CollectionAssert.AreEqual(new[] { "stuff" }, evt.KeysChanged.ToList());
            evt = null;

            // Update, oldValue is in opContents.
            map0.Set("stuff", 5);
            map0.Delete("stuff");
            Assert.AreEqual(map0, evt.Target);
            CollectionAssert.AreEqual(new[] { "stuff" }, evt.KeysChanged.ToList());
            evt = null;

            CompareUsers();
        }

        [TestMethod]
        public void TestChangeEvent()
        {
            Init(users: 2);
            var map0 = Maps[Users[0]];

            ChangesCollection changes = null;
            ChangeKey keyChange = null;

            map0.EventHandler += (s, e) =>
            {
                changes = e.Event.Changes;
            };

            map0.Set("a", 1);
            Assert.IsTrue(changes.Keys.TryGetValue("a", out keyChange));
            Assert.AreEqual(ChangeAction.Add, keyChange.Action);
            Assert.IsNull(keyChange.OldValue);
            changes = null;

            map0.Set("a", 2);
            Assert.IsTrue(changes.Keys.TryGetValue("a", out keyChange));
            Assert.AreEqual(ChangeAction.Update, keyChange.Action);
            Assert.AreEqual(1, keyChange.OldValue);
            changes = null;

            Users[0].Transact(tr =>
            {
                map0.Set("a", 3);
                map0.Set("a", 4);
            });

            Assert.IsTrue(changes.Keys.TryGetValue("a", out keyChange));
            Assert.AreEqual(ChangeAction.Update, keyChange.Action);
            Assert.AreEqual(2, keyChange.OldValue);
            changes = null;

            Users[0].Transact(tr =>
            {
                map0.Set("b", 1);
                map0.Set("b", 2);
            });

            Assert.IsTrue(changes.Keys.TryGetValue("b", out keyChange));
            Assert.AreEqual(ChangeAction.Add, keyChange.Action);
            Assert.IsNull(keyChange.OldValue);
            changes = null;

            Users[0].Transact(tr =>
            {
                map0.Set("c", 1);
                map0.Delete("c");
            });

            Assert.AreEqual(0, changes.Keys.Count);
            changes = null;

            Users[0].Transact(tr =>
            {
                map0.Set("d", 1);
                map0.Set("d", 2);
            });

            Assert.IsTrue(changes.Keys.TryGetValue("d", out keyChange));
            Assert.AreEqual(ChangeAction.Add, keyChange.Action);
            Assert.IsNull(keyChange.OldValue);
            changes = null;

            CompareUsers();
        }

        [TestMethod]
        public void TestYMapEventExceptionsShouldCompleteTransaction()
        {
            Init(users: 1);
            var doc = Users[0];
            var map = Maps[Users[0]];

            bool updateCalled = false;
            bool throwingObserverCalled = false;
            bool throwingDeepObserverCalled = false;

            doc.UpdateV2 += (s, e) =>
            {
                updateCalled = true;
            };

            void throwingObserver(object sender, YEventArgs args)
            {
                throwingObserverCalled = true;
                throw new Exception("Failure");
            }

            void throwingDeepObserver(object sender, YDeepEventArgs args)
            {
                throwingDeepObserverCalled = true;
                throw new Exception("Deep failure");
            }

            map.EventHandler += throwingObserver;
            map.DeepEventHandler += throwingDeepObserver;

            Assert.ThrowsException<Exception>(() =>
            {
                map.Set("y", "2");
            });

            Assert.IsTrue(updateCalled);
            Assert.IsTrue(throwingObserverCalled);
            Assert.IsTrue(throwingDeepObserverCalled);

            // Check if it works again.
            updateCalled = false;
            throwingObserverCalled = false;
            throwingDeepObserverCalled = false;

            Assert.ThrowsException<Exception>(() =>
            {
                map.Set("z", "3");
            });

            Assert.IsTrue(updateCalled);
            Assert.IsTrue(throwingObserverCalled);
            Assert.IsTrue(throwingDeepObserverCalled);

            Assert.AreEqual("3", map.Get("z"));
        }

        [DataTestMethod]
        [DataRow(5, 20)]
        [DataRow(5, 40)]
        [DataRow(5, 42)]
        [DataRow(5, 43)]
        [DataRow(5, 44)]
        [DataRow(5, 45)]
        [DataRow(5, 46)]
        [DataRow(5, 300)]
        [DataRow(5, 400)]
        [DataRow(5, 500)]
        [DataRow(5, 600)]
        [DataRow(5, 1_000)]
        [DataRow(5, 1_800)]
        /*
        [DataRow(5, 3_000)]
        [DataRow(5, 5_000)]
        [DataRow(5, 15_000)]
        */
        public void TestRepeatingGeneratingYMapTests(int users, int iterations)
        {
            RandomTests(new List<Action<TestYInstance, Random>>
            {
                // Set
                (user, rand) =>
                {
                    var key = new[] { "one", "two" }[rand.Next(0, 2)];
                    var value = Guid.NewGuid().ToString();
                    user.GetMap("map").Set(key, value);
                },

                // Set type
                (user, rand) =>
                {
                    var key = new[] { "one", "two" }[rand.Next(0, 2)];
                    var type = new object[] { new YArray(), new YMap() }[rand.Next(0, 2)];
                    user.GetMap("map").Set(key, type);

                    if (type is YArray yarr)
                    {
                        yarr.Insert(0, new object[] { 1, 2, 3, 4 });
                    }
                    else if (type is YMap ymap)
                    {
                        ymap.Set("deepkey", "deepvalue");
                    }
                    else
                    {
                        Assert.Fail("Unexpected");
                    }
                },

                // Delete
                (user, rand) =>
                {
                    var key = new[] { "one", "two" }[rand.Next(0, 2)];
                    user.GetMap("map").Delete(key);
                }
            }, users, iterations);
        }
    }
}
