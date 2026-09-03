namespace Euclid

open System
open Euclid.EuclidErrors

/// <summary>A generic static Bounding Volume Hierarchy (BVH) over any 2D items,
/// built from Euclid axis aligned bounding rectangles (BRect).
/// The tree is immutable and uses the same branch-and-bound queries as Bvh.</summary>
type Bvh2d<'T> private (items: 'T[], rects: BRect[], tree: Bvh<'T>) =

    static let toBox (rect: BRect) =
        BBox.createFromBRect 0.0 0.0 rect

    static member val DefaultLeafSize = 4 with get

    /// The input items this Bvh2d was built from. Do not mutate this array.
    member _.Items = items

    /// The bounding rectangle of each input item, in the same order as Items. Do not mutate this array.
    member _.Rects = rects

    /// The count of items in this Bvh2d.
    member _.Count = items.Length

    /// The axis aligned bounding rectangle around all items in this Bvh2d.
    member _.Rectangle =
        let box = tree.Box
        BRect.createUnchecked (box.MinX, box.MinY, box.MaxX, box.MaxY)

    /// Builds a Bvh2d from the given items.
    static member create (items: 'T[], getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        if isNull items then fail "Bvh2d.create: items array is null."
        if items.Length = 0 then fail "Bvh2d.create: items array is empty."
        let leafSize = if leafSize < 1 then Bvh2d<'T>.DefaultLeafSize else leafSize
        let rects = Array.init items.Length (fun i -> getRect items.[i])
        let tree = Bvh<'T>.createWithBoxes (items, Array.map toBox rects, leafSize)
        Bvh2d<'T> (items, rects, tree)

    /// Builds a Bvh2d from the given resizable array of items.
    static member create (items: ResizeArray<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        if isNull items then fail "Bvh2d.create: items ResizeArray is null."
        Bvh2d<'T>.create (items.ToArray(), getRect, leafSize)

    /// Builds a Bvh2d from the given sequence of items.
    static member create (items: seq<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        if isNull (box items) then fail "Bvh2d.create: items sequence is null."
        Bvh2d<'T>.create (Array.ofSeq items, getRect, leafSize)

    /// Finds the item closest to the given query geometry.
    member _.ClosestItem (queryRect: BRect, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        tree.ClosestItem (toBox queryRect, sqDistanceTo, skipIdx)

    /// Finds the item whose bounding rectangle is closest to the given query rectangle.
    member _.ClosestRect (queryRect: BRect, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        tree.ClosestBox (toBox queryRect, skipIdx)

    /// Finds the item closest to the given query point.
    member _.ClosestItem (pt: Pt, sqDistanceTo: 'T -> float, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        tree.ClosestItem (Pnt (pt.X, pt.Y, 0.0), sqDistanceTo, skipIdx)

    /// Finds the item whose bounding rectangle is closest to the given query point.
    member _.ClosestRect (pt: Pt, [<OPT;DEF(-1)>] skipIdx: int) : struct (int * float) =
        tree.ClosestBox (Pnt (pt.X, pt.Y, 0.0), skipIdx)

    /// Finds the pair of closest items using the given exact squared distance function.
    member _.ClosestPair (sqDistance: 'T -> 'T -> float) : BvhPair =
        tree.ClosestPair sqDistance

    /// Finds the pair of items whose bounding rectangles are closest to each other.
    member _.ClosestPair () : BvhPair =
        tree.ClosestPair ()

    /// Finds the nearest neighbor of every item using the given exact squared distance function.
    member _.NearestNeighbors (sqDistance: 'T -> 'T -> float) : BvhPair[] =
        tree.NearestNeighbors sqDistance

    /// Finds the nearest bounding-rectangle neighbor of every item.
    member _.NearestNeighbors () : BvhPair[] =
        tree.NearestNeighbors ()

    /// Finds all pairs closer than the given maximum distance using the exact squared distance function.
    member _.ClosePairs (maxDistance: float, sqDistance: 'T -> 'T -> float) : ResizeArray<BvhPair> =
        tree.ClosePairs (maxDistance, sqDistance)

    /// Finds all pairs whose bounding rectangles are closer than the given maximum distance.
    member _.ClosePairs (maxDistance: float) : ResizeArray<BvhPair> =
        tree.ClosePairs maxDistance

    /// Finds all items whose bounding rectangle is within tolerance of the given rectangle.
    member _.ItemsInRect (rect: BRect, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        tree.ItemsInBox (toBox rect, tolerance)

    /// Finds all items whose bounding rectangle is within tolerance of the given point.
    member _.ItemsNearPoint (pt: Pt, [<OPT;DEF(0.0)>] tolerance: float) : ResizeArray<int> =
        tree.ItemsNearPoint (Pnt (pt.X, pt.Y, 0.0), tolerance)

/// Provides static functions to create Bvh2d trees without specifying the generic type argument.
[<AbstractClass; Sealed>]
type Bvh2d private () =

    /// Builds a Bvh2d from the given items.
    static member create (items: 'T[], getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        Bvh2d<'T>.create (items, getRect, leafSize)

    /// Builds a Bvh2d from the given resizable array of items.
    static member create (items: ResizeArray<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        Bvh2d<'T>.create (items, getRect, leafSize)

    /// Builds a Bvh2d from the given sequence of items.
    static member create (items: seq<'T>, getRect: 'T -> BRect, [<OPT;DEF(0)>] leafSize: int) : Bvh2d<'T> =
        Bvh2d<'T>.create (items, getRect, leafSize)

    /// Builds a Bvh2d directly from bounding rectangles. The rectangles themselves are the items.
    static member createFromRects (rects: BRect[], [<OPT;DEF(0)>] leafSize: int) : Bvh2d<BRect> =
        Bvh2d<BRect>.create (rects, (fun rect -> rect), leafSize)
