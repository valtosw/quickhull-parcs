namespace QuickHull.Geometry
{
    public static class QuickHullAlgorithm
    {
        private const double DefaultRelativeTolerance = 1e-12;

        public static IReadOnlyList<Point2D> ComputeHull(
            IReadOnlyList<Point2D> points,
            double relativeTolerance = DefaultRelativeTolerance,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(points);
            ValidateRelativeTolerance(relativeTolerance);

            var uniquePoints = new HashSet<Point2D>();

            for (var index = 0; index < points.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = points[index];
                ValidatePoint(point, index);
                uniquePoints.Add(point);
            }

            if (uniquePoints.Count <= 1)
            {
                return uniquePoints.ToArray();
            }

            var orderedPoints = uniquePoints.ToArray();
            var minimum = orderedPoints.MinBy(point => (point.X, point.Y));
            var maximum = orderedPoints.MaxBy(point => (point.X, point.Y));

            if (minimum == maximum)
            {
                return [minimum];
            }

            var tolerance = CalculateTolerance(orderedPoints, relativeTolerance);
            var upperCandidates = FilterOutsideEdge(orderedPoints, minimum, maximum, tolerance);
            var lowerCandidates = FilterOutsideEdge(orderedPoints, maximum, minimum, tolerance);
            var hull = new List<Point2D>(orderedPoints.Length);

            hull.Add(minimum);
            hull.AddRange(ComputeSubproblemChain(new QuickHullSubproblem(minimum, maximum, upperCandidates, tolerance), cancellationToken));
            hull.Add(maximum);
            hull.AddRange(ComputeSubproblemChain(new QuickHullSubproblem(maximum, minimum, lowerCandidates, tolerance), cancellationToken));

            return hull;
        }

        public static IReadOnlyList<QuickHullSubproblem> CreateInitialSubproblems(
            IReadOnlyList<Point2D> points,
            double relativeTolerance = DefaultRelativeTolerance)
        {
            ArgumentNullException.ThrowIfNull(points);
            ValidateRelativeTolerance(relativeTolerance);

            var uniquePoints = new HashSet<Point2D>();

            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                ValidatePoint(point, index);
                uniquePoints.Add(point);
            }

            if (uniquePoints.Count <= 1)
            {
                return [];
            }

            var orderedPoints = uniquePoints.ToArray();
            var minimum = orderedPoints.MinBy(point => (point.X, point.Y));
            var maximum = orderedPoints.MaxBy(point => (point.X, point.Y));

            if (minimum == maximum)
            {
                return [];
            }

            var tolerance = CalculateTolerance(orderedPoints, relativeTolerance);

            return
            [
                new QuickHullSubproblem(minimum, maximum, FilterOutsideEdge(orderedPoints, minimum, maximum, tolerance), tolerance),
                new QuickHullSubproblem(maximum, minimum, FilterOutsideEdge(orderedPoints, maximum, minimum, tolerance), tolerance)
            ];
        }

        public static QuickHullExpansion Expand(QuickHullSubproblem subproblem)
        {
            ArgumentNullException.ThrowIfNull(subproblem);
            ArgumentNullException.ThrowIfNull(subproblem.Candidates);

            if (subproblem.Candidates.Length == 0)
            {
                throw new ArgumentException("A subproblem must contain candidate points to expand.", nameof(subproblem));
            }

            var pivot = FindFarthestPoint(subproblem);
            var leftCandidates = new List<Point2D>();
            var rightCandidates = new List<Point2D>();

            foreach (var point in subproblem.Candidates)
            {

                if (point == pivot)
                {
                    continue;
                }

                if (CrossProduct(subproblem.Start, pivot, point) > subproblem.Tolerance)
                {
                    leftCandidates.Add(point);
                }

                if (CrossProduct(pivot, subproblem.End, point) > subproblem.Tolerance)
                {
                    rightCandidates.Add(point);
                }
            }

            return new QuickHullExpansion(
                pivot,
                new QuickHullSubproblem(subproblem.Start, pivot, leftCandidates.ToArray(), subproblem.Tolerance),
                new QuickHullSubproblem(pivot, subproblem.End, rightCandidates.ToArray(), subproblem.Tolerance));
        }

        public static IReadOnlyList<Point2D> ComputeSubproblemChain(
            QuickHullSubproblem subproblem,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(subproblem);
            ArgumentNullException.ThrowIfNull(subproblem.Candidates);

            if (subproblem.Candidates.Length == 0)
            {
                return [];
            }

            var chain = new List<Point2D>(subproblem.Candidates.Length);
            var pending = new Stack<TraversalEntry>();
            pending.Push(TraversalEntry.ForSubproblem(subproblem));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = pending.Pop();

                if (entry.Point is { } point)
                {
                    chain.Add(point);
                    continue;
                }

                var current = entry.Subproblem!;

                if (current.Candidates.Length == 0)
                {
                    continue;
                }

                var expansion = Expand(current);
                pending.Push(TraversalEntry.ForSubproblem(expansion.Right));
                pending.Push(TraversalEntry.ForPoint(expansion.Pivot));
                pending.Push(TraversalEntry.ForSubproblem(expansion.Left));
            }

            return chain;
        }

        public static double CrossProduct(Point2D start, Point2D end, Point2D point)
        {
            return ((end.X - start.X) * (point.Y - start.Y))
                - ((end.Y - start.Y) * (point.X - start.X));
        }

        private static Point2D FindFarthestPoint(QuickHullSubproblem subproblem)
        {
            var farthestPoint = subproblem.Candidates[0];
            var maximumDistance = CrossProduct(subproblem.Start, subproblem.End, farthestPoint);

            for (var index = 1; index < subproblem.Candidates.Length; index++)
            {
                var point = subproblem.Candidates[index];
                var distance = CrossProduct(subproblem.Start, subproblem.End, point);

                if (distance > maximumDistance)
                {
                    farthestPoint = point;
                    maximumDistance = distance;
                }
            }

            return farthestPoint;
        }

        private static Point2D[] FilterOutsideEdge(
            IEnumerable<Point2D> points,
            Point2D start,
            Point2D end,
            double tolerance)
        {
            return points
                .Where(point => point != start && point != end && CrossProduct(start, end, point) > tolerance)
                .ToArray();
        }

        private static double CalculateTolerance(IEnumerable<Point2D> points, double relativeTolerance)
        {
            var minimumX = double.PositiveInfinity;
            var maximumX = double.NegativeInfinity;
            var minimumY = double.PositiveInfinity;
            var maximumY = double.NegativeInfinity;

            foreach (var point in points)
            {
                minimumX = Math.Min(minimumX, point.X);
                maximumX = Math.Max(maximumX, point.X);
                minimumY = Math.Min(minimumY, point.Y);
                maximumY = Math.Max(maximumY, point.Y);
            }

            var scale = Math.Max(maximumX - minimumX, maximumY - minimumY);

            if (!double.IsFinite(scale) || scale > Math.Sqrt(double.MaxValue / 4))
            {
                throw new ArgumentOutOfRangeException(nameof(points), "The coordinate range is too large for stable double-precision orientation calculations.");
            }

            var tolerance = relativeTolerance * scale * scale;

            if (!double.IsFinite(tolerance))
            {
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance), "The tolerance is too large for the coordinate range.");
            }

            return tolerance;
        }

        private static void ValidatePoint(Point2D point, int index)
        {

            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                throw new ArgumentException($"Point at index {index} must contain finite coordinates.", nameof(point));
            }
        }

        private static void ValidateRelativeTolerance(double relativeTolerance)
        {

            if (!double.IsFinite(relativeTolerance) || relativeTolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(relativeTolerance), "Tolerance must be finite and non-negative.");
            }
        }

        private sealed record TraversalEntry(QuickHullSubproblem? Subproblem, Point2D? Point)
        {
            public static TraversalEntry ForSubproblem(QuickHullSubproblem subproblem)
            {
                return new TraversalEntry(subproblem, null);
            }

            public static TraversalEntry ForPoint(Point2D point)
            {
                return new TraversalEntry(null, point);
            }
        }
    }
}
