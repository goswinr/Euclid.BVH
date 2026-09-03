module Euclid.BVH.Tests
open System

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
let test x = Mocha.runTests x
#else
open Expecto
open System.Globalization
open System.Threading
Thread.CurrentThread.CurrentCulture   <- CultureInfo.GetCultureInfo "en-US" // so that a float never has a comma as decimal separator
Thread.CurrentThread.CurrentUICulture <- CultureInfo.GetCultureInfo "en-US"
let mutable cliArgs : string[] = [||]
let test x = runTestsWithCLIArgs [] cliArgs x
#endif

let run () =
    test TestBvh.tests
    |||
    test TestBvh2d.tests
    |||
    test TestLineBvh.tests

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
#nowarn "20" //The result of this expression has type 'int' and is implicitly ignored.
run()
#else

[<EntryPoint>]
let main (args: string[]) =
    cliArgs <- args
    let r = run ()
    if r = 0 then
        printfn "All tests passed"
    else
        printfn "%d tests failed" r
    r
#endif
