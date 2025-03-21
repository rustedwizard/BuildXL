// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Ninja;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using Test.DScript.Ast;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.FrontEnd.MsBuild.Infrastructure;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.TestEnv;
using Test.BuildXL.Processes;

namespace Test.BuildXL.FrontEnd.Ninja.Infrastructure
{
    /// <summary>
    /// Base class for tests that programmatically add projects and verify the corresponding scheduled process
    /// done by <see cref="NinjaResolver"/>
    /// </summary>
    /// <remarks>
    /// Meant to be used in conjunction with <see cref="NinjaSchedulingProjectBuilder"/>
    /// No pips are run by this class, the engine phase is set to <see cref="EnginePhases.Schedule"/>
    /// </remarks>
    public abstract class NinjaPipSchedulingTestBase : DsTestWithCacheBase
    {
        private readonly ModuleDefinition m_testModule;
        private readonly AbsolutePath m_configFilePath;
        protected string BogusExecutable = FileUtilities.GetTempFileName();

        /// <summary>
        /// <see cref="CmdHelper.CmdX64"/>
        /// </summary>
        protected string CMD => CmdHelper.CmdX64;

        protected string BASH => CmdHelper.Bash;

        protected AbsolutePath TestPath { get; }

        /// <nodoc/>
        public NinjaPipSchedulingTestBase(ITestOutputHelper output, bool usePassThroughFileSystem = false) : base(output, usePassThroughFileSystem)
        {
            TestPath = AbsolutePath.Create(PathTable, TestRoot);

            m_testModule = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                ModuleDescriptor.CreateForTesting("Test"),
                TestPath,
                TestPath.Combine(PathTable, "config.dsc"),
                new[] { TestPath.Combine(PathTable, "build.ninja") },
                allowedModuleDependencies: null,
                cyclicalFriendModules: null,
                mounts: null,
                scrubDirectories: null);

            m_configFilePath = TestPath.Combine(PathTable, "config.dsc");

            PopulateMainConfigAndPrelude();
        }

        protected override TestPipGraph GetPipGraph() => new TestPipGraph();

        /// <summary>
        /// Starts the addition of projects
        /// </summary>
        /// <returns></returns>
        public NinjaSchedulingProjectBuilder Start(NinjaResolverSettings resolverSettings = null, QualifierId qualifierId = default)
        {
            var settings = resolverSettings ?? new NinjaResolverSettings();
            // We want to have both ProjectRoot and SpecFile defined
            if (settings.Root == AbsolutePath.Invalid)
            {
                settings.Root = AbsolutePath.Create(PathTable, TestRoot);
            }

            if (settings.SpecFile == AbsolutePath.Invalid)
            {
                settings.SpecFile = settings.Root.Combine(PathTable, "build.ninja");
            }

            if (qualifierId == default)
            {
                qualifierId = FrontEndContext.QualifierTable.CreateQualifier(CollectionUtilities.EmptyDictionary<string, string>());
            }

            return new NinjaSchedulingProjectBuilder(this, settings, qualifierId);
        }

        /// <summary>
        /// Helper method to create a project with predictions rooted at the test root
        /// </summary>
        /// <returns></returns>
        public NinjaNode CreateNinjaNode(
            string rule = null,
            string command = null,
            IReadOnlySet<AbsolutePath> inputs = null, 
            IReadOnlySet<AbsolutePath> outputs = null, 
            IReadOnlyCollection<NinjaNode> dependencies = null)
        {
            return new NinjaNode(
                    rule ?? "",
                    command ?? (OperatingSystemHelper.IsWindowsOS ? $@"{CMD} /C ""cd .""" : $@"{BASH} -c ""cd ."""),
                    inputs ?? new ReadOnlyHashSet<AbsolutePath>(),
                    outputs ?? new ReadOnlyHashSet<AbsolutePath>(),
                    dependencies ?? CollectionUtilities.EmptySet<NinjaNode>()
                );
        }

        /// <summary>
        /// Schedule the specified nodes and retrieves the result
        /// </summary>
        internal NinjaSchedulingResult ScheduleAll(NinjaResolverSettings resolverSettings, IList<NinjaNode> nodes, QualifierId qualifierId)
        {
            var moduleRegistry = new ModuleRegistry(FrontEndContext.SymbolTable);
            var frontEndFactory = CreateFrontEndFactoryForEvaluation(ParseAndEvaluateLogger);

            using (var controller = CreateFrontEndHost(GetDefaultCommandLine(), frontEndFactory, moduleRegistry, AbsolutePath.Invalid, out _, out _))
            {
                resolverSettings.ComputeEnvironment(FrontEndContext.PathTable, out var environment, out var passthroughEnv, out var _);

                var pipConstructor = new NinjaPipConstructor(
                    FrontEndContext,
                    controller,
                    "NinjaFrontEnd",
                    m_testModule,
                    qualifierId,
                    resolverSettings.Root,
                    resolverSettings.SpecFile,
                    new ()
                    {
                        UserDefinedEnvironment = environment,
                        UserDefinedPassthroughVariables = passthroughEnv,
                        UntrackingSettings = resolverSettings,
                        AdditionalOutputDirectories = resolverSettings.AdditionalOutputDirectories
                    });

                var schedulingResults = new Dictionary<NinjaNode, (bool, Process)>();

                foreach (var node in nodes)
                {
                    var result = pipConstructor.TrySchedulePip(node, qualifierId, out Process process);
                    schedulingResults[node] = (result, process);
                }

                return new NinjaSchedulingResult(PathTable, controller.PipGraph, schedulingResults);
            }
        }

        private void PopulateMainConfigAndPrelude()
        {
            FileSystem.WriteAllText(m_configFilePath, "config({});");
            var preludeDir = TestPath.Combine(PathTable, FrontEndHost.PreludeModuleName);
            FileSystem.CreateDirectory(preludeDir);
            var preludeModule = ModuleConfigurationBuilder.V1Module(FrontEndHost.PreludeModuleName, mainFile: "Prelude.dsc");
            FileSystem.WriteAllText(preludeDir.Combine(PathTable, "package.config.dsc"), preludeModule.ToString());
            FileSystem.WriteAllText(preludeDir.Combine(PathTable, "Prelude.dsc"), SpecEvaluationBuilder.FullPreludeContent);
        }

        private CommandLineConfiguration GetDefaultCommandLine()
        {
            return new CommandLineConfiguration
            {
                Startup =
                    {
                        ConfigFile = m_configFilePath,
                    },
                FrontEnd = new FrontEndConfiguration
                    {
                        ConstructAndSaveBindingFingerprint = false,
                        EnableIncrementalFrontEnd = false,
                    },
                Engine =
                    {
                        TrackBuildsInUserFolder = false,
                        Phase = EnginePhases.Schedule,
                    },
                Schedule =
                    {
                        MaxProcesses = DegreeOfParallelism
                    },
                Layout =
                    {
                        SourceDirectory = m_configFilePath.GetParent(PathTable),
                        OutputDirectory = m_configFilePath.GetParent(PathTable).GetParent(PathTable).Combine(PathTable, "Out"),
                        PrimaryConfigFile = m_configFilePath,
                        BuildEngineDirectory = TestPath.Combine(PathTable, "bin")
                    },
                Cache =
                    {
                        CacheSpecs = SpecCachingOption.Disabled
                    },
            };
        }
    }
}
