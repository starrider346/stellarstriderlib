using System;

namespace StellarStriderLib.Core
{
    internal static class CategoryLayout
    {
        internal static Float2[] Arrange(Float2[] sizes, float width, float spacing, float rowSpacing,
            out float height)
        {
            var positions = new Float2[sizes.Length];
            var x = 0f;
            var y = 0f;
            var rowHeight = 0f;
            var rowStart = 0;
            for (var index = 0; index < sizes.Length; index++)
            {
                var itemWidth = Math.Min(width, sizes[index].X);
                if (index > rowStart && x + itemWidth > width + .01f)
                {
                    CentreRow(rowStart, index);
                    y += rowHeight + rowSpacing;
                    x = 0;
                    rowHeight = 0;
                    rowStart = index;
                }

                positions[index] = new Float2(x, y);
                x += itemWidth + spacing;
                rowHeight = Math.Max(rowHeight, sizes[index].Y);
            }

            CentreRow(rowStart, sizes.Length);
            height = sizes.Length == 0 ? 0 : y + rowHeight;
            return positions;

            void CentreRow(int start, int end)
            {
                for (var index = start; index < end; index++)
                {
                    positions[index] = new Float2(positions[index].X,
                        positions[index].Y + (rowHeight - sizes[index].Y) / 2);
                }
            }
        }
    }
}
