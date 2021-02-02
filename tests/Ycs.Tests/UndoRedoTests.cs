// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class UndoRedoTests : YTestBase
    {
        [TestMethod]
        public void TestUndoText()
        {
            Init(users: 3);
            var text0 = Texts[Users[0]];
            var text1 = Texts[Users[1]];
            var undoManager = new UndoManager(text0);

            // Items that are added and deleted in the same transaction won't be undone.
            text0.Insert(0, "test");
            text0.Delete(0, 4);
            undoManager.Undo();
            Assert.AreEqual(string.Empty, text0.ToString());

            // Follow redone items.
            text0.Insert(0, "a");
            undoManager.StopCapturing();
            text0.Delete(0, 1);
            undoManager.StopCapturing();
            undoManager.Undo();
            Assert.AreEqual("a", text0.ToString());
            undoManager.Undo();
            Assert.AreEqual(string.Empty, text0.ToString());

            text0.Insert(0, "abc");
            text1.Insert(0, "xyz");
            Connector.SyncAll();
            undoManager.Undo();
            Assert.AreEqual("xyz", text0.ToString());
            undoManager.Redo();
            Assert.AreEqual("abcxyz", text0.ToString());
            Connector.SyncAll();
            text1.Delete(0, 1);
            Connector.SyncAll();
            undoManager.Undo();
            Assert.AreEqual("xyz", text0.ToString());
            undoManager.Redo();
            Assert.AreEqual("bcxyz", text0.ToString());

            // Test formats.
            text0.Format(1, 3, new Dictionary<string, object> { { "bold", true } });
            var delta = text0.ToDelta();
            Assert.AreEqual(3, delta?.Count);
            Assert.AreEqual("b", delta[0].Insert);
            Assert.IsNull(delta[0].Attributes);
            Assert.AreEqual("cxy", delta[1].Insert);
            Assert.AreEqual(true, delta[1].Attributes["bold"]);

            undoManager.Undo();
            delta = text0.ToDelta();
            Assert.AreEqual(1, delta?.Count);
            Assert.AreEqual("bcxyz", delta[0].Insert);
            Assert.IsNull(delta[0].Attributes);

            undoManager.Redo();
            delta = text0.ToDelta();
            Assert.AreEqual(3, delta?.Count);
            Assert.AreEqual("b", delta[0].Insert);
            Assert.IsNull(delta[0].Attributes);
            Assert.AreEqual("cxy", delta[1].Insert);
            Assert.AreEqual(true, delta[1].Attributes["bold"]);
        }

        [TestMethod]
        public void TestDoubleUndo()
        {
            var doc = new YDoc();
            var text = doc.GetText();
            text.Insert(0, "1221");

            var undoManager = new UndoManager(text);

            text.Insert(2, "3");
            text.Insert(3, "3");

            undoManager.Undo();
            undoManager.Undo();

            text.Insert(2, "3");
            Assert.AreEqual("12321", text.ToString());
        }

        [TestMethod]
        public void TestUndoMap()
        {
            Init(users: 3);
            var map0 = Maps[Users[0]];
            var map1 = Maps[Users[1]];
            var undoManager = new UndoManager(map0);

            map0.Set("a", 1);
            undoManager.Undo();
            Assert.IsFalse(map0.ContainsKey("a"));
            undoManager.Redo();
            Assert.AreEqual(1, map0.Get("a"));

            // Test subtypes and whether it can restore the whole type.
            var subType = new YMap();
            map0.Set("a", subType);
            subType.Set("x", 42);
            Assert.AreEqual(42, (map0.Get("a") as YMap)?.Get("x"));
            undoManager.Undo();
            Assert.AreEqual(1, map0.Get("a"));
            undoManager.Redo();
            Assert.AreEqual(42, (map0.Get("a") as YMap)?.Get("x"));
            Connector.SyncAll();

            // If content is overwritten by another user, undo operations should be skipped.
            map1.Set("a", 44);
            Connector.SyncAll();
            undoManager.Undo();
            Assert.AreEqual(44, map0.Get("a"));
            undoManager.Redo();
            Assert.AreEqual(44, map0.Get("a"));

            // Test setting value multiple times.
            map0.Set("b", "initial");
            undoManager.StopCapturing();
            map0.Set("b", "val1");
            map0.Set("b", "val2");
            undoManager.StopCapturing();
            undoManager.Undo();
            Assert.AreEqual("initial", map0.Get("b"));
        }

        [TestMethod]
        public void TestUndoArray()
        {
            Init(users: 3);
            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];
            var undoManager = new UndoManager(array0);

            array0.Insert(0, new object[] { 1, 2, 3 });
            array1.Insert(9, new object[] { 4, 5, 6 });
            Connector.SyncAll();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, (ICollection)array0.ToArray());
            undoManager.Undo();
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, (ICollection)array0.ToArray());
            undoManager.Redo();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, (ICollection)array0.ToArray());
            Connector.SyncAll();
            // User1 deletes [1]
            array1.Delete(0, 1);
            Connector.SyncAll();
            undoManager.Undo();
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, (ICollection)array0.ToArray());
            undoManager.Redo();
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, (ICollection)array0.ToArray());
            array0.Delete(0, 5);

            // Test nested structure.
            var ymap = new YMap();
            array0.Insert(0, new[] { ymap });
            Assert.AreEqual(0, (array0.Get(0) as YMap)?.Count);
            undoManager.StopCapturing();
            ymap.Set("a", 1);
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Count);
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Get("a"));
            undoManager.Undo();
            Assert.AreEqual(0, (array0.Get(0) as YMap)?.Count);
            undoManager.Undo();
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, (ICollection)array0.ToArray());
            undoManager.Redo();
            Assert.AreEqual(0, (array0.Get(0) as YMap)?.Count);
            undoManager.Redo();
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Count);
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Get("a"));
            Connector.SyncAll();
            (array1.Get(0) as YMap).Set("b", 2);
            Connector.SyncAll();
            Assert.AreEqual(2, (array0.Get(0) as YMap)?.Count);
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Get("a"));
            Assert.AreEqual(2, (array0.Get(0) as YMap)?.Get("b"));
            undoManager.Undo();
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Count);
            Assert.AreEqual(2, (array0.Get(0) as YMap)?.Get("b"));
            undoManager.Undo();
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, (ICollection)array0.ToArray());
            undoManager.Redo();
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Count);
            Assert.AreEqual(2, (array0.Get(0) as YMap)?.Get("b"));
            undoManager.Redo();
            Assert.AreEqual(2, (array0.Get(0) as YMap)?.Count);
            Assert.AreEqual(1, (array0.Get(0) as YMap)?.Get("a"));
            Assert.AreEqual(2, (array0.Get(0) as YMap)?.Get("b"));
        }

        [TestMethod]
        public void TestUndoEvents()
        {
            Init(users: 3);
            var text0 = Texts[Users[0]];
            var undoManager = new UndoManager(text0);

            int counter = 0;
            int receivedMetadata = -1;

            undoManager.StackItemAdded += (s, e) =>
            {
                Assert.IsNotNull(e.Type);
                Assert.IsTrue(e.ChangedParentTypes?.ContainsKey(text0) ?? false);
                e.StackItem.Meta["test"] = counter++;
            };

            undoManager.StackItemPopped += (s, e) =>
            {
                Assert.IsNotNull(e.Type);
                Assert.IsTrue(e.ChangedParentTypes?.ContainsKey(text0) ?? false);
                receivedMetadata = e.StackItem.Meta.TryGetValue("test", out var val) ? (int)val : -1;
            };

            text0.Insert(0, "abc");
            undoManager.Undo();
            Assert.AreEqual(0, receivedMetadata);
            undoManager.Redo();
            Assert.AreEqual(1, receivedMetadata);
        }

        [TestMethod]
        public void TestTrackClass()
        {
            Init(users: 3);
            var text0 = Texts[Users[0]];
            var undoManager = new UndoManager(new[] { text0 }, 500, it => true, new HashSet<object> { typeof(int) });

            Users[0].Transact(tr =>
            {
                text0.Insert(0, "abc");
            }, origin: 42);

            Assert.AreEqual("abc", text0.ToString());
            undoManager.Undo();
            Assert.AreEqual(string.Empty, text0.ToString());
        }

        [TestMethod]
        public void TestTypeScope()
        {
            Init(users: 3);
            var array0 = Arrays[Users[0]];

            var text0 = new YText();
            var text1 = new YText();
            array0.Insert(0, new[] { text0, text1 });

            var undoManager = new UndoManager(text0);
            var undoManagerBoth = new UndoManager(new[] { text0, text1 }, 500, it => true, new HashSet<object> { null });
            text1.Insert(0, "abc");
            Assert.AreEqual(0, undoManager.Count);
            Assert.AreEqual(1, undoManagerBoth.Count);
            Assert.AreEqual("abc", text1.ToString());
            undoManager.Undo();
            Assert.AreEqual("abc", text1.ToString());
            undoManagerBoth.Undo();
            Assert.AreEqual(string.Empty, text1.ToString());
        }

        [TestMethod]
        public void TestUndoDeleteFilter()
        {
            Init(users: 3);
            var array0 = Arrays[Users[0]];
            var undoManager = new UndoManager(new[] { array0 }, 500, it => !(it is Item item) || (item.Content is ContentType ct && ct.Type._map.Count == 0), new HashSet<object> { null });
            var map0 = new YMap();
            map0.Set("hi", 1);
            var map1 = new YMap();
            array0.Insert(0, new[] { map0, map1 });
            undoManager.Undo();
            Assert.AreEqual(1, array0.Length);
            Assert.AreEqual(1, (array0.Get(0) as YMap).Keys().Count());
        }
    }
}
