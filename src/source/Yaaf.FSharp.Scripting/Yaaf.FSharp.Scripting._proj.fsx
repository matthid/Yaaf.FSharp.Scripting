#if PROJGEN
#else
// Support in VS (not actually required in a build-generation script)
#I "../../../packages/Yaaf.AdvancedBuilding/tools/"
#r "Yaaf.AdvancedBuilding.dll"
open Yaaf.AdvancedBuilding
let projectInfo = Unchecked.defaultof<GlobalProjectInfo>
#endif
let framework_references_net45 =
  [ "mscorlib"
    "FSharp.Core, Version=$(TargetFSharpCoreVersion), Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
    "System"; "System.Core"; "System.Numerics" ]
  |> List.map (fun ref -> Reference { Include = ref; HintPath = ""; IsPrivate = false })
let info =
    MsBuildHelper.readMsBuildInfo "Yaaf.FSharp.Scripting.fsproj"
    |> MsBuildHelper.fixIncludes "../../../source/Yaaf.FSharp.Scripting"
let generatorConfig =
 { BuildFileList =
    [ "../../net45/source/Yaaf.FSharp.Scripting/.fsproj",
        { projectInfo.DefaultTemplateData "net45" with
            TemplateData.Includes = info.ContentIncludes @ info.ProjectReferenceIncludes @ framework_references_net45
            TemplateData.ProjectGuid = info.ProjectGuid
            TemplateData.ProjectName = info.ProjectName
            TemplateName = "fsproj_net45.cshtml" } ] }
