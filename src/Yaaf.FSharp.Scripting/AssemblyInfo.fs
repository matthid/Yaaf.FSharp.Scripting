namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyProductAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyDescriptionAttribute("A helper library to easily add F# scripts to your application.")>]
[<assembly: AssemblyVersionAttribute("1.0.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0.0"
