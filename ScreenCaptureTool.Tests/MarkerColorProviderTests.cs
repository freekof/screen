using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;

namespace ScreenCaptureTool.Tests
{
    [TestClass]
    public class MarkerColorProviderTests
    {
        [TestMethod]
        public void NextColor_SkipsRedAndGreen()
        {
            var provider = new MarkerColorProvider();

            for (int i = 0; i < 20; i++)
            {
                Color color = provider.NextColor();
                Assert.AreNotEqual(Color.Red, color, "Color.Red should be excluded.");
                Assert.AreNotEqual(Color.Green, color, "Color.Green should be excluded.");
            }
        }

        [TestMethod]
        public void NextColor_CyclesThroughPalette()
        {
            var palette = new List<Color> { Color.Blue, Color.Gold };
            var provider = new MarkerColorProvider(palette);

            Color first = provider.NextColor();
            Color second = provider.NextColor();
            Color third = provider.NextColor();

            Assert.AreEqual(Color.Blue, first);
            Assert.AreEqual(Color.Gold, second);
            Assert.AreEqual(Color.Blue, third);
        }
    }
}
