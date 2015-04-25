/// We have the FSI-ASSEMBLY loaded, thats why we need to ensure that our Unquote 'test' method throws properly!
[<AutoOpen>]
module Test.Yaaf.FSharp.Scripting.FsiUnquote

open Swensen.Unquote

open System
open Microsoft.FSharp.Quotations

let testFailed =
    let outputReducedExprsMsg =
        fun outputTestFailedMsg (reducedExprs:Expr list) additionalInfo ->
                let msg =
                    sprintf "\n%s\n%s\n"
                        (if additionalInfo |> String.IsNullOrWhiteSpace then "" else sprintf "\n%s\n" additionalInfo)
                        (reducedExprs |> List.map decompile |> String.concat "\n")
                outputTestFailedMsg msg
    let outputNonFsiTestFailedMsg = (fun msg ->
        printfn "%s" msg
        NUnit.Framework.Assert.Fail(msg))
    outputReducedExprsMsg outputNonFsiTestFailedMsg

let reduceFullyAndGetLast expr =
    let reducedExprs = expr |> reduceFully
    let lastExpr = reducedExprs |> List.rev |> List.head
    reducedExprs, lastExpr

let inline test (expr:Expr<bool>) =
    let reducedExprs, lastExpr = reduceFullyAndGetLast expr
    match lastExpr with
    | DerivedPatterns.Bool(true) -> ()
    | _ ->
        //try
            testFailed reducedExprs ""
        //with 
        //| e -> raise e //we catch and raise e here to hide stack traces for clean test framework output

let inline expectedExnButWrongExnRaisedMsg ty1 ty2 = sprintf "Expected exception of type '%s', but '%s' was raised instead" ty1 ty2
let inline expectedExnButNoExnRaisedMsg ty1 = sprintf "Expected exception of type '%s', but no exception was raised" ty1
///Test wether the given expr fails with the given expected exception (or a subclass thereof).
let inline raises<'a when 'a :> exn> (expr:Expr) =
    let reducedExprs, lastExpr = reduceFullyAndGetLast expr
    match lastExpr with
    | Patterns.Value(lastValue,lastValueTy) when lastValue <> null && typeof<exn>.IsAssignableFrom(lastValueTy) -> //it's an exception
        if typeof<'a>.IsAssignableFrom(lastValueTy) then () //it's the correct exception
        else //it's not the correct exception
            //try
                testFailed reducedExprs (expectedExnButWrongExnRaisedMsg typeof<'a>.Name (lastValueTy.Name))
            //with
            //| e -> raise e
    | _ -> //it's not an exception
        //try
            testFailed reducedExprs (expectedExnButNoExnRaisedMsg typeof<'a>.Name)
        //with
        //| e -> raise e

///Test wether the given expr fails with the given expected exception (or a subclass thereof) when the additional assertion on the exception object holds.
let inline raisesWith<'a when 'a :> exn> (expr:Expr) (exnWhen: 'a -> Expr<bool>) =
    let reducedExprs, lastExpr = reduceFullyAndGetLast expr
    match lastExpr with
    | Patterns.Value(lastValue,lastValueTy) when lastValue <> null && typeof<exn>.IsAssignableFrom(lastValueTy) -> //it's an exception
        if typeof<'a>.IsAssignableFrom(lastValueTy) then //it's the correct exception
            //but we also need to check the exnWhen condition is true
            let lastValue = lastValue :?> 'a
            let exnWhenExpr = exnWhen lastValue
            let exnWhenReducedExprs, exnWhenLastExpr = reduceFullyAndGetLast exnWhenExpr
            match exnWhenLastExpr with
            | DerivedPatterns.Bool(true) -> () //the exnWhen condition is true
            | _ ->
                //try
                    testFailed reducedExprs (sprintf "The expected exception was raised, but the exception assertion failed:\n\nException Assertion:\n\n%s\n\nTest Expression:" (exnWhenReducedExprs |> List.map decompile |> String.concat "\n"))
                //with
                //| e -> raise e //we catch and raise e here to hide stack traces for clean test framework output

        else //it's not the correct exception
            //try
                testFailed reducedExprs (expectedExnButWrongExnRaisedMsg typeof<'a>.Name (lastValueTy.Name))
            //with
            //| e -> raise e
    | _ -> //it's not an exception
        //try
            testFailed reducedExprs (expectedExnButNoExnRaisedMsg typeof<'a>.Name)
        //with
        //| e -> raise e

let inline (=?) x y = test <@ x = y @>
let inline (<?) x y = test <@ x < y @>
let inline (>?) x y = test <@ x > y @>
let inline (<=?) x y = test <@ x <= y @>
let inline (>=?) x y = test <@ x >= y @>
let inline (<>?) x y = test <@ x <> y @>