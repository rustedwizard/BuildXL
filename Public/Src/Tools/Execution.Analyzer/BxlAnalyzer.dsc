// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as Managed from "Sdk.Managed";
import * as GrpcSdk from "Sdk.Protocols.Grpc";
import {VSCode} from "BuildXL.Ide";

namespace Execution.Analyzer {

    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "bxlanalyzer",
        appConfig: f`App.config`,
        generateLogs: true,
        rootNamespace: "BuildXL.Execution.Analyzer",
        skipDocumentationGeneration: true,
        addNotNullAttributeFile: true,
        sources: globR(d`.`, "*.cs"),
        
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.Netstandard.dll,
                NetFx.System.IO.dll,
                NetFx.System.Web.dll,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.IO.Compression.dll,
                NetFx.System.Net.Http.dll,
                NetFx.System.Runtime.Serialization.dll,
                importFrom("System.Memory").pkg
            ]),
            VSCode.DebugAdapter.dll,
            VSCode.DebugProtocol.dll,
            importFrom("Antlr4.Runtime.Standard").pkg,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.Ide").Script.Debugger.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").ProcessPipExecutor.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Ide").Generator.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("Microsoft.Identity.Client").pkg,
            importFrom("Microsoft.Identity.Client.Extensions.Msal").pkg,
            importFrom("Microsoft.TeamFoundationServer.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("ZstdSharp.Port").pkg,
        ],
        internalsVisibleTo: ["Test.Tool.Analyzers"],
        defineConstants: addIf(BuildXLSdk.Flags.isVstsArtifactsEnabled, "FEATURE_VSTS_ARTIFACTSERVICES"),
        gcHeapCount: 3
    });
}
