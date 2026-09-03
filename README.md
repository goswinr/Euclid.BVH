# Euclid.BVH

[![Euclid.BVH on nuget.org](https://img.shields.io/nuget/v/Euclid.BVH)](https://www.nuget.org/packages/Euclid.BVH/)
[![Build Status](https://github.com/goswinr/Euclid.BVH/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Euclid.BVH/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Euclid.BVH/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Euclid.BVH/actions/workflows/test.yml)
[![Docs Build Status](https://github.com/goswinr/Euclid.BVH/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/Euclid.BVH/actions/workflows/docs.yml)
[![license](https://img.shields.io/github/license/goswinr/Euclid.BVH)](LICENSE)

A Bounding Volume Hierarchy (BVH) for fast spatial queries on the
[Euclid](https://github.com/goswinr/Euclid) geometry library.

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

## API

The core type is the generic `Bvh<'T>`:

| Member | Description |
| --- | --- |
| `Bvh.create (items, getBox, ?leafSize)` | Builds the immutable tree from any items and a bounding box function. |
| `Bvh.createFromBoxes (boxes, ?leafSize)` | Builds the tree directly from `BBox[]`, the boxes are the items. |
| `bvh.ClosestBox (queryBox, ?skipIdx)` | The item whose bounding box is closest to a query box. |
| `bvh.ClosestItem (queryBox, sqDistanceTo, ?skipIdx)` | The item closest to a query, measured with an exact squared distance function. |
| `bvh.ClosestPair ()` / `bvh.ClosestPair sqDistance` | The globally closest pair, by box distance or exact distance. |
| `bvh.NearestNeighbors ()` / `bvh.NearestNeighbors sqDistance` | The nearest neighbor of every item. |
| `bvh.ClosePairs maxDistance` / `bvh.ClosePairs (maxDistance, sqDistance)` | All pairs closer than `maxDistance`, found by dual tree traversal. |
| `bvh.ItemsInBox (box, ?tolerance)` | All items whose bounding box is within `tolerance` of a given `BBox`. |

`LineBvh` is a thin wrapper over `Bvh<Line3D>` that measures exact segment-to-segment distances:

| Member | Description |
| --- | --- |
| `LineBvh.create (lines, ?leafSize)` | Builds the immutable tree from an array of `Line3D`. |
| `bvh.ClosestLine (query, ?skipIdx)` | The index of and distance to the line closest to a query line. |
| `bvh.ClosestPair ()` | The globally closest pair of lines. |
| `bvh.NearestNeighbors ()` | The nearest neighbor of every line. |
| `bvh.ClosePairs maxDistance` | All pairs of lines closer than `maxDistance` to each other, found by dual tree traversal. |
| `bvh.LinesInBox (box, ?tolerance)` | All lines whose bounding box is within `tolerance` of a given `BBox`. |
| `bvh.Tree` | The underlying generic `Bvh<Line3D>`. |

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
