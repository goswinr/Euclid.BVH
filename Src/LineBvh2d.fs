namespace Euclid

open Euclid.EuclidErrors

/// A result of a closest pair search in a LineBvh2d.
type LinePair2d = BvhPair

/// A static Bounding Volume Hierarchy (BVH) over 2D lines built from Euclid bounding rectangles (BRect).
type LineBvh2d private (bvh: Bvh2d<Line2D>) =

    static let sqDist (a: Line2D) (b: Line2D) : float =
        XLine2D.getSqDistance (a, b)

    static member val DefaultLeafSize = 4 with get

    /// The underlying 2D tree.
    member _.Tree = bvh

    /// The input lines this LineBvh2d was built from. Do not mutate this array.
    member _.Lines = bvh.Items

    /// The count of lines in this LineBvh2d.
    member _.Count = bvh.Count

    /// The axis aligned bounding rectangle around all lines in this LineBvh2d.
    member _.Rectangle = bvh.Rectangle

    /// Builds a LineBvh2d from the given lines.
    static member create (lines: Line2D[], [<OPT;DEF(0)>] leafSize: int) : LineBvh2d =
        if isNull lines then fail "LineBvh2d.create: lines array is null."
        if lines.Length = 0 then fail "LineBvh2d.create: lines array is empty."
        LineBvh2d (Bvh2d.create (lines, BRect.createFromLine, leafSize))

    /// Finds the closest line in the tree to the given query line.
    member _.ClosestLine (query: Line2D, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        bvh.ClosestItem (BRect.createFromLine query, sqDist query, skipIdx)

    /// Finds the closest line in the tree to the given 2D point.
    member _.ClosestLine (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        bvh.ClosestItem (pt, (fun (ln: Line2D) -> ln.SqDistanceToPt pt), skipIdx)

    /// Finds the point on any line in the tree that is closest to the given 2D point.
    member lb.ClosestPoint (pt: Pt) : Pt =
        let struct (i, _) = lb.ClosestLine pt
        bvh.Items.[i].ClosestPoint pt

    /// Finds the pair of closest lines among all lines in the tree.
    member _.ClosestPair () : LinePair2d =
        if bvh.Count < 2 then fail "LineBvh2d.ClosestPair: needs at least two lines."
        bvh.ClosestPair sqDist

    /// For every line in the tree finds its nearest neighbor line.
    member _.NearestNeighbors () : LinePair2d[] =
        if bvh.Count < 2 then fail "LineBvh2d.NearestNeighbors: needs at least two lines."
        bvh.NearestNeighbors sqDist

    /// Finds all pairs of lines closer than the given maximum distance.
    member _.ClosePairs (maxDistance: float) : ResizeArray<LinePair2d> =
        if maxDistance < 0.0 then fail $"LineBvh2d.ClosePairs: maxDistance {maxDistance} must not be negative."
        bvh.ClosePairs (maxDistance, sqDist)

    /// Finds all lines whose bounding rectangle is within tolerance of the given rectangle.
    member _.LinesInRect (rect: BRect, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        bvh.ItemsInRect (rect, tolerance)

    /// Finds all lines whose bounding rectangle is within tolerance of the given point.
    member _.LinesNearPoint (pt: Pt, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        bvh.ItemsNearPoint (pt, tolerance)
