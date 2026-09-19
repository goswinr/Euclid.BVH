module Euclid.BVH.Tests

open Scriptorium.Quill
open type Scriptorium.Quill.Runner

#if !FABLE_COMPILER
open System.Globalization
open System.Threading
// so that a float never has a comma as decimal separator in an assertion message:
Thread.CurrentThread.CurrentCulture   <- CultureInfo.GetCultureInfo "en-US"
Thread.CurrentThread.CurrentUICulture <- CultureInfo.GetCultureInfo "en-US"
#endif

// noTimeout: the brute force reference implementations these tests compare against are
// quadratic, so a single test can easily run longer than Quill's 5 second default.
[<EntryPoint>]
let main _ =
    runTestsWith (
        noTimeout >> slowThreshold 2000,
        [
            TestBvh.tests
            TestBvh2D.tests
            TestLineBvh.tests
        ])
