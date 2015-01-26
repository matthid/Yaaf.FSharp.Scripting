namespace Yaaf.FSharp.Scripting
open Yaaf.FSharp.Scripting
type ScriptHost private() =
    static member CreateNew (?defines : string list) =
        Helper.getSession(defaultArg defines [])
