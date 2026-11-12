namespace QuickHull.Geometry
{
    public static class PointSetGenerator
    {
        private const ulong SplitMixIncrement = 0x9E3779B97F4A7C15UL;

        public static Point2D[] GenerateUniform(int pointCount, int seed)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(pointCount);

            return GenerateUniformRange(0, pointCount, seed);
        }

        public static Point2D[] GenerateUniformRange(int startIndex, int pointCount, int seed)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(pointCount);


            if (startIndex > int.MaxValue - pointCount)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), "The requested point range exceeds the supported index range.");
            }

            var points = new Point2D[pointCount];
            var seedValue = unchecked((ulong)(long)seed);

            for (var index = 0; index < pointCount; index++)
            {
                var pointIndex = (ulong)(startIndex + index);
                var xValue = Mix(unchecked(seedValue + (SplitMixIncrement * (pointIndex * 2 + 1))));
                var yValue = Mix(unchecked(seedValue + (SplitMixIncrement * (pointIndex * 2 + 2))));
                points[index] = new Point2D(ToUnitInterval(xValue), ToUnitInterval(yValue));
            }

            return points;
        }

        private static ulong Mix(ulong value)
        {
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);

            return value ^ (value >> 31);
        }

        private static double ToUnitInterval(ulong value)
        {
            return (value >> 11) * (1.0 / (1UL << 53));
        }
    }
}
