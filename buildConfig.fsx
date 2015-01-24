// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------

(*
    This file handles the configuration of the Yaaf.AdvancedBuilding build script.

    The first step is handled in build.sh and build.cmd by restoring either paket dependencies or bootstrapping a NuGet.exe and 
    executing NuGet to resolve all build dependencies (dependencies required for the build to work, for example FAKE).

    The secound step is executing build.fsx which loads this file (for configuration), builds the solution and executes all unit tests.
*)


#I @"packages/FAKE/tools/"
#r @"FakeLib.dll"

open System.Collections.Generic
open System.IO

open Fake
open Fake.Git
open Fake.FSharpFormatting
open AssemblyInfoFile


// properties (main)
let projectName = "Yaaf.FSharp.Scripting"
let copyrightNotice = "Yaaf.FSharp.Scripting Copyright © Matthias Dittrich 2015"
let projectSummary = "A helper library to easily add F# scripts to your application."
let projectDescription = "This library builds on top of the FSharp.Compiler.Service library and provides a nice API for F# script integration. It provides APIs to push values into and to get values from scripts. Additionally it adds some extension methods missing from the FSharp.Compiler.Service API."
let authors = ["Matthias Dittrich"]
let page_author = "Matthias Dittrich"
let mail = "matthi.d@gmail.com"
// Read release notes document
let release = ReleaseNotesHelper.parseReleaseNotes (File.ReadLines "doc/ReleaseNotes.md")
let version = release.AssemblyVersion
let version_nuget = release.NugetVersion


let github_user = "matthid"
let github_project = "Yaaf.FSharp.Scripting"
let nuget_url = "https://www.nuget.org/packages/Yaaf.FSharp.Scripting/"

let tags = "fsharp scripting compiler host"

let generated_file_list =
  [ "Yaaf.FSharp.Scripting.dll"
    "Yaaf.FSharp.Scripting.xml" ]

type BuildParams =
    {
        SimpleBuildName : string
        CustomBuildName : string
    }

//let profile111Params = { SimpleBuildName = "profile111"; CustomBuildName = "portable-net45+netcore45+wpa81+MonoAndroid1+MonoTouch1" }
let emptyParams = { SimpleBuildName = ""; CustomBuildName = "" }
//let net45Params = { SimpleBuildName = "net45"; CustomBuildName = "net45" }

let allParams = [ emptyParams ]

let use_nuget = false

let buildDir = "./build/"
let releaseDir = "./release/"
let outNugetDir = "./release/nuget/"
let outLibDir = "./release/lib/"
let outDocDir = "./release/documentation/"
let docTemplatesDir = "./doc/templates/"
let testDir  = "./test/"

let buildMode = "Release" // if isMono then "Release" else "Debug"

let github_url = sprintf "https://github.com/%s/%s" github_user github_project

if isMono then
    monoArguments <- "--runtime=v4.0 --debug"
    //monoArguments <- "--runtime=v4.0"

// Where to look for *.cshtml templates (in this order)
let layoutRoots =
    [ docTemplatesDir; 
      docTemplatesDir @@ "reference" ]

let setVersion () = 
  let info =
      [Attribute.Company projectName
       Attribute.Product projectName
       Attribute.Copyright copyrightNotice
       Attribute.Version version
       Attribute.FileVersion version
       Attribute.InformationalVersion version_nuget]
  CreateFSharpAssemblyInfo "./src/SharedAssemblyInfo.fs" info
  
let nugetPackages =
  [ "Yaaf.FSharp.Scripting.nuspec", (fun p ->
      { p with
          Authors = authors
          Project = projectName
          Summary = projectSummary
          Description = projectDescription
          Version = version_nuget
          ReleaseNotes = toLines release.Notes
          Tags = tags
          Dependencies = 
            [ "FSharp.Compiler.Service", "0.0.82" ] }) ]
    
let findProjectFiles (buildParams:BuildParams) =
    !! (sprintf "src/source/**/*.fsproj")
    ++ (sprintf "src/source/**/*.csproj")

let findTestFiles (buildParams:BuildParams) =
    !! (sprintf "src/test/**/Test.*.fsproj")
    ++ (sprintf "src/test/**/Test.*.csproj")

let unitTestDlls testDir (buildParams:BuildParams) =
    !! (testDir + "/Test.*.dll")
