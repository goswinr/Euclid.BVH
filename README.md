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

## API

The main type is `LineBvh`:

| Member | Description |
| --- | --- |
| `LineBvh.create (lines, ?leafSize)` | Builds the immutable tree from an array of `Line3D`. |
| `bvh.ClosestLine (query, ?skipIdx)` | The index of and distance to the line closest to a query line. |
| `bvh.ClosestPair ()` | The globally closest pair of lines. |
| `bvh.NearestNeighbors ()` | The nearest neighbor of every line. |
| `bvh.ClosePairs maxDistance` | All pairs of lines closer than `maxDistance` to each other, found by dual tree traversal. |
| `bvh.LinesInBox (box, ?tolerance)` | All lines whose bounding box is within `tolerance` of a given `BBox`. |

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
