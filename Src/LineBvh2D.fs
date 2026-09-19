namespace Euclid

open Euclid.EuclidErrors

/// A result of a closest pair search in a LineBvh2D.
/// Holds the indices of the two lines (into the input array) and the distance between them.
/// An alias for BvhPair.
type LinePair2D = BvhPair

/// <summary>A static Bounding Volume Hierarchy (BVH) over 2D lines built from Euclid bounding rectangles (BRect).
/// A thin wrapper around the generic Bvh2D of Line2D that measures distances between
/// the exact line segments via XLine2D.getSqDistance.
/// The tree is built once from an array of Line2D and is then immutable.
/// It is well suited to unevenly distributed input because the tree adapts to the actual
/// bounding rectangles of the lines instead of subdividing space uniformly (as a quadtree or grid would).
/// Typical queries, such as finding the closest line or all pairs of lines closer than a tolerance,
/// run in about O(log n) per line instead of O(n) for a brute force scan.</summary>
type LineBvh2D private (bvh: Bvh2D<Line2D>) =

    /// The exact squared distance between two finite 2D lines.
    static let sqDist (a: Line2D) (b: Line2D) : float =
        XLine2D.getSqDistance (a, b)

    /// The default maximum amount of lines per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// The underlying generic Bvh2D of Line2D.
    member _.Tree = bvh

    /// The input lines this LineBvh2D was built from. Do not mutate this array.
    member _.Lines = bvh.Items

    /// The count of lines in this LineBvh2D.
    member _.Count = bvh.Count

    /// The axis aligned bounding rectangle around all lines in this LineBvh2D.
    member _.Rectangle = bvh.Rectangle

    /// <summary>Builds a LineBvh2D from the given lines.
    /// The tree is built top-down by splitting at the median of the line-rectangle centers
    /// along the axis where those centers have the greatest spread.</summary>
    /// <param name="lines">The 2D lines to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of lines per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable LineBvh2D.</returns>
    static member create (lines: Line2D[], [<OPT;DEF(0)>] leafSize: int) : LineBvh2D =
        if isNull lines then fail "LineBvh2D.create: lines array is null."
        if lines.Length = 0 then fail "LineBvh2D.create: lines array is empty."
        LineBvh2D (Bvh2D.create (lines, BRect.createFromLine, leafSize))

    /// <summary>Finds the closest line in the tree to the given query line.
    /// Uses branch and bound: subtrees whose bounding rectangle is farther away
    /// than the best distance found so far are skipped.</summary>
    /// <param name="query">The 2D line to search the closest line for.</param>
    /// <param name="skipIdx">An index into the input lines array to exclude from the search.
    ///  Use this to find the nearest neighbor of a line that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest line in the input array and the distance to it.</returns>
    member _.ClosestLine (query: Line2D, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        bvh.ClosestItem (BRect.createFromLine query, sqDist query, skipIdx)

    /// <summary>Finds the closest line in the tree to the given 2D point.
    /// The distance is measured to the exact (finite) line segments, while the
    /// bounding rectangles are used for branch and bound pruning.</summary>
    /// <param name="pt">The 2D point to search the closest line for.</param>
    /// <param name="skipIdx">An index into the input lines array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest line in the input array and the distance from the point to it.</returns>
    member _.ClosestLine (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        bvh.ClosestItem (pt, (fun (ln: Line2D) -> ln.SqDistanceToPt pt), skipIdx)

    /// <summary>Finds the point on any line in the tree that is closest to the given 2D point.</summary>
    /// <param name="pt">The 2D point to search the closest point for.</param>
    /// <returns>The closest point on the closest line.</returns>
    member lb.ClosestPoint (pt: Pt) : Pt =
        let i, _ = lb.ClosestLine pt
        bvh.Items.[i].ClosestPoint pt

    /// <summary>Finds the pair of closest lines among all lines in the tree.
    /// For every line the nearest neighbor is searched with branch and bound pruning.</summary>
    /// <returns>A LinePair2D with the indices of the two closest lines and their distance.</returns>
    member _.ClosestPair () : LinePair2D =
        if bvh.Count < 2 then fail "LineBvh2D.ClosestPair: needs at least two lines."
        bvh.ClosestPair sqDist

    /// <summary>For every line in the tree finds its nearest neighbor line.</summary>
    /// <returns>An array of LinePair2D. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of line i as IdxB and the distance between them.</returns>
    member _.NearestNeighbors () : LinePair2D[] =
        if bvh.Count < 2 then fail "LineBvh2D.NearestNeighbors: needs at least two lines."
        bvh.NearestNeighbors sqDist

    /// <summary>Finds all pairs of lines that are closer to each other than the given maximum distance.
    /// Uses a dual tree traversal: pairs of subtrees whose bounding rectangles are farther apart
    /// than the maximum distance are skipped entirely.</summary>
    /// <param name="maxDistance">The maximum distance between two lines for the pair to be reported.</param>
    /// <returns>A ResizeArray of LinePair2D, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member _.ClosePairs (maxDistance: float) : ResizeArray<LinePair2D> =
        if maxDistance < 0.0 then fail $"LineBvh2D.ClosePairs: maxDistance {maxDistance} must not be negative."
        bvh.ClosePairs (maxDistance, sqDist)

    /// <summary>Finds the indices of all lines whose bounding rectangle is closer to the given
    /// axis aligned bounding rectangle than the given tolerance.</summary>
    /// <param name="rect">The axis aligned bounding rectangle to search in.</param>
    /// <param name="tolerance">The tolerance distance around the rectangle. Must not be negative. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found lines in the input array.</returns>
    member _.LinesInRect (rect: BRect, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        bvh.ItemsInRect (rect, tolerance)

    /// <summary>Finds the indices of all lines whose bounding rectangle is closer to the given
    /// 2D point than the given tolerance.</summary>
    /// <param name="pt">The 2D point to search around.</param>
    /// <param name="tolerance">The tolerance distance around the point. Must not be negative. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found lines in the input array.</returns>
    member _.LinesNearPoint (pt: Pt, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        bvh.ItemsNearPoint (pt, tolerance)

    /// <summary>Lazily enumerates all lines in the tree ordered by their exact distance to the given
    /// query line, from the closest to the farthest.
    /// The tree is walked best first, with the bounding rectangle distances as lower bounds, so only the
    /// part of it that is closer than the last line taken is ever visited. Taking just the first entry
    /// costs about as much as ClosestLine, taking all of them sorts the whole tree.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="query">The 2D line to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input lines array to exclude from the enumeration.
    ///  Use this to enumerate the neighbors of a line that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each line in the input array and its distance to the
    /// query line, in order of increasing distance.</returns>
    member _.LinesByDistance (query: Line2D, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        bvh.ItemsByDistance (BRect.createFromLine query, sqDist query, skipIdx)

    /// <summary>Lazily enumerates all lines in the tree ordered by their exact distance to the given
    /// 2D point, from the closest to the farthest.
    /// The tree is walked best first, with the bounding rectangle distances as lower bounds, so only the
    /// part of it that is closer than the last line taken is ever visited.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 2D point to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input lines array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each line in the input array and its distance to the
    /// point, in order of increasing distance.</returns>
    member _.LinesByDistance (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        bvh.ItemsByDistance (pt, (fun (ln: Line2D) -> ln.SqDistanceToPt pt), skipIdx)
