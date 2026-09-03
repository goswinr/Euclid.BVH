namespace Euclid

open System
open Euclid
open Euclid.EuclidErrors

/// An internal node of a Bvh2d tree, stored in a flattened array.
/// If Count is greater than 0 the node is a leaf that owns Count item indices starting at LeftOrStart
/// in the Bvh2d.ItemIndices array.
/// Otherwise LeftOrStart and RightChild are the array indices of the two child nodes.
[<Struct; NoEquality; NoComparison>]
type internal BvhNode2d = {
    /// The axis aligned bounding rectangle of everything below this node.
    Rect: BRect
    /// For a leaf node the start index into Bvh2d.ItemIndices, otherwise the index of the left child node.
    LeftOrStart: int
    /// The index of the right child node. Unused (-1) for leaf nodes.
    RightChild: int
    /// The count of items in a leaf node. 0 or negative for internal nodes.
    Count: int
    }

/// An internal module with functions shared by all Bvh2d instantiations.
/// The index handling (BvhUtil.nodeCount and BvhUtil.selectNth) is shared with the 3D Bvh,
/// only the geometry is specific to 2D.
module internal BvhUtil2d =

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
    let build (rects: BRect[]) (leafSize: int) : int[] * BvhNode2d[] * int =
        let n = rects.Length
        // the permutation of item indices, reordered in place while building:
        let idx = Array.zeroCreate<int> n
        for i = 0 to n - 1 do
            idx.[i] <- i
        // scratch space for the rectangle centers along the split axis of the current node, parallel to idx.
        // It is refilled for the range of each node, so that no per node array is needed:
        let keys = Array.zeroCreate<float> n
        // the count of nodes is known upfront, so the array is allocated at its exact size and filled in place:
        let nodes = Array.zeroCreate<BvhNode2d> (BvhUtil.nodeCount n leafSize)

        let rectOf start count =
            let mutable r = rects.[idx.[start]]
            for i = start + 1 to start + count - 1 do
                r <- r.Union rects.[idx.[i]]
            r

        // recursively builds the node for idx.[start .. start+count-1] into nodes.[nodeIdx] and its
        // subtree into the slots right after it. Returns the first free slot after the subtree.
        let rec buildNode nodeIdx start count : int =
            let rect = rectOf start count
            if count <= leafSize then
                nodes.[nodeIdx] <- { Rect = rect; LeftOrStart = start; RightChild = -1; Count = count }
                nodeIdx + 1
            else
                // split at the median of the rectangle centers along the longer axis of this node's rectangle:
                let sizeX = rect.MaxX - rect.MinX
                let sizeY = rect.MaxY - rect.MinY
                let last = start + count - 1
                if sizeX >= sizeY then
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (rects.[ii].MinX + rects.[ii].MaxX) * 0.5
                else
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (rects.[ii].MinY + rects.[ii].MaxY) * 0.5
                let mid = count / 2
                // only partition around the median, do not sort the whole range:
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
/// so it needs a third less memory and does a third less work per distance test than the 3D Bvh.
/// It is well suited to unevenly distributed input because the tree adapts to the actual
/// bounding rectangles of the items instead of subdividing space uniformly (as a quadtree or grid would).
/// All queries come in two flavors: rectangle based, where the distance between two items is measured
/// as the distance between their bounding rectangles, and exact, where a distance function for the
/// actual items is supplied. The bounding rectangle distance is always a valid lower bound of the
/// exact distance, so it is used for branch and bound pruning in both cases.</summary>
type Bvh2d<'T> private (items: 'T[], rects: BRect[], itemIndices: int[], nodes: BvhNode2d[], root: int) =

    /// The default maximum amount of items per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// The input items this Bvh2d was built from. Do not mutate this array.
    member _.Items = items

    /// The bounding rectangle of each input item, in the same order as Items. Do not mutate this array.
    member _.Rects = rects

    /// The permutation of item indices as referenced by the leaf nodes. Do not mutate this array.
    member internal _.ItemIndices = itemIndices

    /// The count of items in this Bvh2d.
    member _.Count = items.Length

    /// The axis aligned bounding rectangle around all items in this Bvh2d.
    member _.Rectangle = nodes.[root].Rect

    /// <summary>Builds a Bvh2d from the given items.
    /// The tree is built top-down by splitting at the median of the item-rectangle centers
    /// along the longer axis of the current bounding rectangle.</summary>
    /// <param name="items">The items to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d.</returns>
    static member create (items: 'T[], getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        if isNull items then fail "Bvh2d.create: items array is null."
        if items.Length = 0 then fail "Bvh2d.create: items array is empty."
        let leafSize = if leafSize < 1 then Bvh2d<'T>.DefaultLeafSize else leafSize
        let rects = Array.init items.Length (fun i -> getRect items.[i])
        Bvh2d<'T>.createWithRects (items, rects, leafSize)

    /// Builds a Bvh2d from items and their already evaluated bounding rectangles.
    static member internal createWithRects (items: 'T[], rects: BRect[], leafSize: int) : Bvh2d<'T> =
        let idx, nodes, root = BvhUtil2d.build rects leafSize
        Bvh2d<'T> (items, rects, idx, nodes, root)

    /// <summary>Builds a Bvh2d from the given resizable array of items.</summary>
    /// <param name="items">The items to build the tree from. They are copied to an array at build time.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d.</returns>
    static member create (items: ResizeArray<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        if isNull items then fail "Bvh2d.create: items ResizeArray is null."
        Bvh2d<'T>.create (items.ToArray(), getRect, leafSize)

    /// <summary>Builds a Bvh2d from the given sequence of items.</summary>
    /// <param name="items">The items to build the tree from. They are enumerated and copied to an array at build time.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d.</returns>
    static member create (items: seq<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        if isNull (box items) then fail "Bvh2d.create: items sequence is null."
        Bvh2d<'T>.create (Array.ofSeq items, getRect, leafSize)

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
    member _.ClosestItem (queryRect: BRect, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2d.sqRectDist queryRect node.Rect < bestSqDist then
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
                    let dLeft = BvhUtil2d.sqRectDist queryRect nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2d.sqRectDist queryRect nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh2d.ClosestItem: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the item in the tree whose bounding rectangle is closest to the given query rectangle.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <param name="queryRect">The axis aligned bounding rectangle to search the closest item rectangle for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding rectangle and the distance between the rectangles.</returns>
    member _.ClosestRect (queryRect: BRect, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2d.sqRectDist queryRect node.Rect < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil2d.sqRectDist queryRect rects.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let dLeft = BvhUtil2d.sqRectDist queryRect nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2d.sqRectDist queryRect nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh2d.ClosestRect: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the item in the tree closest to the given query point.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the item bounding rectangles provide lower bounds for branch and bound pruning:
    /// subtrees whose bounding rectangle is farther away from the point
    /// than the best distance found so far are skipped.</summary>
    /// <param name="pt">The 2D point to search the closest item for.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (pt: Pt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2d.sqRectPtDist pt node.Rect < bestSqDist then
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
                    let dLeft = BvhUtil2d.sqRectPtDist pt nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2d.sqRectPtDist pt nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh2d.ClosestItem: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the item in the tree whose bounding rectangle is closest to the given query point.
    /// The distance between a point and a rectangle is 0.0 if the point is inside or on the rectangle.</summary>
    /// <param name="pt">The 2D point to search the closest item rectangle for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding rectangle and the distance from the point to that rectangle.</returns>
    member _.ClosestRect (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        let mutable bestSqDist = Double.MaxValue
        let mutable bestIdx = -1
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2d.sqRectPtDist pt node.Rect < bestSqDist then
                if node.Count > 0 then // leaf
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if ii <> skipIdx then
                            let sqD = BvhUtil2d.sqRectPtDist pt rects.[ii]
                            if sqD < bestSqDist then
                                bestSqDist <- sqD
                                bestIdx <- ii
                else
                    let dLeft = BvhUtil2d.sqRectPtDist pt nodes.[node.LeftOrStart].Rect
                    let dRight = BvhUtil2d.sqRectPtDist pt nodes.[node.RightChild].Rect
                    if dLeft <= dRight then
                        search node.LeftOrStart
                        search node.RightChild
                    else
                        search node.RightChild
                        search node.LeftOrStart
        search root
        if bestIdx = -1 then fail "Bvh2d.ClosestRect: no item found. Tree has only the skipped item?"
        struct (bestIdx, sqrt bestSqDist)

    /// <summary>Finds the pair of closest items among all items in the tree, measured with
    /// the given exact squared distance function. For every item the nearest neighbor
    /// is searched with branch and bound pruning on the bounding rectangles.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A BvhPair with the indices of the two closest items and their distance.</returns>
    member bvh.ClosestPair (sqDistance: 'T -> 'T -> float) : BvhPair =
        if items.Length < 2 then fail "Bvh2d.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Length - 1 do
            let struct (j, d) = bvh.ClosestItem (rects.[i], sqDistance items.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>Finds the pair of items whose bounding rectangles are closest to each other.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <returns>A BvhPair with the indices of the two items and the distance between their rectangles.</returns>
    member bvh.ClosestPair () : BvhPair =
        if items.Length < 2 then fail "Bvh2d.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Length - 1 do
            let struct (j, d) = bvh.ClosestRect (rects.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>For every item in the tree finds its nearest neighbor item, measured with
    /// the given exact squared distance function.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>An array of BvhPair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of item i as IdxB and the distance between them.</returns>
    member bvh.NearestNeighbors (sqDistance: 'T -> 'T -> float) : BvhPair[] =
        if items.Length < 2 then fail "Bvh2d.NearestNeighbors: needs at least two items."
        Array.init items.Length (fun i ->
            let struct (j, d) = bvh.ClosestItem (rects.[i], sqDistance items.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// <summary>For every item in the tree finds the item whose bounding rectangle is nearest to its own.
    /// The distance between two rectangles is 0.0 if they overlap or touch.</summary>
    /// <returns>An array of BvhPair. The entry at index i holds i as IdxA, the index of the
    /// item with the nearest bounding rectangle as IdxB and the distance between the rectangles.</returns>
    member bvh.NearestNeighbors () : BvhPair[] =
        if items.Length < 2 then fail "Bvh2d.NearestNeighbors: needs at least two items."
        Array.init items.Length (fun i ->
            let struct (j, d) = bvh.ClosestRect (rects.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// Internal worker for both ClosePairs overloads, taking a squared distance function on item indices.
    member private _.ClosePairsByIdx (maxDistance: float, sqDistIdx: int -> int -> float) : ResizeArray<BvhPair> =
        if maxDistance < 0.0 then fail $"Bvh2d.ClosePairs: maxDistance {maxDistance} must not be negative."
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
            elif BvhUtil2d.sqRectDist a.Rect b.Rect <= sqMaxDist then
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
    /// <returns>A ResizeArray of BvhPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float, sqDistance: 'T -> 'T -> float) : ResizeArray<BvhPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j -> sqDistance items.[i] items.[j])

    /// <summary>Finds all pairs of items whose bounding rectangles are closer to each other
    /// than the given maximum distance. The distance between two rectangles is 0.0 if they overlap or touch,
    /// so a maxDistance of 0.0 finds all pairs of overlapping or touching rectangles.</summary>
    /// <param name="maxDistance">The maximum distance between two item rectangles for the pair to be reported.</param>
    /// <returns>A ResizeArray of BvhPair, each with IdxA less than IdxB. The order of the pairs is not defined.</returns>
    member bvh.ClosePairs (maxDistance: float) : ResizeArray<BvhPair> =
        bvh.ClosePairsByIdx (maxDistance, fun i j -> BvhUtil2d.sqRectDist rects.[i] rects.[j])

    /// <summary>Finds the indices of all items whose bounding rectangle is closer to the given
    /// axis aligned bounding rectangle than the given tolerance.</summary>
    /// <param name="rect">The axis aligned bounding rectangle to search in.</param>
    /// <param name="tolerance">The tolerance distance around the rectangle. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsInRect (rect: BRect, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2d.sqRectDist rect node.Rect <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if BvhUtil2d.sqRectDist rect rects.[ii] <= sqTol then
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
    /// <param name="tolerance">The tolerance distance around the point. Optional, 0.0 by default.</param>
    /// <returns>A ResizeArray of the indices of the found items in the input array.</returns>
    member _.ItemsNearPoint (pt: Pt, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        let sqTol = tolerance * tolerance
        let result = ResizeArray<int>()
        let rec search nodeIdx =
            let node = nodes.[nodeIdx]
            if BvhUtil2d.sqRectPtDist pt node.Rect <= sqTol then
                if node.Count > 0 then
                    for i = node.LeftOrStart to node.LeftOrStart + node.Count - 1 do
                        let ii = itemIndices.[i]
                        if BvhUtil2d.sqRectPtDist pt rects.[ii] <= sqTol then
                            result.Add ii
                else
                    search node.LeftOrStart
                    search node.RightChild
        search root
        result

/// Provides static functions to create Bvh2d trees without specifying the generic type argument.
[<AbstractClass; Sealed>]
type Bvh2d private () =

    /// <summary>Builds a Bvh2d from the given items.
    /// The tree is built top-down by splitting at the median of the item-rectangle centers
    /// along the longer axis of the current bounding rectangle.</summary>
    /// <param name="items">The items to build the tree from. The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d.</returns>
    static member create (items: 'T[], getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        Bvh2d<'T>.create (items, getRect, leafSize)

    /// <summary>Builds a Bvh2d from the given resizable array of items.</summary>
    /// <param name="items">The items to build the tree from. They are copied to an array at build time.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d.</returns>
    static member create (items: ResizeArray<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        Bvh2d<'T>.create (items, getRect, leafSize)

    /// <summary>Builds a Bvh2d from the given sequence of items.</summary>
    /// <param name="items">The items to build the tree from. They are enumerated and copied to an array at build time.</param>
    /// <param name="getRect">A function returning the axis aligned bounding rectangle of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d.</returns>
    static member create (items: seq<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        Bvh2d<'T>.create (items, getRect, leafSize)

    /// <summary>Builds a Bvh2d directly from bounding rectangles. The rectangles themselves are the items.</summary>
    /// <param name="rects">The axis aligned bounding rectangles to build the tree from.
    ///  The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of rectangles per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh2d of BRect.</returns>
    static member createFromRects (rects: BRect[], [<OPT;DEF(0)>] leafSize: int) : Bvh2d<BRect> =
        Bvh2d<BRect>.create (rects, (fun rect -> rect), leafSize)
