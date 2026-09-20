namespace Euclid

open System
open Euclid
open Euclid.EuclidErrors

/// An internal node of a BVH2D tree, stored in a flattened array.
/// If Count is greater than 0 the node is a leaf that owns Count item indices starting at LeftOrStart
/// in the BVH2D.ItemIndices array.
/// Otherwise LeftOrStart and RightChild are the array indices of the two child nodes.
[<Struct; NoEquality; NoComparison>]
type internal BVHNode2D = {
    /// The axis aligned bounding rectangle of everything below this node.
    Rect: BRect
    /// For a leaf node the start index into BVH2D.ItemIndices, otherwise the index of the left child node.
    LeftOrStart: int
    /// The index of the right child node. Unused (-1) for leaf nodes.
    RightChild: int
    /// The count of items in a leaf node. 0 or negative for internal nodes.
    Count: int
    }

/// An internal module with functions shared by all BVH2D instantiations.
/// The index handling (BvhUtil.nodeCount and BvhUtil.selectNth) and the priority queue of the
/// best first traversals (BvhHeap) are shared with the 3D BVH, only the geometry is specific to 2D.
module internal BvhUtil2D =

    /// Returns the squared distance between two axis aligned bounding rectangles.
    /// Returns 0.0 if they overlap or touch.
    let inline sqRectDist (a: BRect) (b: BRect) : float =
        let inline axis aMin aMax bMin bMax =
            if   bMin > aMax then bMin - aMax
            elif aMin > bMax then aMin - bMax
            else 0.0
        let dx = axis a.MinX a.MaxX b.MinX b.MaxX
        let dy = axis a.MinY a.MaxY b.MinY b.MaxY
        dx*dx + dy*dy

    /// Returns the squared distance between a 2D point and an axis aligned bounding rectangle.
    /// Returns 0.0 if the point is inside or on the rectangle.
    let inline sqRectPtDist (p: Pt) (r: BRect) : float =
        let inline axis v rMin rMax =
            if   v < rMin then rMin - v
            elif v > rMax then v - rMax
            else 0.0
        let dx = axis p.X r.MinX r.MaxX
        let dy = axis p.Y r.MinY r.MaxY
        dx*dx + dy*dy

    /// Builds the flattened node array for the given bounding rectangles.
    /// Returns the permutation of item indices, the nodes and the index of the root node.
    let build (rects: BRect[]) (leafSize: int) : int[] * BVHNode2D[] * int =
        let n = rects.Length
        // the permutation of item indices, reordered in place while building:
        let idx = Array.zeroCreate<int> n
        for i = 0 to n - 1 do
            idx.[i] <- i
        // scratch space for the rectangle centers along the split axis of the current node, parallel to idx.
        // It is refilled for the range of each node, so that no per node array is needed:
        let keys = Array.zeroCreate<float> n
        // the count of nodes is known upfront, so the array is allocated at its exact size and filled in place:
        let nodes = Array.zeroCreateUndef<BVHNode2D> (BvhUtil.nodeCount n leafSize)

        let rectOf start count =
            let mutable r = rects.[idx.[start]]
            for i = start + 1 to start + count - 1 do
                r <- r.Union rects.[idx.[i]]
            r

        // recursively builds the node for idx.[start .. start+count-1] into nodes.[nodeIdx] and its
        // subtree into the slots right after it. Returns the first free slot after the subtree.
        let rec buildNode nodeIdx start count : int =
            if count <= leafSize then
                let rect = rectOf start count
                nodes.[nodeIdx] <- { Rect = rect; LeftOrStart = start; RightChild = -1; Count = count }
                nodeIdx + 1
            else
                // Collect node bounds and center ranges together. Item length must not force
                // a split along an axis where the centers have little or no separation.
                let mutable rect = rects.[idx.[start]]
                let mutable minX = (rect.MinX + rect.MaxX) * 0.5
                let mutable minY = (rect.MinY + rect.MaxY) * 0.5
                let mutable maxX = minX
                let mutable maxY = minY
                let last = start + count - 1
                for i = start + 1 to last do
                    let r = rects.[idx.[i]]
                    rect <- rect.Union r
                    let cx = (r.MinX + r.MaxX) * 0.5
                    let cy = (r.MinY + r.MaxY) * 0.5
                    if cx < minX then minX <- cx
                    if cy < minY then minY <- cy
                    if cx > maxX then maxX <- cx
                    if cy > maxY then maxY <- cy
                // Split on the greatest center spread; ties (including coincident centers) prefer X.
                let sizeX = maxX - minX
                let sizeY = maxY - minY
                if sizeX >= sizeY then
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (rects.[ii].MinX + rects.[ii].MaxX) * 0.5
                else
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (rects.[ii].MinY + rects.[ii].MaxY) * 0.5
                let mid = count / 2
                // Partition around the median, with a sort fallback only if selection exceeds its budget:
                BvhUtil.selectNth idx keys start last (start + mid)
                let left = nodeIdx + 1
                let right = buildNode left start mid
                let free = buildNode right (start + mid) (count - mid)
                nodes.[nodeIdx] <- { Rect = rect; LeftOrStart = left; RightChild = right; Count = 0 }
                free

        buildNode 0 0 n |> ignore
        idx, nodes, 0

/// <summary>A generic static Bounding Volume Hierarchy (BVH) over any 2D items,
/// built from Euclid axis aligned bounding rectangles (BRect).
/// The tree is built once from an array of items plus a function that returns the bounding rectangle
/// of each item, and is then immutable.
/// This is a genuinely two dimensional tree: nodes store a BRect, not a BBox with a zero Z range,
/// so it needs a third less memory and does a third less work per distance test than the 3D BVH.
/// It is well suited to unevenly distributed input because the tree adapts to the actual
/// bounding rectangles of the items instead of subdividing space uniformly (as a quadtree or grid would).
/// All queries come in two flavors: rectangle based, where the distance between two items is measured
/// as the distance between their bounding rectangles, and exact, where a distance function for the
/// actual items is supplied. The bounding rectangle distance is always a valid lower bound of the
/// exact distance, so it is used for branch and bound pruning in both cases.</summary>
type BVH2D<'T> private (items: Collections.Generic.IList<'T>, rects: BRect[], itemIndices: int[], nodes: BVHNode2D[], root: int) =

    /// The default maximum amount of items per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// The input collection this BVH2D was built from. Do not modify the collection or its items after construction.
    member _.Items = items

    /// The bounding rectangle of each input item, in the same order as Items. Do not mutate this array.
    member _.Rects = rects

    /// The permutation of item indices as referenced by the leaf nodes. Do not mutate this array.
    member internal _.ItemIndices = itemIndices

    /// The count of items in this BVH2D.
    member _.Count = items.Count

    /// The axis aligned bounding rectangle around all items in this BVH2D.
    member _.Rectangle = nodes.[root].Rect

    /// The bounding rectangles of all tree nodes, grouped by their depth from the root.
    member _.NodeRectanglesByDepth : BRect[][] =
        let levels = ResizeArray<ResizeArray<BRect>>()
        let rec collect depth nodeIdx =
            while levels.Count <= depth do
                levels.Add(ResizeArray())
            let node = nodes.[nodeIdx]
            levels.[depth].Add(node.Rect)
            if node.Count <= 0 then
                collect (depth + 1) node.LeftOrStart
                collect (depth + 1) node.RightChild
        collect 0 root
        levels |> Seq.map (fun level -> level.ToArray()) |> Seq.toArray

    /// Builds a BVH2D from items and their already evaluated bounding rectangles.
    static member internal createWithRects (items: Collections.Generic.IList<'T>, rects: BRect[], leafSize: int) : BVH2D<'T> =
        let idx, nodes, root = BvhUtil2D.build rects leafSize
        BVH2D<'T> (items, rects, idx, nodes, root)

    /// <summary>Builds a BVH2D from the given items.
    /// The tree is built top-down by splitting at the median of the item-rectangle centers
    /// along the axis where those centers have the greatest spread.</summary>
    /// <param name="items">The items to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D.</returns>
    static member create (items: 'T[], getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : BVH2D<'T> =
        if isNull items then fail "BVH2D.create: items array is null."
        if items.Length = 0 then fail "BVH2D.create: items array is empty."
        let leafSize = if leafSize < 1 then BVH2D<'T>.DefaultLeafSize else leafSize
        let rects = Array.map getRect items
        BVH2D<'T>.createWithRects (items, rects, leafSize)

    /// <summary>Builds a BVH2D from the given resizable array of items.</summary>
    /// <param name="items">The items to build the tree from. The ResizeArray is used directly, not copied. Do not modify it or its items afterwards.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D.</returns>
    static member create (items: ResizeArray<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : BVH2D<'T> =
        if isNull items then fail "BVH2D.create: items ResizeArray is null."
        if items.Count = 0 then fail "BVH2D.create: items ResizeArray is empty."
        let leafSize = if leafSize < 1 then BVH2D<'T>.DefaultLeafSize else leafSize
        let rects = Array.zeroCreateUndef<BRect> items.Count
        for i = 0 to items.Count - 1 do
            rects.[i] <- getRect items.[i]
        BVH2D<'T>.createWithRects (items, rects, leafSize)

    /// <summary>Builds a BVH2D from the given sequence of items.</summary>
    /// <param name="items">The items to build the tree from. They are enumerated and copied to an array at build time.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D.</returns>
    static member create (items: seq<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : BVH2D<'T> =
        if isNull (box items) then fail "BVH2D.create: items sequence is null."
        BVH2D<'T>.create (Array.ofSeq items, getRect, leafSize)

    /// <summary>Finds the item in the tree closest to the given query bounding rectangle.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the query rectangle and the item rectangles provide lower bounds for
    /// branch and bound pruning: subtrees whose bounding rectangle is farther away from the query
    /// rectangle than the best distance found so far are skipped.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle of the query geometry.
    ///  It must fully contain the query geometry that sqDistanceTo measures from,
    ///  otherwise subtrees may be pruned incorrectly.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query geometry to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search.
    ///  Use this to find the nearest neighbor of an item that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (queryRect: BRect, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2D.sqRectDist queryRect node.Rect < bestSqDist then
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
                    let dLeft = BvhUtil2D.sqRectDist queryRect nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2D.sqRectDist queryRect nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "BVH2D.ClosestItem: no item found. Tree has only the skipped item?"
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree whose bounding rectangle is closest to the given query rectangle.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle to search the closest item rectangle for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding rectangle and the distance between the rectangles.</returns>
    member _.ClosestRect (queryRect: BRect, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2D.sqRectDist queryRect node.Rect < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil2D.sqRectDist queryRect rects.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let dLeft = BvhUtil2D.sqRectDist queryRect nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2D.sqRectDist queryRect nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "BVH2D.ClosestRect: no item found. Tree has only the skipped item?"
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree closest to the given query point.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the item bounding rectangles provide lower bounds for branch and bound pruning:
    /// subtrees whose bounding rectangle is farther away from the point
    /// than the best distance found so far are skipped.</summary>
    /// <param name="pt">The 2D point to search the closest item for.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (pt: Pt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2D.sqRectPtDist pt node.Rect < bestSqDist then
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
                    let dLeft = BvhUtil2D.sqRectPtDist pt nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2D.sqRectPtDist pt nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "BVH2D.ClosestItem: no item found. Tree has only the skipped item?"
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree whose bounding rectangle is closest to the given query point.
    /// The distance between a point and a rectangle is 0.0 if the point is inside or on the rectangle.</summary>
    /// <param name="pt">The 2D point to search the closest item rectangle for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding rectangle and the distance from the point to that rectangle.</returns>
    member _.ClosestRect (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2D.sqRectPtDist pt node.Rect < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil2D.sqRectPtDist pt rects.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let dLeft = BvhUtil2D.sqRectPtDist pt nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2D.sqRectPtDist pt nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "BVH2D.ClosestRect: no item found. Tree has only the skipped item?"
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the pair of closest items among all items in the tree, measured with
    /// the given exact squared distance function. For every item the nearest neighbor
    /// is searched with branch and bound pruning on the bounding rectangles.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A BVHPair with the indices of the two closest items and their distance.</returns>
    member bvh.ClosestPair (sqDistance: 'T -> 'T -> float) : BVHPair =
        if items.Count < 2 then fail "BVH2D.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Count - 1 do
            let j, d = bvh.ClosestItem (rects.[i], sqDistance items.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>Finds the pair of items whose bounding rectangles are closest to each other.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <returns>A BVHPair with the indices of the two items and the distance between their rectangles.</returns>
    member bvh.ClosestPair () : BVHPair =
        if items.Count < 2 then fail "BVH2D.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Count - 1 do
            let j, d = bvh.ClosestRect (rects.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>For every item in the tree finds its nearest neighbor item, measured with
    /// the given exact squared distance function.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>An array of BVHPair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of item i as IdxB and the distance between them.</returns>
    member bvh.NearestNeighbors (sqDistance: 'T -> 'T -> float) : BVHPair[] =
        if items.Count < 2 then fail "BVH2D.NearestNeighbors: needs at least two items."
        Array.init items.Count (fun i ->
            let j, d = bvh.ClosestItem (rects.[i], sqDistance items.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// <summary>For every item in the tree finds the item whose bounding rectangle is nearest to its own.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <returns>An array of BVHPair. The entry at index i holds i as IdxA, the index of the
    /// item with the nearest bounding rectangle as IdxB and the distance between the rectangles.</returns>
    member bvh.NearestNeighbors () : BVHPair[] =
        if items.Count < 2 then fail "BVH2D.NearestNeighbors: needs at least two items."
        Array.init items.Count (fun i ->
            let j, d = bvh.ClosestRect (rects.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// Internal worker for both ClosePairs overloads, taking a squared distance function on item indices.
    member private _.ClosePairsByIdx (maxDistance: float, sqDistIdx: int -> int -> float) : ResizeArray<BVHPair> =
        if maxDistance < 0.0 then fail $"BVH2D.ClosePairs: maxDistance {maxDistance} must not be negative."
        let sqMaxDist = maxDistance * maxDistance
        let result = ResizeArray<BVHPair>()
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
            elif BvhUtil2D.sqRectDist a.Rect b.Rect <= sqMaxDist then
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
    /// Uses a dual tree traversal: pairs of subtrees whose bounding rectangles are farther apart
    /// than the maximum distance are skipped entirely.</summary>
    /// <param name="maxDistance">The maximum distance between two items for the pair to be reported.</param>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A ResizeArray of BVHPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float, sqDistance: 'T -> 'T -> float) : ResizeArray<BVHPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j -> sqDistance items.[i] items.[j])

    /// <summary>Finds all pairs of items whose bounding rectangles are closer to each other
    /// than the given maximum distance. The distance between two rectangles is 0.0 if they overlap or touch,
    /// so a maxDistance of 0.0 finds all pairs of overlapping or touching rectangles.</summary>
    /// <param name="maxDistance">The maximum distance between two item rectangles for the pair to be reported.</param>
    /// <returns>A ResizeArray of BVHPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float) : ResizeArray<BVHPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j -> BvhUtil2D.sqRectDist rects.[i] rects.[j])

    /// <summary>Finds the indices of all items whose bounding rectangle is closer to the given
    /// axis aligned bounding rectangle than the given tolerance.</summary>
    /// <param name="rect">The axis aligned bounding rectangle to search in.</param>
    /// <param name="tolerance">The tolerance distance around the rectangle. Must not be negative. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsInRect (rect: BRect, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        if tolerance < 0.0 then fail $"BVH2D.ItemsInRect: tolerance {tolerance} must not be negative."
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2D.sqRectDist rect node.Rect <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if BvhUtil2D.sqRectDist rect rects.[ii] <= sqTol then
                            result.Add ii
                else
                    search node.LeftOrStart
                    search node.RightChild
        search root
        result

    /// <summary>Finds the indices of all items whose bounding rectangle is closer to the given
    /// 2D point than the given tolerance.
    /// The distance between a point and a rectangle is 0.0 if the point is inside or on the rectangle.</summary>
    /// <param name="pt">The 2D point to search around.</param>
    /// <param name="tolerance">The tolerance distance around the point. Must not be negative. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsNearPoint (pt: Pt, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        if tolerance < 0.0 then fail $"BVH2D.ItemsNearPoint: tolerance {tolerance} must not be negative."
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2D.sqRectPtDist pt node.Rect <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if BvhUtil2D.sqRectPtDist pt rects.[ii] <= sqTol then
                            result.Add ii
                else
                    search node.LeftOrStart
                    search node.RightChild
        search root
        result

    /// <summary>Lazily enumerates all items in the tree ordered by the distance of their bounding rectangle
    /// to the given query rectangle, from the closest to the farthest.
    /// The tree is walked best first: a min heap holds the subtrees and items seen so far, keyed by
    /// their distance to the query rectangle, and the closest entry is expanded next. So only the part of
    /// the tree that is closer than the last item taken is ever visited. Taking just the first entry costs
    /// about as much as ClosestRect, taking all of them sorts the whole tree.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and the distance from the
    /// query rectangle to its bounding rectangle, in order of increasing distance.</returns>
    member _.RectsByDistance (queryRect: BRect, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil2D.sqRectDist queryRect nodes.[root].Rect, root)
            while heap.Count > 0 do
                let sqD, payload = heap.Pop ()
                if payload < 0 then // an item, all entries still in the heap are at least this far away
                    yield BvhHeap.decodeItem payload, sqrt sqD
                else
                    let node = nodes.[payload]
                    if node.Count > 0 then // leaf
                        for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                            let ii = itemIndices.[i]
                            if ii <> skipIdx then
                                heap.Push (BvhUtil2D.sqRectDist queryRect rects.[ii], BvhHeap.encodeItem ii)
                    else
                        heap.Push (BvhUtil2D.sqRectDist queryRect nodes.[node.LeftOrStart].Rect, node.LeftOrStart)
                        heap.Push (BvhUtil2D.sqRectDist queryRect nodes.[node.RightChild].Rect, node.RightChild)
        }

    /// <summary>Lazily enumerates all items in the tree ordered by their exact distance to the query
    /// geometry, from the closest to the farthest.
    /// The tree is walked best first, with the rectangle distances as lower bounds: a min heap holds the
    /// subtrees seen so far keyed by the distance of their bounding rectangle, and the items of an expanded
    /// leaf keyed by their exact distance. So only the part of the tree that is closer than the last
    /// item taken is ever visited, and sqDistanceTo is called only for the items in those leaves.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle of the query geometry.
    ///  It must fully contain the query geometry that sqDistanceTo measures from,
    ///  otherwise the enumeration order is wrong.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query geometry to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and its exact distance to the
    /// query geometry, in order of increasing distance.</returns>
    member _.ItemsByDistance (queryRect: BRect, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil2D.sqRectDist queryRect nodes.[root].Rect, root)
            while heap.Count > 0 do
                let sqD, payload = heap.Pop ()
                if payload < 0 then
                    yield BvhHeap.decodeItem payload, sqrt sqD
                else
                    let node = nodes.[payload]
                    if node.Count > 0 then // leaf
                        for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                            let ii = itemIndices.[i]
                            if ii <> skipIdx then
                                heap.Push (sqDistanceTo items.[ii], BvhHeap.encodeItem ii)
                    else
                        heap.Push (BvhUtil2D.sqRectDist queryRect nodes.[node.LeftOrStart].Rect, node.LeftOrStart)
                        heap.Push (BvhUtil2D.sqRectDist queryRect nodes.[node.RightChild].Rect, node.RightChild)
        }

    /// <summary>Lazily enumerates all items in the tree ordered by the distance of their bounding rectangle
    /// to the given 2D point, from the closest to the farthest.
    /// The distance from a point to a rectangle is 0.0 if the point is inside or on the rectangle.
    /// The tree is walked best first, so only the part of it that is closer than the last item taken
    /// is ever visited. The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 2D point to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and the distance from the
    /// point to its bounding rectangle, in order of increasing distance.</returns>
    member _.RectsByDistance (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil2D.sqRectPtDist pt nodes.[root].Rect, root)
            while heap.Count > 0 do
                let sqD, payload = heap.Pop ()
                if payload < 0 then
                    yield BvhHeap.decodeItem payload, sqrt sqD
                else
                    let node = nodes.[payload]
                    if node.Count > 0 then // leaf
                        for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                            let ii = itemIndices.[i]
                            if ii <> skipIdx then
                                heap.Push (BvhUtil2D.sqRectPtDist pt rects.[ii], BvhHeap.encodeItem ii)
                    else
                        heap.Push (BvhUtil2D.sqRectPtDist pt nodes.[node.LeftOrStart].Rect, node.LeftOrStart)
                        heap.Push (BvhUtil2D.sqRectPtDist pt nodes.[node.RightChild].Rect, node.RightChild)
        }

    /// <summary>Lazily enumerates all items in the tree ordered by their exact distance to the given
    /// 2D point, from the closest to the farthest.
    /// The tree is walked best first, with the rectangle distances as lower bounds, so only the part of it
    /// that is closer than the last item taken is ever visited, and sqDistanceTo is called only for
    /// the items in the leaves that were expanded.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 2D point to measure the distances from.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and its exact distance to the
    /// point, in order of increasing distance.</returns>
    member _.ItemsByDistance (pt: Pt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil2D.sqRectPtDist pt nodes.[root].Rect, root)
            while heap.Count > 0 do
                let sqD, payload = heap.Pop ()
                if payload < 0 then
                    yield BvhHeap.decodeItem payload, sqrt sqD
                else
                    let node = nodes.[payload]
                    if node.Count > 0 then // leaf
                        for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                            let ii = itemIndices.[i]
                            if ii <> skipIdx then
                                heap.Push (sqDistanceTo items.[ii], BvhHeap.encodeItem ii)
                    else
                        heap.Push (BvhUtil2D.sqRectPtDist pt nodes.[node.LeftOrStart].Rect, node.LeftOrStart)
                        heap.Push (BvhUtil2D.sqRectPtDist pt nodes.[node.RightChild].Rect, node.RightChild)
        }

/// Provides static functions to create BVH2D trees without specifying the generic type argument.
[<AbstractClass; Sealed>]
type BVH2D private () =

    /// <summary>Builds a BVH2D from the given items.
    /// The tree is built top-down by splitting at the median of the item-rectangle centers
    /// along the axis where those centers have the greatest spread.</summary>
    /// <param name="items">The items to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D.</returns>
    static member create (items: 'T[], getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : BVH2D<'T> =
        BVH2D<'T>.create (items, getRect, leafSize)

    /// <summary>Builds a BVH2D from the given resizable array of items.</summary>
    /// <param name="items">The items to build the tree from. The ResizeArray is used directly, not copied. Do not modify it or its items afterwards.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D.</returns>
    static member create (items: ResizeArray<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : BVH2D<'T> =
        BVH2D<'T>.create (items, getRect, leafSize)

    /// <summary>Builds a BVH2D from the given sequence of items.</summary>
    /// <param name="items">The items to build the tree from. They are enumerated and copied to an array at build time.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D.</returns>
    static member create (items: seq<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : BVH2D<'T> =
        BVH2D<'T>.create (items, getRect, leafSize)

    /// <summary>Builds a BVH2D directly from bounding rectangles. The rectangles themselves are the items.</summary>
    /// <param name="rects">The axis aligned bounding rectangles to build the tree from.
    ///  The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of rectangles per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable BVH2D of BRect.</returns>
    static member createFromRects (rects: BRect[], [<OPT;DEF(0)>] leafSize: int) : BVH2D<BRect> =
        BVH2D<BRect>.create (rects, (fun rect -> rect), leafSize)
