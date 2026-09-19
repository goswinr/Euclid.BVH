namespace Euclid

open System
open Euclid
open Euclid.EuclidErrors

/// Shorthand for the OptionalAttribute on method arguments.
type internal OPT = Runtime.InteropServices.OptionalAttribute

/// Shorthand for the DefaultParameterValueAttribute on method arguments.
type internal DEF = Runtime.InteropServices.DefaultParameterValueAttribute


module internal Array =

    /// Just Array.zeroCreate<'T> in .NET, but when `UNCHECKED` is defined and used in Fable, it emits `new Array(len)`
    /// without initializing the items to their default value.
    /// Values are `undefined` in JavaScript
    /// Not safe on numbers, Fable emits TypedArrays for numeric arrays.
    let inline zeroCreateUndef<'T> (len:int) : 'T [] = //
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr (len) "new Array($0)"
        #else
            Array.zeroCreate<'T> len
        #endif

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

    /// Returns the exact number of nodes that build will create for the given count of items.
    /// The shape of the tree only depends on the count of items and the leaf size,
    /// because the split is always at the middle of the index range.
    /// This mirrors the recursion of buildNode below, so that the node array can be allocated at its exact size.
    let rec nodeCount (count: int) (leafSize: int) : int =
        if count <= leafSize then 1
        else
            let mid = count / 2
            1 + nodeCount mid leafSize + nodeCount (count - mid) leafSize

    /// Reorders idx.[first..last], together with the parallel keys, such that position k holds the item
    /// that a full sort by key would put there. All items before k have a smaller or equal key,
    /// all items after k a bigger or equal one.
    /// This is a quickselect with a three way partition. It runs in place, in linear time on average,
    /// and does not allocate. Only the median is needed for the split, so a full sort would be wasted work.
    let selectNth (idx: int[]) (keys: float[]) (first: int) (last: int) (k: int) : unit =
        let inline swap i j =
            let ti = idx.[i] in idx.[i] <- idx.[j] ; idx.[j] <- ti
            let tk = keys.[i] in keys.[i] <- keys.[j] ; keys.[j] <- tk
        let mutable lo = first
        let mutable hi = last
        let mutable go = true
        while go && lo < hi do
            // the median of the first, middle and last key as the pivot,
            // so that sorted or reversed input does not degenerate to quadratic time:
            let a = keys.[lo]
            let b = keys.[lo + (hi - lo) / 2]
            let c = keys.[hi]
            let pivot =
                if a < b then (if b < c then b elif a < c then c else a)
                else          (if a < c then a elif b < c then c else b)
            // partition lo..hi into three parts: smaller than the pivot, equal to it, bigger than it.
            // The equal part is never empty, so each iteration shrinks the range and the loop terminates.
            let mutable lt = lo // keys.[lo   .. lt-1] are smaller than the pivot
            let mutable gt = hi // keys.[gt+1 .. hi  ] are bigger than the pivot
            let mutable i  = lo // keys.[lt   .. i-1 ] are equal to the pivot
            while i <= gt do
                let v = keys.[i]
                if   v < pivot then swap i lt ; lt <- lt + 1 ; i <- i + 1
                elif v > pivot then swap i gt ; gt <- gt - 1 // i is not advanced, the swapped in key is still unseen
                else                            i <- i + 1
            if   k < lt then hi <- lt - 1 // the k-th item is in the smaller part
            elif k > gt then lo <- gt + 1 // the k-th item is in the bigger part
            else             go <- false  // the k-th item is in the equal part, it is already in place

    /// Builds the flattened node array for the given boxes.
    /// Returns the permutation of item indices, the nodes and the index of the root node.
    let build (boxes: BBox[]) (leafSize: int) : int[] * BvhNode[] * int =
        let n = boxes.Length
        // the permutation of item indices, reordered in place while building:
        let idx = Array.zeroCreate<int> n
        for i = 0 to n - 1 do
            idx.[i] <- i
        // scratch space for the box centers along the split axis of the current node, parallel to idx.
        // It is refilled for the range of each node, so that no per node array is needed:
        let keys = Array.zeroCreate<float> n
        // the count of nodes is known upfront, so the array is allocated at its exact size and filled in place:
        let nodes = Array.zeroCreateUndef<BvhNode> (nodeCount n leafSize)

        let boxOf start count =
            let mutable b = boxes.[idx.[start]]
            for i = start + 1 to start + count - 1 do
                b <- b.Union boxes.[idx.[i]]
            b

        // recursively builds the node for idx.[start .. start+count-1] into nodes.[nodeIdx] and its
        // subtree into the slots right after it. Returns the first free slot after the subtree.
        let rec buildNode nodeIdx start count : int =
            let box = boxOf start count
            if count <= leafSize then
                nodes.[nodeIdx] <- { Box = box; LeftOrStart = start; RightChild = -1; Count = count }
                nodeIdx + 1
            else
                // split at the median of the box centers along the longest axis of this node's box:
                let sizeX = box.MaxX - box.MinX
                let sizeY = box.MaxY - box.MinY
                let sizeZ = box.MaxZ - box.MinZ
                let last = start + count - 1
                if sizeX >= sizeY && sizeX >= sizeZ then
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (boxes.[ii].MinX + boxes.[ii].MaxX) * 0.5
                elif sizeY >= sizeZ then
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (boxes.[ii].MinY + boxes.[ii].MaxY) * 0.5
                else
                    for i = start to last do
                        let ii = idx.[i]
                        keys.[i] <- (boxes.[ii].MinZ + boxes.[ii].MaxZ) * 0.5
                let mid = count / 2
                // only partition around the median, do not sort the whole range:
                selectNth idx keys start last (start + mid)
                let left = nodeIdx + 1
                let right = buildNode left start mid
                let free = buildNode right (start + mid) (count - mid)
                nodes.[nodeIdx] <- { Box = box; LeftOrStart = left; RightChild = right; Count = 0 }
                free

        buildNode 0 0 n |> ignore
        idx, nodes, 0

/// An internal module holding the priority queue for the .xxxByDistance queries
/// that return seq of items in order of increasing distance,
/// shared by the 3D and the 2D tree.
///
/// Why it exists: the *ByDistance queries enumerate the items in order of increasing distance,
/// which the depth first branch and bound search of the Closest* queries cannot do. They instead
/// walk the tree best first, expanding whichever subtree or item is currently the closest.
/// That needs a priority queue over the pending subtrees and items, keyed by their distance.
///
/// Why it is hand written: System.Collections.Generic.PriorityQueue would do the job, but it only
/// exists from .NET 6 on. This library also targets net472 and compiles to JavaScript and
/// TypeScript with Fable, so an implementation that works everywhere is needed here.
module internal BvhHeap =

    /// A binary min heap over entries of a float key and an int payload.
    /// The keys and the payloads are held in two parallel ResizeArrays rather than in one array of
    /// entry objects, so that pushing an entry does not allocate. That also keeps it Fable friendly,
    /// a ResizeArray of floats or ints maps to a plain JavaScript array.
    type MinHeap() =
        let keys = ResizeArray<float>()
        let values = ResizeArray<int>()

        let swap i j =
            let k = keys.[i] in keys.[i] <- keys.[j] ; keys.[j] <- k
            let v = values.[i] in values.[i] <- values.[j] ; values.[j] <- v

        /// The count of entries currently in the heap.
        member _.Count = keys.Count

        /// Adds an entry and sifts it up to its place.
        member _.Push (key: float, value: int) : unit =
            keys.Add key
            values.Add value
            let mutable i = keys.Count - 1
            let mutable go = true
            while go && i > 0 do
                let parent = (i - 1) / 2
                if keys.[parent] > keys.[i] then
                    swap parent i
                    i <- parent
                else
                    go <- false

        /// Removes and returns the entry with the smallest key, as a key and payload tuple.
        /// The heap must not be empty.
        member _.Pop () : float * int =
            let topKey = keys.[0]
            let topValue = values.[0]
            let last = keys.Count - 1
            keys.[0] <- keys.[last]
            values.[0] <- values.[last]
            keys.RemoveAt last
            values.RemoveAt last
            // sift the moved up last entry down again:
            let n = keys.Count
            let mutable i = 0
            let mutable go = true
            while go do
                let left = 2 * i + 1
                let right = left + 1
                let mutable smallest = i
                if left  < n && keys.[left]  < keys.[smallest] then smallest <- left
                if right < n && keys.[right] < keys.[smallest] then smallest <- right
                if smallest = i then
                    go <- false
                else
                    swap smallest i
                    i <- smallest
            topKey, topValue

    /// Encodes an item index as the payload of a MinHeap entry.
    /// A best first traversal keeps pending tree nodes and pending items in one single heap, because
    /// they have to be popped in one common distance order. Both are just an int index, so the two
    /// are told apart by their sign: a node index is stored as it is, an item index as a negative value.
    let inline encodeItem (itemIdx: int) : int = -itemIdx - 1

    /// Returns the item index encoded by encodeItem.
    let inline decodeItem (payload: int) : int = -payload - 1

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
type Bvh<'T> private (items: Collections.Generic.IList<'T>, boxes: BBox[], itemIndices: int[], nodes: BvhNode[], root: int) =

    /// The default maximum amount of items per leaf node.
    static member val DefaultLeafSize = 4 with get

    /// The input items this Bvh was built from. Do not mutate this array.
    member _.Items = items

    /// The bounding box of each input item, in the same order as Items. Do not mutate this array.
    member _.Boxes = boxes

    /// The permutation of item indices as referenced by the leaf nodes. Do not mutate this array.
    member internal _.ItemIndices = itemIndices

    /// The count of items in this Bvh.
    member _.Count = items.Count

    /// The axis aligned bounding box around all items in this Bvh.
    member _.Box = nodes.[root].Box

    /// Builds a Bvh from items and their already evaluated bounding boxes.
    static member internal createWithBoxes (items: Collections.Generic.IList<'T>, boxes: BBox[], leafSize: int) : Bvh<'T> =
        let idx, nodes, root = BvhUtil.build boxes leafSize
        Bvh<'T> (items, boxes, idx, nodes, root)

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
        let boxes = Array.map getBox items
        Bvh<'T>.createWithBoxes (items, boxes, leafSize)


    /// <summary>Builds a Bvh from the given resizable array of items.</summary>
    /// <param name="items">The items to build the tree from. They are copied to an array at build time.</param>
    /// <param name="getBox">A function returning the axis aligned bounding box of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh.</returns>
    static member create (items: ResizeArray<'T>, getBox: 'T -> BBox, [<OPT;DEF(0)>] leafSize: int) : Bvh<'T> =
        if isNull items then fail "Bvh.create: items ResizeArray is null."
        if items.Count = 0 then fail "Bvh.create: items ResizeArray is empty."
        let leafSize = if leafSize < 1 then Bvh<'T>.DefaultLeafSize else leafSize
        let boxes = Array.zeroCreateUndef<BBox> items.Count
        for i = 0 to items.Count - 1 do
            boxes.[i] <- getBox items.[i]
        Bvh<'T>.createWithBoxes (items, boxes, leafSize)

    /// <summary>Builds a Bvh from the given sequence of items.</summary>
    /// <param name="items">The items to build the tree from. They are enumerated and copied to an array at build time.</param>
    /// <param name="getBox">A function returning the axis aligned bounding box of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh.</returns>
    static member create (items: seq<'T>, getBox: 'T -> BBox, [<OPT;DEF(0)>] leafSize: int) : Bvh<'T> =
        if isNull (box items) then fail "Bvh.create: items sequence is null."
        Bvh<'T>.create (Array.ofSeq items, getBox, leafSize)

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
    member _.ClosestItem (queryBox: BBox, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
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
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree whose bounding box is closest to the given query box.
    /// The distance between two boxes is 0.0 if they overlap or touch.</summary>
    /// <param name="queryBox">The axis aligned bounding box to search the closest item box for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding box and the distance between the boxes.</returns>
    member bvh.ClosestBox (queryBox: BBox, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
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
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree closest to the given query point.
    /// The distance to an item is measured to the exact geometry via the given squared distance
    /// function, while the item bounding boxes provide lower bounds for branch and bound pruning:
    /// subtrees whose bounding box is farther away from the point
    /// than the best distance found so far are skipped.</summary>
    /// <param name="pt">The 3D point to search the closest item for.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the closest item in the input array and the distance to it.</returns>
    member _.ClosestItem (pt: Pnt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
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
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the item in the tree whose bounding box is closest to the given query point.
    /// The distance between a point and a box is 0.0 if the point is inside or on the box.</summary>
    /// <param name="pt">The 3D point to search the closest item box for.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the search. Optional, -1 (skip nothing) by default.</param>
    /// <returns>The index of the item with the closest bounding box and the distance from the point to that box.</returns>
    member _.ClosestBox (pt: Pnt, [<OPT;DEF(-1)>] skipIdx: int) : int * float =
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
        bestIdx, sqrt bestSqDist

    /// <summary>Finds the pair of closest items among all items in the tree, measured with
    /// the given exact squared distance function. For every item the nearest neighbor
    /// is searched with branch and bound pruning on the bounding boxes.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>A BvhPair with the indices of the two closest items and their distance.</returns>
    member bvh.ClosestPair (sqDistance: 'T -> 'T -> float) : BvhPair =
        if items.Count < 2 then fail "Bvh.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Count - 1 do
            let j, d = bvh.ClosestItem (boxes.[i], sqDistance items.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>Finds the pair of items whose bounding boxes are closest to each other.
    /// The distance between two boxes is 0.0 if they overlap or touch.</summary>
    /// <returns>A BvhPair with the indices of the two items and the distance between their boxes.</returns>
    member bvh.ClosestPair () : BvhPair =
        if items.Count < 2 then fail "Bvh.ClosestPair: needs at least two items."
        let mutable best = { IdxA = -1; IdxB = -1; Distance = Double.MaxValue }
        for i = 0 to items.Count - 1 do
            let j, d = bvh.ClosestBox (boxes.[i], i)
            if d < best.Distance then
                best <- { IdxA = min i j; IdxB = max i j; Distance = d }
        best

    /// <summary>For every item in the tree finds its nearest neighbor item, measured with
    /// the given exact squared distance function.</summary>
    /// <param name="sqDistance">Returns the exact squared distance between two items.</param>
    /// <returns>An array of BvhPair. The entry at index i holds i as IdxA, the index of the
    /// nearest neighbor of item i as IdxB and the distance between them.</returns>
    member bvh.NearestNeighbors (sqDistance: 'T -> 'T -> float) : BvhPair[] =
        if items.Count < 2 then fail "Bvh.NearestNeighbors: needs at least two items."
        Array.init items.Count (fun i ->
            let j, d = bvh.ClosestItem (boxes.[i], sqDistance items.[i], i)
            { IdxA = i; IdxB = j; Distance = d })

    /// <summary>For every item in the tree finds the item whose bounding box is nearest to its own.
    /// The distance between two boxes is 0.0 if they overlap or touch.</summary>
    /// <returns>An array of BvhPair. The entry at index i holds i as IdxA, the index of the
    /// item with the nearest bounding box as IdxB and the distance between the boxes.</returns>
    member bvh.NearestNeighbors () : BvhPair[] =
        if items.Count < 2 then fail "Bvh.NearestNeighbors: needs at least two items."
        Array.init items.Count (fun i ->
            let j, d = bvh.ClosestBox (boxes.[i], i)
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

    /// <summary>Lazily enumerates all items in the tree ordered by the distance of their bounding box
    /// to the given query box, from the closest to the farthest.
    /// The tree is walked best first: a min heap holds the subtrees and items seen so far, keyed by
    /// their distance to the query box, and the closest entry is expanded next. So only the part of the
    /// tree that is closer than the last item taken is ever visited. Taking just the first entry costs
    /// about as much as ClosestBox, taking all of them sorts the whole tree.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="queryBox">The axis aligned bounding box to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and the distance from the
    /// query box to its bounding box, in order of increasing distance.</returns>
    member _.BoxesByDistance (queryBox: BBox, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil.sqBoxDist queryBox nodes.[root].Box, root)
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
                                heap.Push (BvhUtil.sqBoxDist queryBox boxes.[ii], BvhHeap.encodeItem ii)
                    else
                        heap.Push (BvhUtil.sqBoxDist queryBox nodes.[node.LeftOrStart].Box, node.LeftOrStart)
                        heap.Push (BvhUtil.sqBoxDist queryBox nodes.[node.RightChild].Box, node.RightChild)
        }

    /// <summary>Lazily enumerates all items in the tree ordered by their exact distance to the query
    /// geometry, from the closest to the farthest.
    /// The tree is walked best first, with the box distances as lower bounds: a min heap holds the
    /// subtrees seen so far keyed by the distance of their bounding box, and the items of an expanded
    /// leaf keyed by their exact distance. So only the part of the tree that is closer than the last
    /// item taken is ever visited, and sqDistanceTo is called only for the items in those leaves.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="queryBox">The axis aligned bounding box of the query geometry.
    ///  It must fully contain the query geometry that sqDistanceTo measures from,
    ///  otherwise the enumeration order is wrong.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query geometry to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and its exact distance to the
    /// query geometry, in order of increasing distance.</returns>
    member _.ItemsByDistance (queryBox: BBox, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil.sqBoxDist queryBox nodes.[root].Box, root)
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
                        heap.Push (BvhUtil.sqBoxDist queryBox nodes.[node.LeftOrStart].Box, node.LeftOrStart)
                        heap.Push (BvhUtil.sqBoxDist queryBox nodes.[node.RightChild].Box, node.RightChild)
        }

    /// <summary>Lazily enumerates all items in the tree ordered by the distance of their bounding box
    /// to the given 3D point, from the closest to the farthest.
    /// The distance from a point to a box is 0.0 if the point is inside or on the box.
    /// The tree is walked best first, so only the part of it that is closer than the last item taken
    /// is ever visited. The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 3D point to measure the distances from.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and the distance from the
    /// point to its bounding box, in order of increasing distance.</returns>
    member _.BoxesByDistance (pt: Pnt, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil.sqBoxPntDist pt nodes.[root].Box, root)
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
                                heap.Push (BvhUtil.sqBoxPntDist pt boxes.[ii], BvhHeap.encodeItem ii)
                    else
                        heap.Push (BvhUtil.sqBoxPntDist pt nodes.[node.LeftOrStart].Box, node.LeftOrStart)
                        heap.Push (BvhUtil.sqBoxPntDist pt nodes.[node.RightChild].Box, node.RightChild)
        }

    /// <summary>Lazily enumerates all items in the tree ordered by their exact distance to the given
    /// 3D point, from the closest to the farthest.
    /// The tree is walked best first, with the box distances as lower bounds, so only the part of it
    /// that is closer than the last item taken is ever visited, and sqDistanceTo is called only for
    /// the items in the leaves that were expanded.
    /// The sequence is re-enumerable, every enumeration starts a new traversal.</summary>
    /// <param name="pt">The 3D point to measure the distances from.</param>
    /// <param name="sqDistanceTo">Returns the exact squared distance from the query point to an item.</param>
    /// <param name="skipIdx">An index into the input items array to exclude from the enumeration. Optional, -1 (skip nothing) by default.</param>
    /// <returns>A lazy sequence of the index of each item in the input array and its exact distance to the
    /// point, in order of increasing distance.</returns>
    member _.ItemsByDistance (pt: Pnt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : seq<int * float> =
        seq {
            let heap = BvhHeap.MinHeap()
            heap.Push (BvhUtil.sqBoxPntDist pt nodes.[root].Box, root)
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
                        heap.Push (BvhUtil.sqBoxPntDist pt nodes.[node.LeftOrStart].Box, node.LeftOrStart)
                        heap.Push (BvhUtil.sqBoxPntDist pt nodes.[node.RightChild].Box, node.RightChild)
        }

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

    /// <summary>Builds a Bvh from the given resizable array of items.</summary>
    /// <param name="items">The items to build the tree from. They are copied to an array at build time.</param>
    /// <param name="getBox">A function returning the axis aligned bounding box of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh.</returns>
    static member create (items: ResizeArray<'T>, getBox: 'T -> BBox, [<OPT;DEF(0)>] leafSize: int) : Bvh<'T> =
        Bvh<'T>.create (items, getBox, leafSize)

    /// <summary>Builds a Bvh from the given sequence of items.</summary>
    /// <param name="items">The items to build the tree from. They are enumerated and copied to an array at build time.</param>
    /// <param name="getBox">A function returning the axis aligned bounding box of an item.
    ///  It is called once per item at build time.</param>
    /// <param name="leafSize">The maximum amount of items per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh.</returns>
    static member create (items: seq<'T>, getBox: 'T -> BBox, [<OPT;DEF(0)>] leafSize: int) : Bvh<'T> =
        Bvh<'T>.create (items, getBox, leafSize)

    /// <summary>Builds a Bvh directly from bounding boxes. The boxes themselves are the items.</summary>
    /// <param name="boxes">The axis aligned bounding boxes to build the tree from.
    ///  The array is referenced, not copied. Do not mutate it afterwards.</param>
    /// <param name="leafSize">The maximum amount of boxes per leaf node. Optional, 4 by default.</param>
    /// <returns>A new immutable Bvh of BBox.</returns>
    static member createFromBoxes (boxes: BBox[], [<OPT;DEF(0)>] leafSize: int) : Bvh<BBox> =
        Bvh<BBox>.create (boxes, (fun b -> b), leafSize)
