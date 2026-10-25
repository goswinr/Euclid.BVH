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
            let first = rects.[idx.[start]]
            let mutable minX, minY = first.MinX, first.MinY
            let mutable maxX, maxY = first.MaxX, first.MaxY
            for i = start + 1 to start + count - 1 do
                let r = rects.[idx.[i]]
                minX <- min r.MinX minX
                minY <- min r.MinY minY
                maxX <- max r.MaxX maxX
                maxY <- max r.MaxY maxY
            BRect.createUnchecked(minX, minY, maxX, maxY)

        // recursively builds the node for idx.[start .. start+count-1] into nodes.[nodeIdx] and its
        // subtree into the slots right after it. Returns the first free slot after the subtree.
        let rec buildNode nodeIdx start count : int =
            if count <= leafSize then
                let rect = rectOf start count
                nodes.[nodeIdx] <- { Rect = rect; LeftOrStart = start; RightChild = -1; Count = count }
                nodeIdx + 1
            else
                // Collect the center ranges to choose the split axis. Item length must not force
                // a split along an axis where the centers have little or no separation.
                // The bounds of this node are not collected here, they are the union of its two children.
                let first = rects.[idx.[start]]
                let mutable minX = (first.MinX + first.MaxX) * 0.5
                let mutable minY = (first.MinY + first.MaxY) * 0.5
                let mutable maxX = minX
                let mutable maxY = minY
                let last = start + count - 1
                for i = start + 1 to last do
                    let r = rects.[idx.[i]]
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
                // The union of the two finished children is cheaper than another pass over all items:
                let l = nodes.[left].Rect
                let r = nodes.[right].Rect
                let bounds = BRect.createUnchecked(min l.MinX r.MinX, min l.MinY r.MinY, max l.MaxX r.MaxX, max l.MaxY r.MaxY)
                nodes.[nodeIdx] <- { Rect = bounds; LeftOrStart = left; RightChild = right; Count = 0 }
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

    /// The search behind ClosestItem with a query rectangle, ClosestPair and NearestNeighbors.
    /// Finds the item closest to the query geometry among the items with an index bigger than minIdx,
    /// except skipIdx, if its squared distance is smaller than sqBound.
    /// Returns its index and squared distance, or -1 as index if there is no such item.
    member private _.ClosestItemSq (queryRect: BRect, sqDistanceTo: 'T -> float, minIdx: int, skipIdx: int, sqBound: float) : struct (int * float) =
        let mutable bestSqDist = sqBound
        let mutable bestIdx = -1
        // sqNodeDist is the squared distance from the query rectangle to the rectangle of the node, the caller has it already.
        let rec search nodeIdx sqNodeDist =
            if sqNodeDist < bestSqDist then
                let node = nodes.[nodeIdx]
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        // the rectangle distance is a lower bound of the exact distance, so it is checked first:
                        if ii > minIdx && ii <> skipIdx && BvhUtil2D.sqRectDist queryRect rects.[ii] < bestSqDist then
                            let sqD = sqDistanceTo items.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    // visit the closer child first for better pruning:
                    let left = node.LeftOrStart
                    let right = node.RightChild
                    let dLeft = BvhUtil2D.sqRectDist queryRect nodes.[left].Rect
                    let dRight = BvhUtil2D.sqRectDist queryRect nodes.[right].Rect
                    if dLeft <= dRight then
                        search left dLeft
                        search right dRight
                    else
                        search right dRight
                        search left dLeft
        search root (BvhUtil2D.sqRectDist queryRect nodes.[root].Rect)
        struct (bestIdx, bestSqDist)

    /// The search behind ClosestRect with a query rectangle, ClosestPair and NearestNeighbors.
    /// Finds the item whose rectangle is closest to the query rectangle among the items with an index bigger
    /// than minIdx, except skipIdx, if its squared rectangle distance is smaller than sqBound.
    /// Returns its index and squared distance, or -1 as index if there is no such item.
    member private _.ClosestRectSq (queryRect: BRect, minIdx: int, skipIdx: int, sqBound: float) : struct (int * float) =
        let mutable bestSqDist = sqBound
        let mutable bestIdx = -1
        let rec search nodeIdx sqNodeDist =
            if sqNodeDist < bestSqDist then
                let node = nodes.[nodeIdx]
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii > minIdx && ii <> skipIdx then
                            let sqD = BvhUtil2D.sqRectDist queryRect rects.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let left = node.LeftOrStart
                    let right = node.RightChild
                    let dLeft = BvhUtil2D.sqRectDist queryRect nodes.[left].Rect
                    let dRight = BvhUtil2D.sqRectDist queryRect nodes.[right].Rect
                    if dLeft <= dRight then
                        search left dLeft
                        search right dRight
                    else
                        search right dRight
                        search left dLeft
        search root (BvhUtil2D.sqRectDist queryRect nodes.[root].Rect)
        struct (bestIdx, bestSqDist)

    /// <summary>Finds the item in the tree closest to the given query bounding rectangle.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the query rectangle and the item rectangles provide lower bounds for
    /// branch and bound pruning: subtrees and items whose bounding rectangle is farther away from the query
    /// rectangle than the best distance found so far are skipped. Items for which sqDistanceTo returns NaN are ignored.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle of the query geometry.
    ///  It must fully contain the query geometry that sqDistanceTo measures from,
    ///  otherwise subtrees may be pruned incorrectly.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query geometry to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search.
    ///  Use this to find the nearest neighbor of an item that is part of the tree itself. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member bvh.ClosestItem (queryRect: BRect, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let struct (idx, sqD) = bvh.ClosestItemSq (queryRect, sqDistanceTo, -1, skipIdx, Double.PositiveInfinity)
        if idx = -1 then fail "BVH2D.ClosestItem: no item found. The tree has only the skipped item, or all distances are infinite or NaN."
        idx, sqrt sqD

    /// <summary>Finds the item in the tree whose bounding rectangle is closest to the given query rectangle.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle to search the closest item rectangle for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding rectangle and the distance between the rectangles.</returns>
    member bvh.ClosestRect (queryRect: BRect, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let struct (idx, sqD) = bvh.ClosestRectSq (queryRect, -1, skipIdx, Double.PositiveInfinity)
        if idx = -1 then fail "BVH2D.ClosestRect: no item found. Tree has only the skipped item?"
        idx, sqrt sqD

    /// <summary>Finds the item in the tree closest to the given query point.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the item bounding rectangles provide lower bounds for branch and bound pruning:
    /// subtrees whose bounding rectangle is farther away from the point
    /// than the best distance found so far are skipped. Items for which sqDistanceTo returns NaN are ignored.</summary>
    /// <param name="pt">The 2D point to search the closest item for.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (pt: Pt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let mutable bestSqDist = Double.PositiveInfinity
        let mutable bestIdx = -1
        let rec search nodeIdx sqNodeDist =
            if sqNodeDist < bestSqDist then
                let node = nodes.[nodeIdx]
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        // Unlike in ClosestItemSq, the item rectangle is not checked before the exact distance here:
                        // the distance from a point is usually cheap, and on Fable the extra rectangle access cost more than it saved.
                        if ii <> skipIdx then
                            let sqD = sqDistanceTo items.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    // visit the closer child first for better pruning:
                    let left = node.LeftOrStart
                    let right = node.RightChild
                    let dLeft = BvhUtil2D.sqRectPtDist pt nodes.[left].Rect
                    let dRight = BvhUtil2D.sqRectPtDist pt nodes.[right].Rect
                    if dLeft <= dRight then
                        search left dLeft
                        search right dRight
                    else
                        search right dRight
                        search left dLeft
        search root (BvhUtil2D.sqRectPtDist pt nodes.[root].Rect)
        if bestIdx = -1 then fail "BVH2D.ClosestItem: no item found. The tree has only the skipped item, or all distances are infinite or NaN."
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree whose bounding rectangle is closest to the given query point.
    /// The distance between a point and a rectangle is 0.0 if the point is inside or on the rectangle.</summary>
    /// <param name="pt">The 2D point to search the closest item rectangle for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding rectangle and the distance from the point to that rectangle.</returns>
    member _.ClosestRect (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
        let mutable bestSqDist = Double.PositiveInfinity
        let mutable bestIdx = -1
        let rec search nodeIdx sqNodeDist =
            if sqNodeDist < bestSqDist then
                let node = nodes.[nodeIdx]
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil2D.sqRectPtDist pt rects.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let left = node.LeftOrStart
                    let right = node.RightChild
                    let dLeft = BvhUtil2D.sqRectPtDist pt nodes.[left].Rect
                    let dRight = BvhUtil2D.sqRectPtDist pt nodes.[right].Rect
                    if dLeft <= dRight then
                        search left dLeft
                        search right dRight
                    else
                        search right dRight
                        search left dLeft
        search root (BvhUtil2D.sqRectPtDist pt nodes.[root].Rect)
        if bestIdx = -1 then fail "BVH2D.ClosestRect: no item found. Tree has only the skipped item?"
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the pair of closest items among all items in the tree, measured with
    /// the given exact squared distance function. For every item the closest item after it in the input
    /// is searched with branch and bound pruning on the bounding rectangles, bounded by the closest pair found so far.
    /// Pairs for which sqDistance returns NaN are ignored.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A BVHPair with the indices of the two closest items and their distance.</returns>
    member bvh.ClosestPair (sqDistance: 'T -> 'T -> float) : BVHPair =
        if items.Count < 2 then fail "BVH2D.ClosestPair: needs at least two items."
        // The closest pair found so far bounds the search from the next item, so most searches end early.
        // Item i only searches the items after it: a pair with an earlier item j was either measured by
        // the search from j, or pruned there against a bound that is not smaller than the final result.
        let mutable bestSqDist = Double.PositiveInfinity
        let mutable bestA = -1
        let mutable bestB = -1
        let mutable i = 0
        while i < items.Count - 1 && bestSqDist > 0.0 do // nothing can be closer than 0.0
            let struct (j, sqD) = bvh.ClosestItemSq (rects.[i], sqDistance items.[i], i, -1, bestSqDist)
            if j <> -1 then
                bestSqDist <- sqD
                bestA <- i
                bestB <- j
            i <- i + 1
        if bestA = -1 then fail "BVH2D.ClosestPair: no pair found. All distances are infinite or NaN."
        { IdxA = bestA; IdxB = bestB; Distance = sqrt bestSqDist }

    /// <summary>Finds the pair of items whose bounding rectangles are closest to each other.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <returns>A BVHPair with the indices of the two items and the distance between their rectangles.</returns>
    member bvh.ClosestPair () : BVHPair =
        if items.Count < 2 then fail "BVH2D.ClosestPair: needs at least two items."
        // The same bounded search as in the exact ClosestPair above. It stops at the first pair of touching rectangles.
        let mutable bestSqDist = Double.PositiveInfinity
        let mutable bestA = -1
        let mutable bestB = -1
        let mutable i = 0
        while i < items.Count - 1 && bestSqDist > 0.0 do
            let struct (j, sqD) = bvh.ClosestRectSq (rects.[i], i, -1, bestSqDist)
            if j <> -1 then
                bestSqDist <- sqD
                bestA <- i
                bestB <- j
            i <- i + 1
        if bestA = -1 then fail "BVH2D.ClosestPair: no pair found. All rectangle distances are infinite?"
        { IdxA = bestA; IdxB = bestB; Distance = sqrt bestSqDist }

    /// <summary>For every item in the tree finds its nearest neighbor item, measured with
    /// the given exact squared distance function. Pairs for which sqDistance returns NaN are ignored.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>An array of BVHPair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of item i as IdxB and the distance between them.</returns>
    member bvh.NearestNeighbors (sqDistance: 'T -> 'T -> float) : BVHPair[] =
        if items.Count < 2 then fail "BVH2D.NearestNeighbors: needs at least two items."
        Array.init items.Count (fun i ->
            let struct (j, sqD) = bvh.ClosestItemSq (rects.[i], sqDistance items.[i], -1, i, Double.PositiveInfinity)
            if j = -1 then fail $"BVH2D.NearestNeighbors: no neighbor found for item {i}. All its distances are infinite or NaN."
            { IdxA = i; IdxB = j; Distance = sqrt sqD })

    /// <summary>For every item in the tree finds the item whose bounding rectangle is nearest to its own.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <returns>An array of BVHPair. The entry at index i holds i as IdxA, the index of the
    /// item with the nearest bounding rectangle as IdxB and the distance between the rectangles.</returns>
    member bvh.NearestNeighbors () : BVHPair[] =
        if items.Count < 2 then fail "BVH2D.NearestNeighbors: needs at least two items."
        Array.init items.Count (fun i ->
            let struct (j, sqD) = bvh.ClosestRectSq (rects.[i], -1, i, Double.PositiveInfinity)
            if j = -1 then fail $"BVH2D.NearestNeighbors: no neighbor found for item {i}. All its rectangle distances are infinite?"
            { IdxA = i; IdxB = j; Distance = sqrt sqD })

    /// Internal worker for both ClosePairs overloads.
    /// sqDistIdx gets the indices i less than j of two items whose bounding rectangles are within maxDistance
    /// of each other, and the squared distance between those rectangles. It returns the squared distance between the items.
    member private _.ClosePairsByIdx (maxDistance: float, sqDistIdx: int -> int -> float -> float) : ResizeArray<BVHPair> =
        if not (maxDistance >= 0.0) then fail $"BVH2D.ClosePairs: maxDistance {maxDistance} must not be negative or NaN."
        let sqMaxDist = maxDistance * maxDistance
        let result = ResizeArray<BVHPair>()
        let inline testPair a b =
            let i = min a b
            let j = max a b
            // the rectangle distance is a lower bound of the item distance, so it is checked first:
            let sqRect = BvhUtil2D.sqRectDist rects.[i] rects.[j]
            if sqRect <= sqMaxDist then
                let sqD = sqDistIdx i j sqRect
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
                if a.Count > 0 && b.Count > 0 then // both leaves
                    for i = a.LeftOrStart to a.LeftOrStart + a.Count - 1 do
                        for j = b.LeftOrStart to b.LeftOrStart + b.Count - 1 do
                            testPair itemIndices.[i] itemIndices.[j]
                elif a.Count <= 0 then // split the internal node, a first if both are internal
                    searchPair a.LeftOrStart nb
                    searchPair a.RightChild nb
                else
                    searchPair na b.LeftOrStart
                    searchPair na b.RightChild
        searchPair root root
        result

    /// <summary>Finds all pairs of items that are closer to each other than the given maximum distance,
    /// measured with the given exact squared distance function.
    /// Uses a dual tree traversal: pairs of subtrees whose bounding rectangles are farther apart
    /// than the maximum distance are skipped entirely, and sqDistance is only called for pairs of items
    /// whose bounding rectangles are within the maximum distance.</summary>
    /// <param name="maxDistance">The maximum distance between two items for the pair to be reported.</param>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A ResizeArray of BVHPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float, sqDistance: 'T -> 'T -> float) : ResizeArray<BVHPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j _ -> sqDistance items.[i] items.[j])

    /// <summary>Finds all pairs of items whose bounding rectangles are closer to each other
    /// than the given maximum distance. The distance between two rectangles is 0.0 if they overlap or touch,
    /// so a maxDistance of 0.0 finds all pairs of overlapping or touching rectangles.</summary>
    /// <param name="maxDistance">The maximum distance between two item rectangles for the pair to be reported.</param>
    /// <returns>A ResizeArray of BVHPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float) : ResizeArray<BVHPair> =
        bvh.ClosePairsByIdx (maxDistance, fun _ _ sqRect -> sqRect)

    /// <summary>Finds the indices of all items whose bounding rectangle is closer to the given
    /// axis aligned bounding rectangle than the given tolerance.</summary>
    /// <param name="rect">The axis aligned bounding rectangle to search in.</param>
    /// <param name="tolerance">The tolerance distance around the rectangle. Must not be negative. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsInRect (rect: BRect, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        if not (tolerance >= 0.0) then fail $"BVH2D.ItemsInRect: tolerance {tolerance} must not be negative or NaN."
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
        if not (tolerance >= 0.0) then fail $"BVH2D.ItemsNearPoint: tolerance {tolerance} must not be negative or NaN."
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

    /// The best first traversal behind all the ...ByDistance members.
    /// A min heap holds the subtrees and items seen so far, and the entry with the smallest key is expanded next.
    /// nodeSqDist returns the squared distance from the query to the rectangle of a node, a lower bound for all items below it.
    /// itemSqDist returns the squared distance from the query to an item, by item index. Items for which it returns NaN are left out.
    member private _.ByDistance (nodeSqDist: BRect -> float, itemSqDist: int -> float, skipIdx: int) : seq<int * float> =
        // Pops and expands entries until an item is at the top. Returns that item and its distance, or -1 as
        // index if the heap ran empty. This is a plain function and not part of the sequence expression below,
        // because Fable compiles a loop inside a sequence expression to nested sequences.
        let next (heap: BvhHeap.MinHeap) : int * float =
            let mutable idx = -1
            let mutable sqD = 0.0
            while idx = -1 && heap.Count > 0 do
                let key = heap.TopKey
                let payload = heap.TopValue
                heap.RemoveTop ()
                if payload < 0 then // an item, all entries still in the heap are at least this far away
                    idx <- BvhHeap.decodeItem payload
                    sqD <- key
                else
                    let node = nodes.[payload]
                    if node.Count > 0 then // leaf
                        for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                            let ii = itemIndices.[i]
                            if ii <> skipIdx then
                                let d = itemSqDist ii
                                if not (Double.IsNaN d) then // items without a distance are left out
                                    heap.Push (d, BvhHeap.encodeItem ii)
                    else
                        heap.Push (nodeSqDist nodes.[node.LeftOrStart].Rect, node.LeftOrStart)
                        heap.Push (nodeSqDist nodes.[node.RightChild].Rect, node.RightChild)
            idx, sqrt sqD
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (nodeSqDist nodes.[root].Rect, root)
            while heap.Count > 0 do
                let item = next heap
                if fst item <> -1 then yield item
        }

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
    member bvh.RectsByDistance (queryRect: BRect, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        bvh.ByDistance ((fun r -> BvhUtil2D.sqRectDist queryRect r), (fun ii -> BvhUtil2D.sqRectDist queryRect rects.[ii]), skipIdx)

    /// <summary>Lazily enumerates all items in the tree ordered by their exact distance to the query
    /// geometry, from the closest to the farthest.
    /// The tree is walked best first, with the rectangle distances as lower bounds: a min heap holds the
    /// subtrees seen so far keyed by the distance of their bounding rectangle, and the items of an expanded
    /// leaf keyed by their exact distance. So only the part of the tree that is closer than the last
    /// item taken is ever visited, and sqDistanceTo is called only for the items in those leaves.
    /// Items for which sqDistanceTo returns NaN are left out.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle of the query geometry.
    ///  It must fully contain the query geometry that sqDistanceTo measures from,
    ///  otherwise the enumeration order is wrong.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query geometry to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and its exact distance to the
    /// query geometry, in order of increasing distance.</returns>
    member bvh.ItemsByDistance (queryRect: BRect, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        bvh.ByDistance ((fun r -> BvhUtil2D.sqRectDist queryRect r), (fun ii -> sqDistanceTo items.[ii]), skipIdx)

    /// <summary>Lazily enumerates all items in the tree ordered by the distance of their bounding rectangle
    /// to the given 2D point, from the closest to the farthest.
    /// The distance from a point to a rectangle is 0.0 if the point is inside or on the rectangle.
    /// The tree is walked best first, so only the part of it that is closer than the last item taken
    /// is ever visited. The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 2D point to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and the distance from the
    /// point to its bounding rectangle, in order of increasing distance.</returns>
    member bvh.RectsByDistance (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        bvh.ByDistance ((fun r -> BvhUtil2D.sqRectPtDist pt r), (fun ii -> BvhUtil2D.sqRectPtDist pt rects.[ii]), skipIdx)

    /// <summary>Lazily enumerates all items in the tree ordered by their exact distance to the given
    /// 2D point, from the closest to the farthest.
    /// The tree is walked best first, with the rectangle distances as lower bounds, so only the part of it
    /// that is closer than the last item taken is ever visited, and sqDistanceTo is called only for
    /// the items in the leaves that were expanded. Items for which sqDistanceTo returns NaN are left out.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 2D point to measure the distances from.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and its exact distance to the
    /// point, in order of increasing distance.</returns>
    member bvh.ItemsByDistance (pt: Pt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        bvh.ByDistance ((fun r -> BvhUtil2D.sqRectPtDist pt r), (fun ii -> sqDistanceTo items.[ii]), skipIdx)

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
