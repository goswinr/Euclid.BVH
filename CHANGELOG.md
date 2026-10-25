# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- `Polyline2D.findAllSelfIntersectionsWithBvh` and `Polyline3D.findAllSelfIntersectionsWithBvh` return all intersecting segment pairs and their points in a `ResizeArray`, ordered by segment indices; the 3D member accepts an optional `maxSkewDistance`.
- `Polyline2D.tryFindSelfIntersectionWithBvh` and `Polyline3D.tryFindSelfIntersectionWithBvh` extension members find a self intersection with a BVH over the segments, in about `O(n log n)` instead of `O(n²)`; the 3D one takes an optional `maxSkewDistance`, 1e-6 by default.
- On .NET they are faster than testing every pair of segments from about 200 points on in 2D and from about 50 points on in 3D; the doc comments also give the Fable JavaScript sizes.
- A benchmark of them against brute force on .NET and Fable JavaScript in `Test/BenchPolylineSelfIntersection`.
- A benchmark of all queries before and after a change, on .NET and Fable JavaScript, in `Test/BenchQueries`.

### Changed
- `ClosestPair` in `BVH` and `BVH2D`, and so in `BVHLine3D` and `BVHLine2D`, bounds the search from each item by the closest pair found so far and searches only the items after it; with the exact line distance it is 2.2 to 3.4 times faster on .NET and 1.9 to 3 times faster on Fable JavaScript.
- The box and rectangle overload of `ClosestPair` returns at the first pair of touching bounds instead of searching from every item.
- `ClosePairs` with an exact distance calls the distance function only for pairs of items whose bounds are within `maxDistance`; with the exact line distance it is about 2 times faster on .NET and 1.2 times faster on Fable JavaScript.
- The closest item and nearest neighbor searches compute the distance to the bounds of each node only once.
- The exact searches from a query box or rectangle, `ClosestPair` and `NearestNeighbors` compare the bounds of an item with the best distance so far before calling the distance function.
- The `...ByDistance` sequences no longer allocate a tuple per heap entry, and Fable compiles their traversal to plain loops instead of nested sequences; they are about 1.25 to 1.3 times faster on Fable JavaScript.
- The tree builders take the bounds of an internal node from its two children instead of from another pass over all its items.

### Fixed
- `ClosePairs`, `ItemsInBox`, `ItemsInRect`, `ItemsNearPoint` and the corresponding line queries reject a NaN `maxDistance` or `tolerance` instead of returning an empty result.
- `ItemsByDistance` leaves out items whose distance is NaN; before, such an item could break the order of the whole sequence.
- `ClosestPair` with an exact distance no longer fails when one item has no finite distance to any other item.
- The errors of `ClosestItem` and `NearestNeighbors` no longer blame the skipped item when all distances are infinite or NaN.

## [0.2.1] - 2026-10-10
### Changed
- Updated to Euclid 0.52.0.
- The 2D and 3D tree builders accumulate bounding coordinates directly, avoiding temporary rectangle and box objects inside the bounds loops when compiled with Fable; the split strategy and public API are unchanged.

## [0.2.0] - 2026-09-20
### Added
- Lazy nearest-first enumeration: `BVH.BoxesByDistance` and `BVH2D.RectsByDistance` return an F# `seq` of all items ordered from the closest bounding box or rectangle outwards to the farthest, for a query box/rectangle or a query point.
- `BVH.ItemsByDistance` and `BVH2D.ItemsByDistance` do the same ordered by an exact squared distance callback instead of by the bounding volume, for a query box/rectangle or a query point.
- `BVHLine3D.LinesByDistance` and `BVHLine2D.LinesByDistance` enumerate all lines ordered by their exact segment distance to a query line or point, with optional self exclusion via `skipIdx`.
- These queries walk the tree best first with a min heap, so taking only the first few entries visits only the part of the tree that is closer than the last entry taken, and costs about as much as the corresponding `Closest*` query.

### Changed
- Breaking: renamed the public BVH types for consistent capitalization and dimensions: `Bvh` to `BVH`, `Bvh2d` to `BVH2D`, `BvhPair` to `BVHPair`, `LineBvh` to `BVHLine3D`, `LineBvh2d` to `BVHLine2D`, and `LinePair2d` to `LinePair2D`.
- The 2D and 3D tree builders now split along the axis with the greatest spread of item centers, improving pruning for long parallel items whose centers coincide along their longest axis.
- Tests now run on [Scriptorium](https://fable-hub.github.io/Scriptorium/guides/getting-started/) (`Scriptorium.Quill` and `Scriptorium.Nib`) on every target, replacing Expecto on .NET and Fable.Mocha plus the `mocha` npm package on JavaScript.
- The JavaScript tests are run by `dotnet fable --runScript` instead of `mocha`, so `Test/package.json` has no runtime test dependency left.
- Breaking: `ClosestItem`, `ClosestBox`, `ClosestRect` and `ClosestLine` now return a plain tuple `(int * float)` instead of a struct tuple, matching the rest of the public API.

### Fixed
- Tree building now limits quickselect's partition work and falls back to an in-place heap sort of the remaining range, avoiding quadratic selection on structured input orders in both 2D and 3D.
- Box, rectangle and point range queries now reject negative tolerances, including the corresponding line queries.
- Corrected the XML documentation for `ResizeArray` constructors: the input collection is used directly and must not be modified after construction.

## [0.1.0] - 2026-09-07
### Added
- `Bvh<'T>`: a generic static Bounding Volume Hierarchy over any item type, built from items plus a bounding box function (`Bvh.create`) or directly from `BBox[]` (`Bvh.createFromBoxes`).
- Box based queries on `Bvh<'T>`: `ClosestBox`, `ClosestPair`, `NearestNeighbors`, `ClosePairs` and `ItemsInBox`.
- Point queries on `Bvh<'T>`: `ClosestBox (pt)`, `ClosestItem (pt, sqDistanceTo)` and `ItemsNearPoint (pt, ?tolerance)` for querying with a single 3D point (`Pnt`).
- Exact distance queries on `Bvh<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `Bvh2D<'T>`: a 2D BVH with its own data structure, storing a `BRect` per node and running all queries directly on rectangles.
- Rectangle based queries on `Bvh2D<'T>`: `ClosestRect (queryRect)`, `ClosestRect (pt)`, `ClosestPair`, `NearestNeighbors`, `ClosePairs`, `ItemsInRect` and `ItemsNearPoint`.
- Exact distance queries on `Bvh2D<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `Bvh2D.Rects` exposing the bounding rectangle of every item.
- `LineBvh`: a static Bounding Volume Hierarchy over `Line3D`, a thin wrapper around `Bvh<Line3D>` with `LineBvh.Tree` exposing the underlying generic tree.
- `LineBvh.create` to build the tree by median splits along the longest axis.
- `LineBvh.ClosestLine` branch-and-bound nearest line query, with optional self exclusion.
- `LineBvh.ClosestPair` to find the globally closest pair of lines.
- `LineBvh.NearestNeighbors` to find the nearest neighbor of every line.
- `LineBvh.ClosePairs` dual tree traversal to find all pairs of lines closer than a maximum distance.
- `LineBvh.LinesInBox` to find all lines near an axis aligned bounding box.
- Point queries on `LineBvh`: `ClosestLine (pt)`, `ClosestPoint (pt)` and `LinesNearPoint (pt, ?tolerance)`.
- `LineBvh2D`: the 2D counterpart of `LineBvh`, built on `Bvh2D<'T>`.
- `BvhPair` result type for pair queries, with `LinePair` as an alias for it.
- Fable support: the library and all tests compile and pass with Fable (JavaScript and TypeScript), tested with Mocha in CI like the Euclid library.
- An interactive SVG nearest-neighbour visualisation for 20–20,000 random lines, stepping through queries, selecting 1–10 neighbours, showing the bounding rectangles tested by the search and comparing per-query BVH performance with brute-force closest-line search.

[Unreleased]: https://github.com/goswinr/Euclid.BVH/compare/0.2.1...HEAD
[0.2.1]: https://github.com/goswinr/Euclid.BVH/compare/0.2.0...0.2.1
[0.2.0]: https://github.com/goswinr/Euclid.BVH/compare/0.1.0...0.2.0
[0.1.0]: https://github.com/goswinr/Euclid.BVH/releases/tag/0.1.0
