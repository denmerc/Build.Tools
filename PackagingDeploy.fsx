#r    @"../../../packages/FAKE/tools/FakeLib.dll"
#r "System.Xml.Linq.dll"
#r "System.IO.Compression.dll"
#load "./Utils.fsx"

open System
open System.IO
open System.IO.Compression
open System.Text.RegularExpressions
open System.Xml.Linq
open Fake
open Utils

let private nuget = @"NuGet/NuGet.exe"

let private packageDeployment (config: Map<string, string>) outputDir proj =
    async {
        let outputDirFull = match config.TryFind "packaging:outputsubdirs" with
                            | Some "true" -> outputDir + "\\" + Path.GetFileNameWithoutExtension(proj)
                            | _ -> outputDir

        Directory.CreateDirectory(outputDirFull) |> ignore

        let args =
            sprintf "pack \"%s\" -OutputDirectory \"%s\" -Properties Configuration=%s;VisualStudioVersion=%s -NoPackageAnalysis" 
                proj
                outputDirFull
                (config.get "build:configuration")
                (config.get "vs:version")
         
        let! result = 
            asyncShellExec { ExecParams.Program = findToolInSubPath "NuGet.exe" currentDirectory
                             WorkingDirectory = DirectoryName proj
                             CommandLine = args
                             Args = [] }
        
        if result <> 0 then failwithf "Error packaging NuGet package. Project file: %s" proj
        
        return result
    }

let private getPackageName nupkg =
    // Regex turns D:\output\My.Package.1.0.0.0.nupkg into My.Package
    let regex = new Regex(".*\\\\([^\\\\]+)\\.[\\d]+\\.[\\d]+\\.[\\d]+[\\.\\-][\\d\\-a-zA-Z]+\\.nupkg")
    regex.Replace(nupkg, "$1")

let private pushPackagesToDir (config: Map<string, string>) dir nupkg =
    let info = new FileInfo(nupkg)
    let name = getPackageName nupkg
    let directory = sprintf "%s\%s" dir name
    if (not (Directory.Exists(directory))) then 
        Directory.CreateDirectory(directory) |> ignore
    let file = info.CopyTo(sprintf "%s\%s" directory info.Name, true)
    sprintf "Pushed File: %s to: %s" info.Name directory |> ignore

let private pushPackagesToUrl (config: Map<string, string>) pushurl apikey nupkg =
    let args =
        sprintf "push \"%s\" %s -s \"%s\""
            nupkg
            apikey
            pushurl
    let result =
        ExecProcess (fun info ->
            info.FileName <- config.get "core:tools" @@ nuget
            info.WorkingDirectory <- DirectoryName nupkg
            info.Arguments <- args) (TimeSpan.FromMinutes 5.)

    if result <> 0 then failwithf "Error pushing NuGet package %s" nupkg

let pushPackages (config: Map<string, string>) pushto pushdir pushurl apikey nupkg =
    match pushto, pushdir with
    | Some "dir", Some dir ->
        if isNullOrEmpty dir then failwith "You must specify pushdir to push NuGet packages with the pushto=dir option."
        pushPackagesToDir config dir nupkg
    | Some "dir", None ->
        failwith "You must specify pushdir to push NuGet packages with the pushto=dir option."
    | Some "url", _ | None, _ | _, _ ->
        if isNullOrEmpty pushurl || isNullOrEmpty apikey then failwith "You must specify both apikey and pushurl to push NuGet packages with the pushto=url option."
        pushPackagesToUrl config pushurl apikey nupkg

let cleanDirOnceHistory = new System.Collections.Generic.List<string>()
let CleanDirOnce dir =
    if (cleanDirOnceHistory.Contains(dir)) = false then
        cleanDirOnceHistory.Add(dir)
        CleanDir dir

let package (config : Map<string, string>) _ =
    CleanDirOnce (config.get "packaging:deployoutput")

    let nuspecSearch = match config.TryFind "packaging:deploynuspecsearch" with
                       | Some x when String.IsNullOrEmpty x = false -> x
                       | _ -> "./**/Deploy/*.nuspec"
    
    !! nuspecSearch
    |> Seq.map (packageDeployment config (config.get "packaging:deployoutput"))
    |> Async.Parallel
    |> Async.Ignore
    |> Async.RunSynchronously

let push (config : Map<string, string>) _ =
    let pushto = config.TryFind "packaging:deploypushto"
    let pushdir = config.TryFind "packaging:deploypushdir"
    let pushurl = config.get "packaging:deploypushurl"
    let apikey = config.get "packaging:deployapikey"
    !! (config.get "packaging:deployoutput" @@ "./**/*.nupkg")
        |> Seq.iter (pushPackages config pushto pushdir pushurl apikey)

