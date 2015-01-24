namespace System
open System.Reflection

[<assembly: AssemblyCompanyAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyProductAttribute("Yaaf.FSharp.Scripting")>]
[<assembly: AssemblyCopyrightAttribute("Yaaf.FSharp.Scripting Copyright © Matthias Dittrich 2015")>]
[<assembly: AssemblyVersionAttribute("0.1.2")>]
[<assembly: AssemblyFileVersionAttribute("0.1.2")>]
[<assembly: AssemblyInformationalVersionAttribute("0.1.2")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.2"
