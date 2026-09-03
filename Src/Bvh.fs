namespace Euclid

open System
open Euclid
open Euclid.EuclidErrors

/// Shorthand for the OptionalAttribute on method arguments.
type internal OPT = Runtime.InteropServices.OptionalAttribute

/// Shorthand for the DefaultParameterValueAttribute on method arguments.
type internal DEF = Runtime.InteropServices.DefaultParameterValueAttribute

/// A result of a closest pair search in a Bvh tree.
/// Holds the indices of the two items (into the input array) and the distance between them.
[<Struct>]
type BvhPair = {
    /// The index of the first item in the input array of the Bvh.
    IdxA: int
    /// The index of the second item in the input array of the Bvh.
    IdxB: int
    /// The distance between the two items.
    Distance: float
    }

/// An internal node of a Bvh tree, stored in a flattened array.
/// If Count is greater than 0 the node is a leaf that owns Count item indices starting at LeftOrStart
/// in the Bvh.ItemIndices array.
/// Otherwise LeftOrStart and RightChild are the array indices of the two child nodes.
[<Struct; NoEquality; NoComparison>]
type internal BvhNode = {
    /// The axis aligned bounding box of everything below this node.
    Box: BBox
    /// For a leaf node the start index into Bvh.ItemIndices, otherwise the index of the left child node.
    LeftOrStart: int
    /// The index of the right child node. Unused (-1) for leaf nodes.
    RightChild: int
    /// The count of items in a leaf node. 0 or negative for internal nodes.
    Count: int
    }

/// An internal module with functions shared by all Bvh instantiations.
module internal BvhUtil =

    /// Returns the squared distance between two axis aligned bounding boxes.
    /// Returns 0.0 if they overlap or touch.
    let inline sqBoxDist (a: BBox) (b: BBox) : float =
        let inline axis aMin aMax bMin bMax =
            if   bMin > aMax then bMin - aMax
            elif aMin > bMax then aMin - bMax
            else 0.0
        let dx = axis a.MinX a.MaxX b.MinX b.MaxX
        let dy = axis a.MinY a.MaxY b.MinY b.MaxY
        let dz = axis a.MinZ a.MaxZ b.MinZ b.MaxZ
        dx*dx + dy*dy + dz*dz

    /// Returns the squared distance between a 3D point and an axis aligned bounding box.
    /// Returns 0.0 if the point is inside or on the box.
    let inline sqBoxPntDist (p: Pnt) (b: BBox) : float =
        let inline axis v bMin bMax =
            if   v < bMin then bMin - v
            elif v > bMax then v - bMax
            else 0.0
        let dx = axis p.X b.MinX b.MaxX
        let dy = axis p.Y b.MinY b.MaxY
        let dz = axis p.Z b.MinZ b.MaxZ
        dx*dx + dy*dy + dz*dz

    /// Builds the flattened node array for the given boxes.
    /// Returns the permutation of item indices, the nodes and the index of the root node.
    let build (boxes: BBox[]) (leafSize: int) : int[] * BvhNode[] * int =
        let n = boxes.Length
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
        let rec buildNode start count : int =
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
                let left = buildNode start mid
                let right = buildNode (start + mid) (count - mid)
                nodes.[nodeIdx] <- { Box = box; LeftOrStart = left; RightChild = right; Count = 0 }
                nodeIdx

        let root = buildNode 0 n
        idx, nodes.ToArray(), root

/// <summary>A generic static Bounding Volume Hierarchy (BVH) over any items,
/// built from Euclid axis aligned bounding boxes (BBox).
/// The tree is built once from an array of items plus a function that returns the bounding box
/// of each item, and is then immutable.
/// It is well suited to unevenly distributed input because the tree adapts to the actual
/// bounding boxes of the items instead of subdividing space uniformly (as an octree or grid would).
/// All queries come in two flavors: box based, where the distance between two items is measured
/// as the distance between their bounding boxes, and exact, where a distance function for the
/// actual items is supplied. The bounding box distance is always a valid lower bound of the
/// exact distance, so it is used for branch and bound pruning in both cases.</summary>
type Bvh<'T> private (items: 'T[], boxes: BBox[], itemIndices: int[], nodes: BvhNode[], root: int) =

    /// The default maximum amount of items per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// The input items this Bvh was built from. Do not mutate this array.
    member _.Items = items

    /// The bounding box of each input item, in the same order as Items. Do not mutate this array.
    member _.Boxes = boxes

    /// The permutation of item indices as referenced by the leaf nodes. Do not mutate this array.
    member internal _.ItemIndices = itemIndices

    /// The count of items in this Bvh.
    member _.Count = items.Length

    /// The axis aligned bounding box around all items in this Bvh.
    member _.Box = nodes.[root].Box

    /// <summary>Builds a Bvh from the given items.
    /// The tree is built top-down by splitting at the median of the item-box centers
    /// along the longest axis of the current bounding box.</summary>
    /// <param name="items">The items to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="getBox">A function returning the axis aligned bounding box of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh.</returns>
    static member create (items: 'T[], getBox: 'T -> BBox, [<OPT;DEF(0)>] leafSize: int) : Bvh<'T> =
        if isNull items then fail "Bvh.create: items array is null."
        if items.Length = 0 then fail "Bvh.create: items array is empty."
        let leafSize = if leafSize < 1 then Bvh<'T>.DefaultLeafSize else leafSize
        let boxes = Array.init items.Length (fun i -> getBox items.[i])
        let idx, nodes, root = BvhUtil.build boxes leafSize
        Bvh<'T> (items, boxes, idx, nodes, root)

    /// <summary>Finds the item in the tree closest to the given query bounding box.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the query box and the item boxes provide lower bounds for
    /// branch and bound pruning: subtrees whose bounding box is farther away from the query box
    /// than the best distance found so far are skipped.</summary>
    /// <param name="queryBox">The axis aligned bounding box of the query geometry.
    ///  It must fully contain the query geometry that sqDistanceTo measures from,
    ///  otherwise subtrees may be pruned incorrectly.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query geometry to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search.
    ///  Use this to find the nearest neighbor of an item that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (queryBox: BBox, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil.sqBoxDist queryBox node.Box < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = sqDistanceTo items.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    // visit the closer child first for better pruning:
                    let dLeft = BvhUtil.sqBoxDist queryBox nodes.[node.LeftOrStart].Box
                    let dRight = BvhUtil.sqBoxDist queryBox nodes.[node.RightChild].Box
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh.ClosestItem: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the item in the tree whose bounding box is closest to the given query box.
    /// The distance between two boxes is 0.0 if they overlap or touch.</summary>
    /// <param name="queryBox">The axis aligned bounding box to search the closest item box for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding box and the distance between the boxes.</returns>
    member bvh.ClosestBox (queryBox: BBox, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        // over-approximate the item boxes as themselves: box distance is exact here
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil.sqBoxDist queryBox node.Box < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil.sqBoxDist queryBox boxes.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let dLeft = BvhUtil.sqBoxDist queryBox nodes.[node.LeftOrStart].Box
                    let dRight = BvhUtil.sqBoxDist queryBox nodes.[node.RightChild].Box
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh.ClosestBox: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the item in the tree closest to the given query point.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the item bounding boxes provide lower bounds for branch and bound pruning:
    /// subtrees whose bounding box is farther away from the point
    /// than the best distance found so far are skipped.</summary>
    /// <param name="pt">The 3D point to search the closest item for.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (pt: Pnt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil.sqBoxPntDist pt node.Box < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = sqDistanceTo items.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    // visit the closer child first for better pruning:
                    let dLeft = BvhUtil.sqBoxPntDist pt nodes.[node.LeftOrStart].Box
                    let dRight = BvhUtil.sqBoxPntDist pt nodes.[node.RightChild].Box
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh.ClosestItem: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the item in the tree whose bounding box is closest to the given query point.
    /// The distance between a point and a box is 0.0 if the point is inside or on the box.</summary>
    /// <param name="pt">The 3D point to search the closest item box for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding box and the distance from the point to that box.</returns>
    member _.ClosestBox (pt: Pnt, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil.sqBoxPntDist pt node.Box < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil.sqBoxPntDist pt boxes.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let dLeft = BvhUtil.sqBoxPntDist pt nodes.[node.LeftOrStart].Box
                    let dRight = BvhUtil.sqBoxPntDist pt nodes.[node.RightChild].Box
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh.ClosestBox: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the pair of closest items among all items in the tree, measured with
    /// the given exact squared distance function. For every item the nearest neighbor
    /// is searched with branch and bound pruning on the bounding boxes.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A BvhPair with the indices of the two closest items and their distance.</returns>
    member bvh.ClosestPair (sqDistance: 'T -> 'T -> float) : BvhPair =
        if items.Length < 2 then fail "Bvh.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Length - 1 do
            let struct (j, d) = bvh.ClosestItem (boxes.[i], sqDistance items.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>Finds the pair of items whose bounding boxes are closest to each other.
    /// The distance between two boxes is 0.0 if they overlap or touch.</summary>
    /// <returns>A BvhPair with the indices of the two items and the distance between their boxes.</returns>
    member bvh.ClosestPair () : BvhPair =
        if items.Length < 2 then fail "Bvh.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Length - 1 do
            let struct (j, d) = bvh.ClosestBox (boxes.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>For every item in the tree finds its nearest neighbor item, measured with
    /// the given exact squared distance function.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>An array of BvhPair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of item i as IdxB and the distance between them.</returns>
    member bvh.NearestNeighbors (sqDistance: 'T -> 'T -> float) : BvhPair[] =
        if items.Length < 2 then fail "Bvh.NearestNeighbors: needs at least two items."
        Array.init items.Length (fun i ->
            let struct (j, d) = bvh.ClosestItem (boxes.[i], sqDistance items.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// <summary>For every item in the tree finds the item whose bounding box is nearest to its own.
    /// The distance between two boxes is 0.0 if they overlap or touch.</summary>
    /// <returns>An array of BvhPair. The entry at index i holds i as IdxA, the index of the
    /// item with the nearest bounding box as IdxB and the distance between the boxes.</returns>
    member bvh.NearestNeighbors () : BvhPair[] =
        if items.Length < 2 then fail "Bvh.NearestNeighbors: needs at least two items."
        Array.init items.Length (fun i ->
            let struct (j, d) = bvh.ClosestBox (boxes.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// Internal worker for both ClosePairs overloads, taking a squared distance function on item indices.
    member private _.ClosePairsByIdx (maxDistance: float, sqDistIdx: int -> int -> float) : ResizeArray<BvhPair> =
        if maxDistance < 0.0 then fail $"Bvh.ClosePairs: maxDistance {maxDistance} must not be negative."
        let sqMaxDist = maxDistance * maxDistance
        let result = ResizeArray<BvhPair>()
        let inline testPair a b =
            if a <> b then
                let i = min a b
                let j = max a b
                let sqD = sqDistIdx i j
                if sqD <= sqMaxDist then
                    result.Add { IdxA = i; IdxB = j; Distance = sqrt sqD }
        let rec searchPair na nb =
            let a = nodes.[na]
            let b = nodes.[nb]
            if na = nb then // a self pair: recurse into all child combinations, each unordered pair only once
                if a.Count > 0 then // leaf: test each pair only once
                    for i = a.LeftOrStart to a.LeftOrStart + a.Count - 1 do
                        for j = i + 1 to a.LeftOrStart + a.Count - 1 do
                            testPair itemIndices.[i] itemIndices.[j]
                else
                    searchPair a.LeftOrStart a.LeftOrStart
                    searchPair a.RightChild a.RightChild
                    searchPair a.LeftOrStart a.RightChild
            elif BvhUtil.sqBoxDist a.Box b.Box <= sqMaxDist then
                match a.Count > 0, b.Count > 0 with
                | true, true -> // both leaves
                    for i = a.LeftOrStart to a.LeftOrStart + a.Count - 1 do
                        for j = b.LeftOrStart to b.LeftOrStart + b.Count - 1 do
                            testPair itemIndices.[i] itemIndices.[j]
                | false, _ ->
                    searchPair a.LeftOrStart nb
                    searchPair a.RightChild nb
                | true, false ->
                    searchPair na b.LeftOrStart
                    searchPair na b.RightChild
        searchPair root root
        result

    /// <summary>Finds all pairs of items that are closer to each other than the given maximum distance,
    /// measured with the given exact squared distance function.
    /// Uses a dual tree traversal: pairs of subtrees whose bounding boxes are farther apart
    /// than the maximum distance are skipped entirely.</summary>
    /// <param name="maxDistance">The maximum distance between two items for the pair to be reported.</param>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A ResizeArray of BvhPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float, sqDistance: 'T -> 'T -> float) : ResizeArray<BvhPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j -> sqDistance items.[i] items.[j])

    /// <summary>Finds all pairs of items whose bounding boxes are closer to each other
    /// than the given maximum distance. The distance between two boxes is 0.0 if they overlap or touch,
    /// so a maxDistance of 0.0 finds all pairs of overlapping or touching boxes.</summary>
    /// <param name="maxDistance">The maximum distance between two item boxes for the pair to be reported.</param>
    /// <returns>A ResizeArray of BvhPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float) : ResizeArray<BvhPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j -> BvhUtil.sqBoxDist boxes.[i] boxes.[j])

    /// <summary>Finds the indices of all items whose bounding box is closer to the given
    /// axis aligned bounding box than the given tolerance.</summary>
    /// <param name="box">The axis aligned bounding box to search in.</param>
    /// <param name="tolerance">The tolerance distance around the box. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsInBox (box: BBox, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil.sqBoxDist box node.Box <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if BvhUtil.sqBoxDist box boxes.[ii] <= sqTol then
                            result.Add ii
                else
                    search node.LeftOrStart
                    search node.RightChild
        search root
        result

    /// <summary>Finds the indices of all items whose bounding box is closer to the given
    /// 3D point than the given tolerance.
    /// The distance between a point and a box is 0.0 if the point is inside or on the box.</summary>
    /// <param name="pt">The 3D point to search around.</param>
    /// <param name="tolerance">The tolerance distance around the point. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsNearPoint (pt: Pnt, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil.sqBoxPntDist pt node.Box <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if BvhUtil.sqBoxPntDist pt boxes.[ii] <= sqTol then
                            result.Add ii
                else
                    search node.LeftOrStart
                    search node.RightChild
        search root
        result

/// Provides static functions to create Bvh trees without specifying the generic type argument.
[<AbstractClass; Sealed>]
type Bvh private () =

    /// <summary>Builds a Bvh from the given items.
    /// The tree is built top-down by splitting at the median of the item-box centers
    /// along the longest axis of the current bounding box.</summary>
    /// <param name="items">The items to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="getBox">A function returning the axis aligned bounding box of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh.</returns>
    static member create (items: 'T[], getBox: 'T -> BBox, [<OPT;DEF(0)>] leafSize: int) : Bvh<'T> =
        Bvh<'T>.create (items, getBox, leafSize)

    /// <summary>Builds a Bvh directly from bounding boxes. The boxes themselves are the items.</summary>
    /// <param name="boxes">The axis aligned bounding boxes to build the tree from.
    ///  The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of boxes per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh of BBox.</returns>
    static member createFromBoxes (boxes: BBox[], [<OPT;DEF(0)>] leafSize: int) : Bvh<BBox> =
        Bvh<BBox>.create (boxes, (fun b -> b), leafSize)
