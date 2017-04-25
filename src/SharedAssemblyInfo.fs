namespace System
open System.Reflection

[<assembly: AssemblyCompanyAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyProductAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyCopyrightAttribute("Yaaf.FSharp.Scripting Copyright © Matthias Dittrich 2015")>]
[<assembly: AssemblyVersionAttribute("2.0.0")>]
[<assembly: AssemblyFileVersionAttribute("2.0.0")>]
[<assembly: AssemblyInformationalVersionAttribute("2.0.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "2.0.0"
    let [<Literal>] InformationalVersion = "2.0.0"
