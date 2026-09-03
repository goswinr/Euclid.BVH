namespace Euclid

open System
open Euclid
open Euclid.EuclidErrors

/// A result of a closest pair search in a LineBvh.
/// Holds the indices of the two lines (into the input array) and the distance between them.
/// An alias for BvhPair.
type LinePair = BvhPair

/// <summary>A static Bounding Volume Hierarchy (BVH) over 3D lines built from Euclid axis aligned bounding boxes (BBox).
/// A thin wrapper around the generic Bvh of Line3D that measures distances between
/// the exact line segments via XLine3D.getSqDistance.
/// The tree is built once from an array of Line3D and is then immutable.
/// It is well suited to unevenly distributed input because the tree adapts to the actual
/// bounding boxes of the lines instead of subdividing space uniformly (as an octree or grid would).
/// Typical queries, such as finding the closest line or all pairs of lines closer than a tolerance,
/// run in about O(log n) per line instead of O(n) for a brute force scan.</summary>
type LineBvh private (bvh: Bvh<Line3D>) =

    /// The exact squared distance between two finite 3D lines.
    static let sqDist (a: Line3D) (b: Line3D) : float =
        XLine3D.getSqDistance (a, b)

    /// The default maximum amount of lines per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// The underlying generic Bvh of Line3D.
    member _.Tree = bvh

    /// The input lines this LineBvh was built from. Do not mutate this array.
    member _.Lines = bvh.Items

    /// The count of lines in this LineBvh.
    member _.Count = bvh.Count

    /// The axis aligned bounding box around all lines in this LineBvh.
    member _.Box = bvh.Box

    /// <summary>Builds a LineBvh from the given lines.
    /// The tree is built top-down by splitting at the median of the line-box centers
    /// along the longest axis of the current bounding box.</summary>
    /// <param name="lines">The 3D lines to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of lines per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable LineBvh.</returns>
    static member create (lines: Line3D[], [<OPT;DEF(0)>] leafSize: int) : LineBvh =
        if isNull lines then fail "LineBvh.create: lines array is null."
        if lines.Length = 0 then fail "LineBvh.create: lines array is empty."
        LineBvh (Bvh.create (lines, BBox.createFromLine, leafSize))

    /// <summary>Finds the closest line in the tree to the given query line.
    /// Uses branch and bound: subtrees whose bounding box is farther away
    /// than the best distance found so far are skipped.</summary>
    /// <param name="query">The 3D line to search the closest line for.</param>
    /// <param name="skipIdx">An index into the input lines array to exclude from the search.
    ///  Use this to find the nearest neighbor of a line that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest line in the input array and the distance to it.</returns>
    member _.ClosestLine (query: Line3D, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        bvh.ClosestItem (BBox.createFromLine query, sqDist query, skipIdx)

    /// <summary>Finds the pair of closest lines among all lines in the tree.
    /// For every line the nearest neighbor is searched with branch and bound pruning.</summary>
    /// <returns>A LinePair with the indices of the two closest lines and their distance.</returns>
    member _.ClosestPair () : LinePair =
        if bvh.Count < 2 then fail "LineBvh.ClosestPair: needs at least two lines."
        bvh.ClosestPair sqDist

    /// <summary>For every line in the tree finds its nearest neighbor line.</summary>
    /// <returns>An array of LinePair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of line i as IdxB and the distance between them.</returns>
    member _.NearestNeighbors () : LinePair[] =
        if bvh.Count < 2 then fail "LineBvh.NearestNeighbors: needs at least two lines."
        bvh.NearestNeighbors sqDist

    /// <summary>Finds all pairs of lines that are closer to each other than the given maximum distance.
    /// Uses a dual tree traversal: pairs of subtrees whose bounding boxes are farther apart
    /// than the maximum distance are skipped entirely.</summary>
    /// <param name="maxDistance">The maximum distance between two lines for the pair to be reported.</param>
    /// <returns>A ResizeArray of LinePair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member _.ClosePairs (maxDistance: float) : ResizeArray<LinePair> =
        if maxDistance < 0.0 then fail $"LineBvh.ClosePairs: maxDistance {maxDistance} must not be negative."
        bvh.ClosePairs (maxDistance, sqDist)

    /// <summary>Finds the indices of all lines whose bounding box is closer to the given
    /// axis aligned bounding box than the given tolerance.</summary>
    /// <param name="box">The axis aligned bounding box to search in.</param>
    /// <param name="tolerance">The tolerance distance around the box. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found lines in the input array.</returns>
    member _.LinesInBox (box: BBox, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        bvh.ItemsInBox (box, tolerance)
