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
  parsed

[<Test>]
let ``Check that fsi arg-parser works`` () =
  argTest [| "--noninteractive"; "-g+"; "--warnaserror+:34,42" ; "--"; "test" ; "more" |] |> ignore
  
  argTest [| "-I:first"; "-I:second" |]|> ignore
  argTest [| "-r:first"; "-r:second" |]|> ignore
  argTest [| "--load:first"; "--load:second" |]|> ignore
  argTest [| "--use:first"; "--use:second" |]|> ignore
