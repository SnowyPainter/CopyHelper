using System.Windows;

namespace CopyHelper.Models
{
    public sealed class SegmentedRegion
    {
        public SegmentedRegion(RegionType type, Rect bounds)
        {
            Type = type;
            Bounds = bounds;
        }

        public RegionType Type { get; }
        public Rect Bounds { get; }
    }
}
