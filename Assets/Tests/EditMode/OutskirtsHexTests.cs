using System.Linq;
using Client.Framework.Presentation;
using NUnit.Framework;

namespace Client.Tests
{
    public sealed class OutskirtsHexTests
    {
        [Test, Category("Outskirts")]
        public void CentralCastleHasSixInnerCellsAndTwelveTerritoryCells()
        {
            var cells = Enumerable.Range(1, OutskirtsHex.Count).ToArray();
            Assert.That(cells.Count(p => OutskirtsHex.Distance(p, OutskirtsHex.HomePosition) == 1), Is.EqualTo(6));
            Assert.That(cells.Count(p => OutskirtsHex.Distance(p, OutskirtsHex.HomePosition) == 2), Is.EqualTo(12));
            Assert.That(cells.Count(p => OutskirtsHex.Distance(p, OutskirtsHex.HomePosition) <= 2), Is.EqualTo(19));
        }

        [Test, Category("Outskirts")]
        public void PortraitLayoutKeepsAllFortyFiveDistinctSelectableIds()
        {
            var cells = Enumerable.Range(1, OutskirtsHex.Count).ToArray();
            Assert.That(cells.Select(OutskirtsHex.Center).Distinct().Count(), Is.EqualTo(45));
            foreach (int id in cells)
            {
                Assert.That(OutskirtsHex.Pick(OutskirtsHex.Center(id)), Is.EqualTo(id));
                Assert.That(OutskirtsHex.Distance(id, id), Is.Zero);
            }
            foreach (int id in new[] { 13, 16, 21, 23, 34 })
                Assert.That(OutskirtsHex.Distance(id, OutskirtsHex.HomePosition), Is.GreaterThan(2), "Special sites must stay outside the private land ring.");
        }
    }
}
