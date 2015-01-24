[<AutoOpen>]
module Yaaf.FSharp.Scripting.Extensions

open System

type IFsiSession with
    /// Assigns the given object to the given name (ie "let varName = obj") 
    member x.Let<'a> varName obj =
        let typeName = typeof<'a>.FSharpFullNameWithTypeArgs
        x.EvalInteraction (sprintf "let __hook = HookHelper<%s>.Empty" typeName)
        let __hook = x.EvalExpression<HookHelper<'a>> "__hook"
        __hook.item <- Some obj
        x.EvalInteraction (sprintf "let %s = __hook.item.Value" varName)
        
    member x.Open ns = 
        x.EvalInteraction (sprintf "open %s" ns)
    member x.Reference file = 
        x.EvalInteraction (sprintf "#r @\"%s\"" file)
        