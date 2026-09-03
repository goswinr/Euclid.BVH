# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- Point queries on `Bvh<'T>`: `ClosestBox (pt)`, `ClosestItem (pt, sqDistanceTo)` and `ItemsNearPoint (pt, ?tolerance)` for querying with a single 3D point (`Pnt`).
- Point queries on `LineBvh`: `ClosestLine (pt)`, `ClosestPoint (pt)` and `LinesNearPoint (pt, ?tolerance)`.
- `Bvh<'T>`: a generic static Bounding Volume Hierarchy over any item type, built from items plus a bounding box function (`Bvh.create`) or directly from `BBox[]` (`Bvh.createFromBoxes`).
- Box based queries on `Bvh<'T>`: `ClosestBox`, `ClosestPair`, `NearestNeighbors`, `ClosePairs` and `ItemsInBox`.
- Exact distance queries on `Bvh<'T>` via squared distance callbacks: `ClosestItem`, `ClosestPair`, `NearestNeighbors` and `ClosePairs` overloads.
- `BvhPair` result type for pair queries.
- `LineBvh.Tree` exposing the underlying generic `Bvh<Line3D>`.

### Changed
- `LineBvh` is now a thin wrapper around `Bvh<Line3D>`. Its public API is unchanged.
- `LinePair` is now an alias for `BvhPair`.

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
