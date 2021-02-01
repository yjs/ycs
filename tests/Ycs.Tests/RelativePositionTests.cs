// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class RelativePositionTests : YTestBase
    {
        [TestMethod]
        public void TestRelativePositionCase1()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            yText.Insert(0, "1");
            yText.Insert(0, "abc");
            yText.Insert(0, "z");
            yText.Insert(0, "y");
            yText.Insert(0, "x");

            CheckRelativePositions(yText);
        }

        [TestMethod]
        public void TestRelativePositionCase2()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            yText.Insert(0, "abc");

            CheckRelativePositions(yText);
        }

        [TestMethod]
        public void TestRelativePositionCase3()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            yText.Insert(0, "abc");
            yText.Insert(0, "1");
            yText.Insert(0, "xyz");

            CheckRelativePositions(yText);
        }

        [TestMethod]
        public void TestRelativePositionCase4()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            yText.Insert(0, "1");

            CheckRelativePositions(yText);
        }

        [TestMethod]
        public void TestRelativePositionCase5()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            yText.Insert(0, "2");
            yText.Insert(0, "1");

            CheckRelativePositions(yText);
        }

        [TestMethod]
        public void TestRelativePositionCase6()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            CheckRelativePositions(yText);
        }

        [TestMethod]
        public void TestRelativePositionAssociationDifference()
        {
            Init(users: 1);
            var yText = Texts[Users[0]];

            yText.Insert(0, "2");
            yText.Insert(0, "1");

            var rposRight = RelativePosition.FromTypeIndex(yText, 1, 0);
            var rposLeft = RelativePosition.FromTypeIndex(yText, 1, -1);

            yText.Insert(1, "x");

            var posRight = AbsolutePosition.TryCreateFromRelativePosition(rposRight, Users[0]);
            var posLeft = AbsolutePosition.TryCreateFromRelativePosition(rposLeft, Users[0]);

            Assert.IsNotNull(posRight);
            Assert.IsNotNull(posLeft);
            Assert.AreEqual(2, posRight.Index);
            Assert.AreEqual(1, posLeft.Index);
        }

        private void CheckRelativePositions(YText yText)
        {
            // Test if all positions are encoded and restored correctly.
            for (int i = 0; i < yText.Length; i++)
            {
                // For all types of assotiations.
                for (int assoc = -1; assoc < 2; assoc++)
                {
                    var rpos = RelativePosition.FromTypeIndex(yText, i, assoc);
                    var encodedRpos = rpos.ToArray();
                    var decodedRpos = RelativePosition.Read(encodedRpos);
                    var absPos = AbsolutePosition.TryCreateFromRelativePosition(decodedRpos, yText.Doc);

                    Assert.AreEqual(i, absPos.Index);
                    Assert.AreEqual(assoc, absPos.Assoc);
                }
            }
        }
    }
}
