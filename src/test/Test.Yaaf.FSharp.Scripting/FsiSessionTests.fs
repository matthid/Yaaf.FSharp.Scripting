module Test.Yaaf.FSharp.Scripting.FsiSessionTests

open NUnit.Framework
open Swensen.Unquote
open Yaaf.FSharp.Scripting
let fsiSession = ScriptHost.CreateNew()

[<Test>]
let ``let with a given integer type works`` () =
    fsiSession.Let "test" 25
    test <@ fsiSession.EvalExpression<int> "test" = 25 @>

[<Test>]
let ``let with a given integer option type works`` () =
    fsiSession.Let "test" (Some 25)
    test <@ fsiSession.EvalExpression<int option> "test" = Some 25 @>

