namespace QuickHull.Geometry
{
    public sealed record QuickHullSubproblem(
        Point2D Start,
        Point2D End,
        Point2D[] Candidates,
        double Tolerance);

    public sealed record QuickHullExpansion(
        Point2D Pivot,
        QuickHullSubproblem Left,
        QuickHullSubproblem Right);
}
