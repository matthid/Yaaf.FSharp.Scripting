// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
#load "packages/Yaaf.AdvancedBuilding/content/buildConfigDef.fsx"
#load @"buildConfig.fsx"
#load "packages/Yaaf.AdvancedBuilding/content/generateDocsInclude.fsx"
open Fake
RunTargetOrDefault "LocalDoc"