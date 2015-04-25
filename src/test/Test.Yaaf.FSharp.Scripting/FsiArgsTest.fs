module Test.Yaaf.FSharp.Scripting.FsiArgsTest

open System.IO
open NUnit.Framework
open Test.Yaaf.FSharp.Scripting.FsiUnquote
open Yaaf.FSharp.Scripting

let argTest args =
  let parsed = FsiOptions.ofArgs args
  let toArgs = parsed.AsArgs
  let parsed2 = FsiOptions.ofArgs toArgs
  test <@ parsed = parsed2 @>

[<Test>]
let ``Check that fsi arg-parser works`` () =
  argTest [| "--noninteractive"; "-g+"; "--warnaserror+:34,42" ; "--"; "test" ; "more" |]
  
  argTest [| "-I:first"; "-I:second" |]
  argTest [| "-r:first"; "-r:second" |]
  argTest [| "--load:first"; "--load:second" |]
  argTest [| "--use:first"; "--use:second" |]