namespace Euclid

open Euclid.EuclidErrors

/// When Euclid is opened this module will be auto-opened.
/// It only contains extension members for type Polyline2D that use a BVH2D.
[<AutoOpen>]
module AutoOpenPolyline2DBVH =

  type Polyline2D with

    /// <summary>Tries to find a self intersection in a Polyline2D, using a BVH2D over the bounding rectangles of its segments.
    /// A closed Polyline2D is not reported as a self intersection.
    /// Two non-adjacent segments that just touch are reported.
    /// If several pairs of segments intersect, the pair with the lowest first index, then the lowest second index is returned.
    /// This is the same result as Polyline2D.tryFindSelfIntersectionWithBRect gives.
    /// Building the tree and a dual tree traversal take about O(n log n), instead of the O(n²) of testing every pair of segments.
    /// But unlike the Euclid functions this does not stop at the first intersection, it always tests all candidate pairs.
    /// On .NET this is faster than both Polyline2D.tryFindSelfIntersection and Polyline2D.tryFindSelfIntersectionWithBRect
    /// from about 200 points on. At 1,000 points it is about 7 times faster than tryFindSelfIntersection.
    /// On Fable JavaScript, in a program that also runs other code, it is faster than tryFindSelfIntersection
    /// from about 200 points on, and faster than tryFindSelfIntersectionWithBRect from about 1,000 points on.
    /// But when tryFindSelfIntersection is about the only code a JavaScript program runs, V8 optimizes it so well
    /// that it stays faster up to 1,000 to 2,000 points.
    /// See Test/BenchPolylineSelfIntersection in the repository for the measurements.</summary>
    /// <param name="pl">The 2D polyline to check.</param>
    /// <returns>The intersection point and the indices of the two intersecting segments.
    /// None if there is no self intersection or if the polyline has fewer than 3 points.</returns>
    static member tryFindSelfIntersectionWithBvh (pl: Polyline2D) : Option<Pt * int * int> =
        let xys = pl.XYs
        let segmentCount = xys.Count / 2 - 1
        if segmentCount < 2 then
            None // a polyline with fewer than two segments cannot intersect itself
        else
            let lastSegment = segmentCount - 1
            let isClosed = pl.IsAlmostClosed 1e-7

            let intersect a b =
                let ia = 2 * a
                let ib = 2 * b
                XLineXY.tryIntersect(
                    xys.[ia], xys.[ia + 1], xys.[ib], xys.[ib + 1],
                    xys.[ia + 2] - xys.[ia], xys.[ia + 3] - xys.[ia + 1],
                    xys.[ib + 2] - xys.[ib], xys.[ib + 3] - xys.[ib + 1])

            // The items are the segment indices, so ClosePairs passes segment indices with a < b.
            // A squared distance of 0.0 reports the pair, infinity rejects it.
            let sqDist a b =
                if b = a + 1 || (isClosed && a = 0 && b = lastSegment) then
                    infinity // adjacent segments share a vertex
                else
                    match intersect a b with
                    | ValueSome _ -> 0.0
                    | ValueNone -> infinity

            let segments = Array.init segmentCount id
            let tree =
                BVH2D.create (segments, fun s ->
                    BRect.createXY(xys.[2 * s], xys.[2 * s + 1], xys.[2 * s + 2], xys.[2 * s + 3]))
            let pairs = tree.ClosePairs(0.0, sqDist)
            if pairs.Count = 0 then
                None
            else
                let mutable first = pairs.[0]
                for p in pairs do
                    if p.IdxA < first.IdxA || (p.IdxA = first.IdxA && p.IdxB < first.IdxB) then
                        first <- p
                match intersect first.IdxA first.IdxB with
                | ValueSome pt -> Some (pt, first.IdxA, first.IdxB)
                | ValueNone -> None


/// When Euclid is opened this module will be auto-opened.
/// It only contains extension members for type Polyline3D that use a BVH.
[<AutoOpen>]
module AutoOpenPolyline3DBVH =

  type Polyline3D with

    /// <summary>Tries to find a self intersection in a Polyline3D, using a BVH over the bounding boxes of its segments.
    /// Two non-adjacent segments intersect if they come closer to each other than the maxSkewDistance.
    /// Segments within 0.25 degrees of being parallel are not tested, so overlapping collinear segments are not found.
    /// A closed Polyline3D is not reported as a self intersection.
    /// If several pairs of segments intersect, the pair with the lowest first index, then the lowest second index is returned.
    /// Building the tree and a dual tree traversal take about O(n log n), instead of the O(n²) of testing every pair of segments.
    /// But unlike a brute force search this does not stop at the first intersection, it always tests all candidate pairs.
    /// On .NET this is faster than testing every pair of segments from about 50 points on,
    /// on Fable JavaScript from about 100 points on.
    /// See Test/BenchPolylineSelfIntersection in the repository for the measurements.</summary>
    /// <param name="pl">The 3D polyline to check.</param>
    /// <param name="maxSkewDistance">The maximum distance between two segments at their closest approach,
    ///  for them to still count as intersecting. Must not be negative. Optional, 1e-6 by default.</param>
    /// <returns>The point on the first segment where the two segments are closest, and the indices of the two intersecting segments.
    /// None if there is no self intersection or if the polyline has fewer than 3 points.</returns>
    static member tryFindSelfIntersectionWithBvh (pl: Polyline3D, [<OPT;DEF(1e-6)>] maxSkewDistance: float) : Option<Pnt * int * int> =
        if maxSkewDistance < 0.0 then
            fail $"Polyline3D.tryFindSelfIntersectionWithBvh: maxSkewDistance {maxSkewDistance} must not be negative."
        let xyzs = pl.XYZs
        let segmentCount = xyzs.Count / 3 - 1
        if segmentCount < 2 then
            None // a polyline with fewer than two segments cannot intersect itself
        else
            let lastSegment = segmentCount - 1
            let isClosed = pl.IsAlmostClosed 1e-7

            let intersect a b =
                let ia = 3 * a
                let ib = 3 * b
                XLineXYZ.tryIntersect(
                    xyzs.[ia], xyzs.[ia + 1], xyzs.[ia + 2], xyzs.[ib], xyzs.[ib + 1], xyzs.[ib + 2],
                    xyzs.[ia + 3] - xyzs.[ia], xyzs.[ia + 4] - xyzs.[ia + 1], xyzs.[ia + 5] - xyzs.[ia + 2],
                    xyzs.[ib + 3] - xyzs.[ib], xyzs.[ib + 4] - xyzs.[ib + 1], xyzs.[ib + 5] - xyzs.[ib + 2],
                    maxSkewDistance, Tangent.``0.25``, 1e-6)

            // The items are the segment indices, so ClosePairs passes segment indices with a < b.
            // A squared distance of 0.0 reports the pair, infinity rejects it.
            let sqDist a b =
                if b = a + 1 || (isClosed && a = 0 && b = lastSegment) then
                    infinity // adjacent segments share a vertex
                else
                    match intersect a b with
                    | ValueSome _ -> 0.0
                    | ValueNone -> infinity

            let segments = Array.init segmentCount id
            let tree =
                BVH.create (segments, fun s ->
                    let i = 3 * s
                    let ax, ay, az = xyzs.[i], xyzs.[i + 1], xyzs.[i + 2]
                    let bx, by, bz = xyzs.[i + 3], xyzs.[i + 4], xyzs.[i + 5]
                    BBox.createUnchecked(min ax bx, min ay by, min az bz, max ax bx, max ay by, max az bz))
            // Segments whose boxes are farther apart than the skew distance cannot come closer than it:
            let pairs = tree.ClosePairs(maxSkewDistance, sqDist)
            if pairs.Count = 0 then
                None
            else
                let mutable first = pairs.[0]
                for p in pairs do
                    if p.IdxA < first.IdxA || (p.IdxA = first.IdxA && p.IdxB < first.IdxB) then
                        first <- p
                match intersect first.IdxA first.IdxB with
                | ValueSome pt -> Some (pt, first.IdxA, first.IdxB)
                | ValueNone -> None
