module Test.Yaaf.FSharp.Scripting.FsiSessionTests

open NUnit.Framework
open Test.Yaaf.FSharp.Scripting.FsiUnquote
open Yaaf.FSharp.Scripting
let fsiSession = ScriptHost.CreateNew(["MYDEFINE"])
let fixNewLines (l:string) = l.Replace("\r\n", "\n") 
[<Test>]
let ``let with a given integer type works`` () =
    fsiSession.Let "test" 25
    test <@ fsiSession.EvalExpression<int> "test" = 25 @>
    
[<Test>]
let ``test that we get the correct output`` () =
    let inter = fsiSession.EvalInteractionWithOutput "3 + 4"
    test <@ fixNewLines inter.Output.FsiOutput = "val it : int = 7\n" @>

[<Test>]
let ``let with a given integer option type works`` () =
    fsiSession.Let "test" (Some 25)
    test <@ fsiSession.EvalExpression<int option> "test" = Some 25 @>

[<Test>]
let ``check that defines work works`` () =
    fsiSession.EvalInteraction("""
#if MYDEFINE
let test = 125
#else
let test = 0
#endif""")
    test <@ fsiSession.EvalExpression<int> "test" = 125 @>

[<Test>]
let ``check that fsi object works`` () =
    fsiSession.EvalInteraction ("""
fsi.AddPrinter(fun (n:int) -> n.ToString())
printfn "%d" 4
""")

[<Test>]
let ``check that compile time exceptions are wrapped`` () =
  Assert.Throws<FsiEvaluationException>(fun () ->
    fsiSession.EvalInteraction ("""
asdfasd
"""))
  |> ignore

[<Test>]
let ``check that runtime exceptions are wrapped`` () =
  Assert.Throws<FsiEvaluationException>(fun () ->
    fsiSession.EvalInteraction ("""
((failwith "game over") : unit)
"""))
  |> ignore

[<Test>]
let ``check that we get print output`` () =
    let res = fsiSession.EvalInteractionWithOutput ("""
printf "%s" "test" """)
    test <@ res.Output.ScriptOutput = "test" @>