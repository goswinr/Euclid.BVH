namespace Euclid

open System
open Euclid
open Euclid.EuclidErrors

/// Shorthand for the OptionalAttribute on method arguments.
type internal OPT = Runtime.InteropServices.OptionalAttribute

/// Shorthand for the DefaultParameterValueAttribute on method arguments.
type internal DEF = Runtime.InteropServices.DefaultParameterValueAttribute

/// A result of a closest pair search in a LineBvh.
/// Holds the indices of the two lines (into the input array) and the distance between them.
[<Struct>]
type LinePair = {
    /// The index of the first line in the input array of the LineBvh.
    IdxA: int
    /// The index of the second line in the input array of the LineBvh.
    IdxB: int
    /// The distance between the two lines.
    Distance: float
    }

/// An internal node of the LineBvh tree, stored in a flattened array.
/// If Count is greater than 0 the node is a leaf that owns Count line indices starting at LeftOrStart
/// in the LineBvh.LineIndices array.
/// Otherwise LeftOrStart and RightChild are the array indices of the two child nodes.
[<Struct; NoEquality; NoComparison>]
type internal BvhNode = {
    /// The axis aligned bounding box of everything below this node.
    Box: BBox
    /// For a leaf node the start index into LineBvh.LineIndices, otherwise the index of the left child node.
    LeftOrStart: int
    /// The index of the right child node. Unused (-1) for leaf nodes.
    RightChild: int
    /// The count of lines in a leaf node. 0 or negative for internal nodes.
    Count: int
    }

/// <summary>A static Bounding Volume Hierarchy (BVH) over 3D lines built from Euclid axis aligned bounding boxes (BBox).
/// The tree is built once from an array of Line3D and is then immutable.
/// It is well suited to unevenly distributed input because the tree adapts to the actual
/// bounding boxes of the lines instead of subdividing space uniformly (as an octree or grid would).
/// Typical queries, such as finding the closest line or all pairs of lines closer than a tolerance,
/// run in about O(log n) per line instead of O(n) for a brute force scan.</summary>
type LineBvh private (lines: Line3D[], lineIndices: int[], nodes: BvhNode[], root: int) =

    /// The default maximum amount of lines per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// Returns the squared distance between two axis aligned bounding boxes.
    /// Returns 0.0 if they overlap or touch.
    static member inline internal sqBoxDist (a: BBox) (b: BBox) : float =
        let inline axis aMin aMax bMin bMax =
            if   bMin > aMax then bMin - aMax
            elif aMin > bMax then aMin - bMax
            else 0.0
        let dx = axis a.MinX a.MaxX b.MinX b.MaxX
        let dy = axis a.MinY a.MaxY b.MinY b.MaxY
        let dz = axis a.MinZ a.MaxZ b.MinZ b.MaxZ
        dx*dx + dy*dy + dz*dz

    /// The input lines this LineBvh was built from. Do not mutate this array.
    member _.Lines = lines

    /// The permutation of line indices as referenced by the leaf nodes. Do not mutate this array.
    member internal _.LineIndices = lineIndices

    /// The count of lines in this LineBvh.
    member _.Count = lines.Length

    /// The axis aligned bounding box around all lines in this LineBvh.
    member _.Box = nodes.[root].Box

    /// <summary>Builds a LineBvh from the given lines.
    /// The tree is built top-down by splitting at the median of the line-box centers
    /// along the longest axis of the current bounding box.</summary>
    /// <param name="lines">The 3D lines to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of lines per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable LineBvh.</returns>
    static member create (lines: Line3D[], [<OPT;DEF(0)>] leafSize: int) : LineBvh =
        if isNull lines then fail "LineBvh.create: lines array is null."
        if lines.Length = 0 then fail "LineBvh.create: lines array is empty."
        let leafSize = if leafSize < 1 then LineBvh.DefaultLeafSize else leafSize
        let n = lines.Length
        let boxes = Array.init n (fun i -> BBox.createFromLine lines.[i])
        let cx = Array.init n (fun i -> (boxes.[i].MinX + boxes.[i].MaxX) * 0.5)
        let cy = Array.init n (fun i -> (boxes.[i].MinY + boxes.[i].MaxY) * 0.5)
        let cz = Array.init n (fun i -> (boxes.[i].MinZ + boxes.[i].MaxZ) * 0.5)
        let idx = Array.init n id
        let nodes = ResizeArray<BvhNode>(2 * n / leafSize + 2)

        let boxOf start count =
            let mutable b = boxes.[idx.[start]]
            for i = start + 1 to start + count - 1 do
                b <- b.Union boxes.[idx.[i]]
            b

        // recursively builds the node for idx.[start .. start+count-1] and returns its index in the nodes list
        let rec build start count : int =
            let box = boxOf start count
            let nodeIdx = nodes.Count
            if count <= leafSize then
                nodes.Add { Box = box; LeftOrStart = start; RightChild = -1; Count = count }
                nodeIdx
            else
                // split at the median of the box centers along the longest axis of this node's box:
                let sizeX = box.MaxX - box.MinX
                let sizeY = box.MaxY - box.MinY
                let sizeZ = box.MaxZ - box.MinZ
                let centers =
                    if   sizeX >= sizeY && sizeX >= sizeZ then cx
                    elif sizeY >= sizeZ                   then cy
                    else                                       cz
                Array.Sort (idx, start, count, { new Collections.Generic.IComparer<int> with
                                                    member _.Compare (a, b) = compare centers.[a] centers.[b] })
                let mid = count / 2
                nodes.Add { Box = box; LeftOrStart = -1; RightChild = -1; Count = 0 } // placeholder, patched below
                let left = build start mid
                let right = build (start + mid) (count - mid)
                nodes.[nodeIdx] <- { Box = box; LeftOrStart = left; RightChild = right; Count = 0 }
                nodeIdx

        let root = build 0 n
        LineBvh (lines, idx, nodes.ToArray(), root)

    /// <summary>Finds the closest line in the tree to the given query line.
    /// Uses branch and bound: subtrees whose bounding box is farther away
    /// than the best distance found so far are skipped.</summary>
    /// <param name="query">The 3D line to search the closest line for.</param>
    /// <param name="skipIdx">An index into the input lines array to exclude from the search.
    ///  Use this to find the nearest neighbor of a line that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest line in the input array and the distance to it.</returns>
    member bvh.ClosestLine (query: Line3D, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let queryBox = BBox.createFromLine query
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if LineBvh.sqBoxDist queryBox node.Box < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let li = lineIndices.[i]
                        if li <> skipIdx then
                            let sqD = XLine3D.getSqDistance (query, lines.[li])
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- li
                else
                    // visit the closer child first for better pruning:
                    let dLeft = LineBvh.sqBoxDist queryBox nodes.[node.LeftOrStart].Box
                    let dRight = LineBvh.sqBoxDist queryBox nodes.[node.RightChild].Box
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "LineBvh.ClosestLine: no line found. Tree has only the skipped line?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the pair of closest lines among all lines in the tree.
    /// For every line the nearest neighbor is searched with branch and bound pruning.</summary>
    /// <returns>A LinePair with the indices of the two closest lines and their distance.</returns>
    member bvh.ClosestPair () : LinePair =
        if lines.Length < 2 then fail "LineBvh.ClosestPair: needs at least two lines."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to lines.Length - 1 do
            let struct (j, d) = bvh.ClosestLine (lines.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>For every line in the tree finds its nearest neighbor line.</summary>
    /// <returns>An array of LinePair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of line i as IdxB and the distance between them.</returns>
    member bvh.NearestNeighbors () : LinePair[] =
        if lines.Length < 2 then fail "LineBvh.NearestNeighbors: needs at least two lines."
        Array.init lines.Length (fun i ->
            let struct (j, d) = bvh.ClosestLine (lines.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// <summary>Finds all pairs of lines that are closer to each other than the given maximum distance.
    /// Uses a dual tree traversal: pairs of subtrees whose bounding boxes are farther apart
    /// than the maximum distance are skipped entirely.</summary>
    /// <param name="maxDistance">The maximum distance between two lines for the pair to be reported.</param>
    /// <returns>A ResizeArray of LinePair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float) : ResizeArray<LinePair> =
        if maxDistance < 0.0 then fail $"LineBvh.ClosePairs: maxDistance {maxDistance} must not be negative."
        let sqMaxDist = maxDistance * maxDistance
        let result = ResizeArray<LinePair>()
        let inline testPair a b =
            if a <> b then
                let i = min a b
                let j = max a b
                let sqD = XLine3D.getSqDistance (lines.[i], lines.[j])
                if sqD <= sqMaxDist then
                    result.Add { IdxA = i; IdxB = j; Distance = sqrt sqD }
        let rec searchPair na nb =
            let a = nodes.[na]
            let b = nodes.[nb]
            if na = nb then // a self pair: recurse into all child combinations, each unordered pair only once
                if a.Count > 0 then // leaf: test each pair only once
                    for i = a.LeftOrStart to a.LeftOrStart + a.Count - 1 do
                        for j = i + 1 to a.LeftOrStart + a.Count - 1 do
                            testPair lineIndices.[i] lineIndices.[j]
                else
                    searchPair a.LeftOrStart a.LeftOrStart
                    searchPair a.RightChild a.RightChild
                    searchPair a.LeftOrStart a.RightChild
            elif LineBvh.sqBoxDist a.Box b.Box <= sqMaxDist then
                match a.Count > 0, b.Count > 0 with
                | true, true -> // both leaves
                    for i = a.LeftOrStart to a.LeftOrStart + a.Count - 1 do
                        for j = b.LeftOrStart to b.LeftOrStart + b.Count - 1 do
                            testPair lineIndices.[i] lineIndices.[j]
                | false, _ ->
                    searchPair a.LeftOrStart nb
                    searchPair a.RightChild nb
                | true, false ->
                    searchPair na b.LeftOrStart
                    searchPair na b.RightChild
        searchPair root root
        result

    /// <summary>Finds the indices of all lines whose bounding box is closer to the given
    /// axis aligned bounding box than the given tolerance.</summary>
    /// <param name="box">The axis aligned bounding box to search in.</param>
    /// <param name="tolerance">The tolerance distance around the box. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found lines in the input array.</returns>
    member _.LinesInBox (box: BBox, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if LineBvh.sqBoxDist box node.Box <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let li = lineIndices.[i]
                        if LineBvh.sqBoxDist box (BBox.createFromLine lines.[li]) <= sqTol then
                            result.Add li
                else
                    search node.LeftOrStart
                    search node.RightChild
        search root
        result
