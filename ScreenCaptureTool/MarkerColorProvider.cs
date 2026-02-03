using System;
using System.Collections.Generic;
using System.Drawing;

namespace ScreenCaptureTool
{
    public sealed class MarkerColorProvider
    {
        private readonly List<Color> palette;
        private int index;

        public MarkerColorProvider()
            : this(GetDefaultPalette())
        {
        }

        public MarkerColorProvider(IEnumerable<Color> palette)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            this.palette = new List<Color>();
            foreach (Color color in palette)
            {
                if (color == Color.Red || color == Color.Green)
                {
                    continue;
                }
                this.palette.Add(color);
            }

            if (this.palette.Count == 0)
            {
                throw new ArgumentException("Palette must contain at least one non-red, non-green color.", nameof(palette));
            }
        }

        public Color NextColor()
        {
            Color color = palette[index];
            index = (index + 1) % palette.Count;
            return color;
        }

        private static IEnumerable<Color> GetDefaultPalette()
        {
            return new[]
            {
                Color.Blue,
                Color.Gold,
                Color.Purple,
                Color.Orange,
                Color.Cyan,
                Color.HotPink
            };
        }
    }
}
