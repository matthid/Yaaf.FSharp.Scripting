namespace System
open System.Reflection

[<assembly: AssemblyCompanyAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyProductAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyCopyrightAttribute("Yaaf.FSharp.Scripting Copyright © Matthias Dittrich 2015")>]
[<assembly: AssemblyVersionAttribute("1.6.0")>]
[<assembly: AssemblyFileVersionAttribute("1.6.0")>]
[<assembly: AssemblyInformationalVersionAttribute("1.6.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.6.0"
    let [<Literal>] InformationalVersion = "1.6.0"
