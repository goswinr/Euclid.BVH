# Euclid.BVH

[![Euclid.BVH on nuget.org](https://img.shields.io/nuget/v/Euclid.BVH)](https://www.nuget.org/packages/Euclid.BVH/)
[![Build Status](https://github.com/goswinr/Euclid.BVH/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Euclid.BVH/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Euclid.BVH/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Euclid.BVH/actions/workflows/test.yml)
[![Docs Build Status](https://github.com/goswinr/Euclid.BVH/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/Euclid.BVH/actions/workflows/docs.yml)
[![license](https://img.shields.io/github/license/goswinr/Euclid.BVH)](LICENSE)

A Bounding Volume Hierarchy (BVH) for fast spatial queries on the
[Euclid](https://github.com/goswinr/Euclid) geometry library.
Like Euclid itself it also compiles to JavaScript and TypeScript via [Fable](https://fable.io/).

Given thousands of static 3D lines with uneven spatial distribution, this library answers
questions like *"which pairs of lines are closest to each other?"* in about `O(n log n)`
instead of the `O(n²)` of a brute force scan.

## Why a BVH?

For finding pairs of closest lines among many static, unevenly distributed 3D lines a
**Bounding Volume Hierarchy over the lines' axis aligned bounding boxes** is the best fit:

- Unlike a uniform grid or an octree it partitions the *objects*, not *space*, so it adapts
  automatically to uneven, clustered input. No tuning of cell sizes is needed.
- Unlike a k-d tree over points, it stores whole line segments. A line is in exactly one leaf,
  so no line has to be split or duplicated.
- Since the lines are static, the tree is built once (top-down median split along the longest
  axis, `O(n log n)`) and is then immutable and cheap to query.
- Distances between axis aligned bounding boxes (`BBox`) give cheap lower bounds for the
  distance between the lines inside them, which lets branch-and-bound queries skip most of
  the tree.

## Usage

```fsharp
open Euclid

// thousands of static 3D lines:
let lines : Line3D[] = ...

// build once, O(n log n):
let bvh = LineBvh.create lines

// the closest line to a query line:
let struct (index, distance) = bvh.ClosestLine (Line3D (0., 0., 0., 1., 1., 1.))

// the nearest neighbor of a line that is itself in the tree (excluding itself):
let struct (neighbor, dist) = bvh.ClosestLine (lines.[42], 42)

// the globally closest pair of lines:
let pair = bvh.ClosestPair ()   // pair.IdxA, pair.IdxB, pair.Distance

// the nearest neighbor of every line:
let neighbors = bvh.NearestNeighbors ()

// all pairs of lines that are closer than 0.5 units to each other:
let closePairs = bvh.ClosePairs 0.5

// all lines near an axis aligned bounding box:
let hits = bvh.LinesInBox (BBox.createFromSeq [ Pnt (0., 0., 0.); Pnt (10., 10., 10.) ])

// point queries: the closest line to a 3D point, the closest point on any line,
// and all lines near a point:
let struct (idx, dist) = bvh.ClosestLine (Pnt (5., 5., 5.))
let closestPt = bvh.ClosestPoint (Pnt (5., 5., 5.))
let nearby = bvh.LinesNearPoint (Pnt (5., 5., 5.), 2.0)
```

### Generic usage

The tree itself is generic: `Bvh<'T>` works with any item type, given a function that returns
the bounding box of an item. It can also be built and queried with plain boxes.

```fsharp
open Euclid

// build directly from bounding boxes, the boxes are the items:
let boxes : BBox[] = ...
let bvh = Bvh.createFromBoxes boxes

// the box closest to a query box (distance 0.0 if they overlap or touch):
let struct (index, distance) = bvh.ClosestBox queryBox

// all pairs of overlapping or touching boxes:
let overlaps = bvh.ClosePairs 0.0

// or build from any items with a box function:
type Ball = { Center: Pnt; Radius: float }
let balls : Ball[] = ...
let bvh = Bvh.create (balls, fun b -> BBox.createFromCenter (b.Center, 2.*b.Radius, 2.*b.Radius, 2.*b.Radius))

// box based queries work as-is; for exact distances supply a squared distance function:
let sqDist a b = let d = max 0.0 (a.Center.DistanceTo b.Center - a.Radius - b.Radius) in d * d
let pair = bvh.ClosestPair sqDist                 // globally closest pair of balls
let touching = bvh.ClosePairs (0.1, sqDist)       // all pairs of balls closer than 0.1
let struct (i, d) = bvh.ClosestItem (queryBox, sqDist query)  // closest ball to a query ball
```

The bounding box distance is always a lower bound of the exact distance, so the tree can
prune subtrees safely in both flavors of query.

### 2D usage

`Bvh2d<'T>` provides the same generic queries for 2D items bounded by Euclid `BRect`
values, with `Pt` point queries. `LineBvh2d` adds exact `Line2D` segment queries.

It is a genuinely two dimensional tree with its own data structure: its nodes store a `BRect`,
not a `BBox` with a zero Z range. So it needs a third less memory per node and does a third
less arithmetic per distance test than the 3D `Bvh<'T>`.

```fsharp
let rects : BRect[] = ...
let bvh = Bvh2d.createFromRects rects
let struct (index, distance) = bvh.ClosestRect (Pt (5., 5.))
let nearby = bvh.ItemsInRect (BRect.createXY (0., 0., 10., 10.))

let lines : Line2D[] = ...
let lineBvh = LineBvh2d.create lines
let struct (lineIndex, lineDistance) = lineBvh.ClosestLine (Pt (5., 5.))
```

## Examples

Runnable examples for an F# script (`.fsx`) after `#r "nuget: Euclid.BVH"`.
The line based examples all use the `bvh` built in the first one.

### Clash detection on piping

Find all pairs of pipe center lines that violate a minimum clearance:

```fsharp
open System
open Euclid

let rand = Random 42

// 10_000 random short lines clustered in a 100 x 100 x 100 volume:
let lines =
    Array.init 10_000 (fun _ ->
        let p = Pnt (rand.NextDouble() * 100., rand.NextDouble() * 100., rand.NextDouble() * 100.)
        let v = Vec (rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5)
        Line3D.createFromPntAndVec (p, v))

let bvh = LineBvh.create lines

// all pairs of lines closer than 0.25 units to each other, via dual tree traversal:
let clashes = bvh.ClosePairs 0.25
for pair in clashes do
    printfn $"line {pair.IdxA} and line {pair.IdxB} are only {pair.Distance} apart"

// the single worst offender, the globally closest pair:
let worst = bvh.ClosestPair ()
printfn $"closest pair: {worst.IdxA} and {worst.IdxB} at distance {worst.Distance}"
```

### Snapping a point to the nearest line

```fsharp
// where the user clicked:
let mousePt = Pnt (50., 50., 50.)

// index of and distance to the nearest line:
let struct (idx, dist) = bvh.ClosestLine mousePt

// the exact point on that line to snap to:
let snapPt = bvh.ClosestPoint mousePt
printfn $"snapped to line {idx} at {snapPt}, {dist} away"
```

### Nearest neighbor statistics

```fsharp
// for every line the index of and distance to its nearest neighbor:
let neighbors = bvh.NearestNeighbors ()

let avgGap = neighbors |> Array.averageBy (fun p -> p.Distance)
let isolated = neighbors |> Array.filter (fun p -> p.Distance > 10. * avgGap)
printfn $"average gap {avgGap}, {isolated.Length} lines are isolated"
```

### Region queries

```fsharp
// all lines whose bounding box touches a region of interest:
let region = BBox.createFromSeq [ Pnt (40., 40., 40.); Pnt (60., 60., 60.) ]
for idx in bvh.LinesInBox region do
    printfn $"line {idx} is inside or touches the region"

// all lines whose bounding box is within 2.0 units of a point:
for idx in bvh.LinesNearPoint (mousePt, 2.0) do
    printfn $"line {idx} is near the point"
```

### Overlapping boxes with the generic tree

`Bvh<'T>` can be used with plain boxes, for example as a broad phase for collision detection:

```fsharp
open System
open Euclid

let rand = Random 42

// 10_000 random boxes:
let boxes =
    Array.init 10_000 (fun _ ->
        let c = Pnt (rand.NextDouble() * 100., rand.NextDouble() * 100., rand.NextDouble() * 100.)
        BBox.createFromCenter (c, rand.NextDouble(), rand.NextDouble(), rand.NextDouble()))

let bvh = Bvh.createFromBoxes boxes

// all pairs of boxes that overlap or touch:
let overlaps = bvh.ClosePairs 0.0

// all boxes within 1.5 units of a given box:
let query = BBox.createFromCenter (Pnt (50., 50., 50.), 4., 4., 4.)
let near = bvh.ItemsInBox (query, 1.5)

// the box closest to a 3D point:
let struct (closest, distance) = bvh.ClosestBox (Pnt (0., 0., 0.))
```

### Custom item types

Any type works, given a bounding box function. Exact distances are supplied per query:

```fsharp
open System
open Euclid

type Ball = { Center: Pnt; Radius: float }

let rand = Random 42
let balls =
    Array.init 5_000 (fun _ ->
        { Center = Pnt (rand.NextDouble() * 100., rand.NextDouble() * 100., rand.NextDouble() * 100.)
          Radius = 0.1 + rand.NextDouble() })

let bvh = Bvh.create (balls, fun b -> BBox.createFromCenter (b.Center, 2.*b.Radius, 2.*b.Radius, 2.*b.Radius))

// exact squared surface-to-surface distance between two balls:
let sqDist a b =
    let d = max 0.0 (a.Center.DistanceTo b.Center - a.Radius - b.Radius)
    d * d

// the pair of balls with the smallest gap between their surfaces:
let pair = bvh.ClosestPair sqDist

// all pairs of balls whose surfaces are closer than 0.1:
let touching = bvh.ClosePairs (0.1, sqDist)
```

## API

The core type is the generic `Bvh<'T>`:

| Member | Description |
| --- | --- |
| `Bvh.create (items, getBox, ?leafSize)` | Builds the immutable tree from any items and a bounding box function. |
| `Bvh.createFromBoxes (boxes, ?leafSize)` | Builds the tree directly from `BBox[]`, the boxes are the items. |
| `bvh.ClosestBox (queryBox, ?skipIdx)` | The item whose bounding box is closest to a query box. |
| `bvh.ClosestBox (pt, ?skipIdx)` | The item whose bounding box is closest to a 3D point. |
| `bvh.ClosestItem (queryBox, sqDistanceTo, ?skipIdx)` | The item closest to a query, measured with an exact squared distance function. |
| `bvh.ClosestItem (pt, sqDistanceTo, ?skipIdx)` | The item closest to a 3D point, measured with an exact squared distance function. |
| `bvh.ClosestPair ()` / `bvh.ClosestPair sqDistance` | The globally closest pair, by box distance or exact distance. |
| `bvh.NearestNeighbors ()` / `bvh.NearestNeighbors sqDistance` | The nearest neighbor of every item. |
| `bvh.ClosePairs maxDistance` / `bvh.ClosePairs (maxDistance, sqDistance)` | All pairs closer than `maxDistance`, found by dual tree traversal. |
| `bvh.ItemsInBox (box, ?tolerance)` | All items whose bounding box is within `tolerance` of a given `BBox`. |
| `bvh.ItemsNearPoint (pt, ?tolerance)` | All items whose bounding box is within `tolerance` of a given 3D point. |

`Bvh2d<'T>` is the 2D equivalent, built on `BRect` instead of `BBox`. It has the same members,
with `Rect` in place of `Box` and `Pt` in place of `Pnt`:

| Member | Description |
| --- | --- |
| `Bvh2d.create (items, getRect, ?leafSize)` | Builds an immutable 2D tree from any items and a `BRect` function. |
| `Bvh2d.createFromRects (rects, ?leafSize)` | Builds a 2D tree directly from `BRect[]`, the rectangles are the items. |
| `bvh.ClosestRect (queryRect, ?skipIdx)` | The item whose bounding rectangle is closest to a query rectangle. |
| `bvh.ClosestRect (pt, ?skipIdx)` | The item whose bounding rectangle is closest to a 2D point. |
| `bvh.ClosestItem (queryRect, sqDistanceTo, ?skipIdx)` | The item closest to a query, measured with an exact squared distance function. |
| `bvh.ClosestItem (pt, sqDistanceTo, ?skipIdx)` | The item closest to a 2D point, measured with an exact squared distance function. |
| `bvh.ClosestPair ()` / `bvh.ClosestPair sqDistance` | The globally closest pair, by rectangle distance or exact distance. |
| `bvh.NearestNeighbors ()` / `bvh.NearestNeighbors sqDistance` | The nearest neighbor of every item. |
| `bvh.ClosePairs maxDistance` / `bvh.ClosePairs (maxDistance, sqDistance)` | All pairs closer than `maxDistance`, found by dual tree traversal. |
| `bvh.ItemsInRect (rect, ?tolerance)` | All items whose bounding rectangle is within `tolerance` of a given `BRect`. |
| `bvh.ItemsNearPoint (pt, ?tolerance)` | All items whose bounding rectangle is within `tolerance` of a given 2D point. |
| `bvh.Rectangle` | The bounding rectangle around all items. |

`LineBvh` is a thin wrapper over `Bvh<Line3D>` that measures exact segment-to-segment distances:

| Member | Description |
| --- | --- |
| `LineBvh.create (lines, ?leafSize)` | Builds the immutable tree from an array of `Line3D`. |
| `bvh.ClosestLine (query, ?skipIdx)` | The index of and distance to the line closest to a query line. |
| `bvh.ClosestPair ()` | The globally closest pair of lines. |
| `bvh.NearestNeighbors ()` | The nearest neighbor of every line. |
| `bvh.ClosePairs maxDistance` | All pairs of lines closer than `maxDistance` to each other, found by dual tree traversal. |
| `bvh.LinesInBox (box, ?tolerance)` | All lines whose bounding box is within `tolerance` of a given `BBox`. |
| `bvh.ClosestLine (pt, ?skipIdx)` | The index of and distance to the line closest to a 3D point. |
| `bvh.ClosestPoint pt` | The point on any line in the tree that is closest to a 3D point. |
| `bvh.LinesNearPoint (pt, ?tolerance)` | All lines whose bounding box is within `tolerance` of a given 3D point. |
| `bvh.Tree` | The underlying generic `Bvh<Line3D>`. |

`LineBvh2d` provides the corresponding `Line2D` API: `ClosestLine`, `ClosestPoint`,
`ClosestPair`, `NearestNeighbors`, `ClosePairs`, `LinesInRect`, and `LinesNearPoint`.

Full API documentation: [goswinr.github.io/Euclid.BVH](https://goswinr.github.io/Euclid.BVH)

## Build

```bash
dotnet build
```

## Test

```bash
dotnet run --project Test/Test.fsproj
```

The tests verify all queries against brute force implementations on randomized, clustered input.

## Changelog

See [CHANGELOG.md](https://github.com/goswinr/Euclid.BVH/blob/main/CHANGELOG.md)

## License

[MIT](https://github.com/goswinr/Euclid.BVH/blob/main/LICENSE)
