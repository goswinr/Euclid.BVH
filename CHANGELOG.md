# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- `Bvh2d<'T>` and `LineBvh2d`: 2D BVHs using Euclid `BRect` bounding rectangles and `Pt` point queries.
- Rectangle based queries on `Bvh2d<'T>`: `ClosestRect (queryRect)`, `ClosestRect (pt)`, `ClosestPair`, `NearestNeighbors`, `ClosePairs`, `ItemsInRect` and `ItemsNearPoint`.
- Exact distance queries on `Bvh2d<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `Bvh2d.Rects` exposing the bounding rectangle of every item.
- Fable support: the library and all tests now compile and pass with Fable (JavaScript and TypeScript), tested with Mocha in CI like the Euclid library.
- Point queries on `Bvh<'T>`: `ClosestBox (pt)`, `ClosestItem (pt, sqDistanceTo)` and `ItemsNearPoint (pt, ?tolerance)` for querying with a single 3D point (`Pnt`).
- Point queries on `LineBvh`: `ClosestLine (pt)`, `ClosestPoint (pt)` and `LinesNearPoint (pt, ?tolerance)`.
- `Bvh<'T>`: a generic static Bounding Volume Hierarchy over any item type, built from items plus a bounding box function (`Bvh.create`) or directly from `BBox[]` (`Bvh.createFromBoxes`).
- Box based queries on `Bvh<'T>`: `ClosestBox`, `ClosestPair`, `NearestNeighbors`, `ClosePairs` and `ItemsInBox`.
- Exact distance queries on `Bvh<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `BvhPair` result type for pair queries.
- `LineBvh.Tree` exposing the underlying generic `Bvh<Line3D>`.

### Changed
- The interactive SVG visualisation keeps the previous two tree depths visible with progressively faded outlines.
- `Bvh2d<'T>` has its own 2D data structure now: its nodes store a `BRect` and all queries run directly on rectangles.
  - Before, it wrapped a `Bvh<'T>` of `BBox` with a zero Z range, which cost a third more memory per node and a third more arithmetic per distance test, and converted every query argument to 3D first.
  - The public API is unchanged, but the tree shape can differ where the split axis was picked differently.
- Tree building allocates about 8 times less and is about 2.5 times faster on big inputs:
  - the median split now uses an in place quickselect instead of sorting a copy of each index range,
  - the node array is allocated at its exact size instead of growing a `ResizeArray` and copying it,
  - and the three arrays of box centers are replaced by one scratch array.
  - The resulting trees are identical in shape and query performance.
- `LineBvh` is now a thin wrapper around `Bvh<Line3D>`. Its public API is unchanged.
- `LinePair` is now an alias for `BvhPair`.

### Fixed
- `dotnet build` failed because `Ionide.KeepAChangelog.Tasks` could not parse the indented continuation lines in the Unreleased section of this file. They are nested list items now.

## [0.1.0] - 2026-09-03
### Added
- `LineBvh`: a static Bounding Volume Hierarchy over `Line3D` built from Euclid `BBox` bounding boxes.
- `LineBvh.create` to build the tree by median splits along the longest axis.
- `LineBvh.ClosestLine` branch-and-bound nearest line query, with optional self exclusion.
- `LineBvh.ClosestPair` to find the globally closest pair of lines.
- `LineBvh.NearestNeighbors` to find the nearest neighbor of every line.
- `LineBvh.ClosePairs` dual tree traversal to find all pairs of lines closer than a maximum distance.
- `LineBvh.LinesInBox` to find all lines near an axis aligned bounding box.

[Unreleased]: https://github.com/goswinr/Euclid.BVH/compare/0.1.0...HEAD
[0.1.0]: https://github.com/goswinr/Euclid.BVH/releases/tag/0.1.0
