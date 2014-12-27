﻿namespace Persimmon.VisualStudio.TestExplorer

open Microsoft.VisualStudio.TestPlatform.ObjectModel
open Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter
open System
open System.Reflection

type VSTestRunner() =
  inherit MarshalByRefObject()

  let persimmonFamilies = [
    "Persimmon.dll"
    "Persimmon.Runner.dll"
  ]

  let loadAssembly source =
    let assemblyRef = AssemblyName.GetAssemblyName(source)
    Assembly.Load(assemblyRef)

  let collectTestCase (collector: Persimmon.Runner.Wrapper.TestCollector) sources =
      sources
      |> Seq.filter (fun (x: string) -> persimmonFamilies |> List.exists (fun y -> x.EndsWith(y)) |> not)
      |> Seq.map loadAssembly
      |> collector.CollectRootTestObjects
      |> Seq.map VSTestCase.ofWrapperTestCase

  let runAllTestCase (runner: Persimmon.Runner.Wrapper.TestRunner) sources =
      sources
      |> Seq.filter (fun (x: string) -> persimmonFamilies |> List.exists (fun y -> x.EndsWith(y)) |> not)
      |> Seq.map loadAssembly
      |> runner.RunAllTests
      |> Seq.map (fun (c, r) -> VSTestResult.ofWrapperTestResult (VSTestCase.ofWrapperTestCase c) r)

  member private __.Run<'T when 'T :> MarshalByRefObject>(f: 'T -> unit) =
    let fullPath = Assembly.GetExecutingAssembly().Location
    let directory = IO.Path.GetDirectoryName(fullPath)
    let setup = AppDomainSetup(LoaderOptimization = System.LoaderOptimization.MultiDomain, PrivateBinPath = directory, ApplicationBase = directory, DisallowBindingRedirects = true)
    let evidence = AppDomain.CurrentDomain.Evidence
    let appDomain = AppDomain.CreateDomain("persimmon visual studio test explorer domain", evidence, setup)
    try
      let t = appDomain.CreateInstanceAndUnwrap("Persimmon.Runner.Wrapper", typeof<'T>.FullName) :?> 'T
      f t
    finally
      AppDomain.Unload(appDomain)

  member this.DiscoverTests(sources: string seq, sink: ITestCaseDiscoverySink) =
    this.Run<Persimmon.Runner.Wrapper.TestCollector>(fun collector ->
      sources
      |> collectTestCase collector
      |> Seq.iter (fun c -> sink.SendTestCase(c))
    )
  member this.RunTests(sources: string seq, handle: IFrameworkHandle) =
    this.Run<Persimmon.Runner.Wrapper.TestRunner>(fun runner ->
      sources
      |> Seq.distinct
      |> runAllTestCase runner
      |> Seq.iter handle.RecordResult
    )
