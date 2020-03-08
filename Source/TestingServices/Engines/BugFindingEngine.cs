﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Coyote.IO;
using Microsoft.Coyote.Runtime.Exploration;
using Microsoft.Coyote.Runtime.Logging;
using Microsoft.Coyote.TestingServices.Coverage;
using Microsoft.Coyote.TestingServices.Runtime;
using Microsoft.Coyote.TestingServices.Tracing;

namespace Microsoft.Coyote.TestingServices
{
    /// <summary>
    /// Implementation of the bug-finding engine.
    /// </summary>
    [DebuggerStepThrough]
    internal sealed class BugFindingEngine : AbstractTestingEngine
    {
        /// <summary>
        /// The readable trace, if any.
        /// </summary>
        internal string ReadableTrace { get; private set; }

        /// <summary>
        /// The reproducable trace, if any.
        /// </summary>
        internal string ReproducableTrace { get; private set; }

        /// <summary>
        /// A graph of the machines, states and events of a single test iteration.
        /// </summary>
        internal Graph Graph { get; private set; }

        /// <summary>
        /// Contains a single iteration of XML log output in the case where the IsXmlLogEnabled
        /// configuration is specified.
        /// </summary>
        private StringBuilder XmlLog;

        /// <summary>
        /// A guard for printing info.
        /// </summary>
        private int PrintGuard;

        /// <summary>
        /// Creates a new bug-finding engine.
        /// </summary>
        internal static BugFindingEngine Create(Configuration configuration) =>
            Create(configuration, LoadAssembly(configuration.AssemblyToBeAnalyzed));

        /// <summary>
        /// Creates a new bug-finding engine.
        /// </summary>
        internal static BugFindingEngine Create(Configuration configuration, Assembly assembly)
        {
            TestMethodInfo testMethodInfo = null;
            try
            {
                testMethodInfo = TestMethodInfo.GetFromAssembly(assembly, configuration.TestMethodName);
            }
            catch
            {
                Error.ReportAndExit($"Failed to get test method '{configuration.TestMethodName}' from assembly '{assembly.FullName}'");
            }

            return new BugFindingEngine(configuration, testMethodInfo);
        }

        /// <summary>
        /// Creates a new bug-finding engine.
        /// </summary>
        internal static BugFindingEngine Create(Configuration configuration, Delegate testMethod)
        {
            var testMethodInfo = new TestMethodInfo(testMethod);
            return new BugFindingEngine(configuration, testMethodInfo);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BugFindingEngine"/> class.
        /// </summary>
        private BugFindingEngine(Configuration configuration, TestMethodInfo testMethodInfo)
            : base(configuration, testMethodInfo)
        {
            this.ReadableTrace = string.Empty;
            this.ReproducableTrace = string.Empty;
            this.PrintGuard = 1;
        }

        /// <summary>
        /// Creates a new testing task.
        /// </summary>
        protected override Task CreateTestingTask()
        {
            string options = string.Empty;
            if (this.Configuration.SchedulingStrategy == SchedulingStrategy.Random ||
                this.Configuration.SchedulingStrategy == SchedulingStrategy.PCT ||
                this.Configuration.SchedulingStrategy == SchedulingStrategy.FairPCT ||
                this.Configuration.SchedulingStrategy == SchedulingStrategy.ProbabilisticRandom)
            {
                options = $" (seed:{this.RandomValueGenerator.Seed})";
            }

            this.Logger.WriteLine($"... Task {this.Configuration.TestingProcessId} is " +
                $"using '{this.Configuration.SchedulingStrategy}' strategy{options}.");

            return new Task(() =>
            {
                try
                {
                    // Invokes the user-specified initialization method.
                    this.TestMethodInfo.InitializeAllIterations();

                    int maxIterations = this.Configuration.SchedulingIterations;
                    for (int i = 0; i < maxIterations; i++)
                    {
                        if (this.CancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }

                        // Runs a new testing iteration.
                        this.RunNextIteration(i);

                        if (!this.Configuration.PerformFullExploration && this.TestReport.NumOfFoundBugs > 0)
                        {
                            break;
                        }

                        if (!this.Strategy.PrepareForNextIteration())
                        {
                            break;
                        }

                        if (this.RandomValueGenerator != null && this.Configuration.IncrementalSchedulingSeed)
                        {
                            // Increments the seed in the random number generator (if one is used), to
                            // capture the seed used by the scheduling strategy in the next iteration.
                            this.RandomValueGenerator.Seed += 1;
                        }

                        // Increases iterations if there is a specified timeout
                        // and the default iteration given.
                        if (this.Configuration.SchedulingIterations == 1 &&
                            this.Configuration.Timeout > 0)
                        {
                            maxIterations++;
                        }
                    }

                    // Invokes the user-specified test disposal method.
                    this.TestMethodInfo.DisposeAllIterations();
                }
                catch (Exception ex)
                {
                    Exception innerException = ex;
                    while (innerException is TargetInvocationException)
                    {
                        innerException = innerException.InnerException;
                    }

                    if (innerException is AggregateException)
                    {
                        innerException = innerException.InnerException;
                    }

                    if (!(innerException is TaskCanceledException))
                    {
                        ExceptionDispatchInfo.Capture(innerException).Throw();
                    }
                }
            }, this.CancellationTokenSource.Token);
        }

        /// <summary>
        /// Runs the next testing iteration.
        /// </summary>
        private void RunNextIteration(int iteration)
        {
            if (this.ShouldPrintIteration(iteration + 1))
            {
                this.Logger.WriteLine($"..... Iteration #{iteration + 1}");

                // Flush when logging to console.
                if (this.Logger is ConsoleLogger)
                {
                    Console.Out.Flush();
                }
            }

            // Runtime used to serialize and test the program in this iteration.
            SystematicTestingRuntime runtime = null;

            // Logger used to intercept the program output if no custom logger
            // is installed and if verbosity is turned off.
            InMemoryLogger runtimeLogger = null;

            // Gets a handle to the standard output and error streams.
            var stdOut = Console.Out;
            var stdErr = Console.Error;

            try
            {
                // Creates a new instance of the systematic testing runtime.
                runtime = new SystematicTestingRuntime(this.Configuration, this.Strategy);

                // If verbosity is turned off, then intercept the program log, and also redirect
                // the standard output and error streams to a nul logger.
                if (!this.Configuration.IsVerbose)
                {
                    runtimeLogger = new InMemoryLogger();
                    runtime.SetLogger(runtimeLogger);

                    var writer = TextWriter.Null;
                    Console.SetOut(writer);
                    Console.SetError(writer);
                }

                this.InitializeCustomLogging(runtime);

                if (this.Configuration.IsXmlLogEnabled)
                {
                    this.XmlLog = new StringBuilder();
                    runtime.RegisterLog(new ActorRuntimeLogXmlFormatter(XmlWriter.Create(this.XmlLog, new XmlWriterSettings() { Indent = true, IndentChars = "  ", OmitXmlDeclaration = true })));
                }

                // Runs the test and waits for it to terminate.
                runtime.RunTest(this.TestMethodInfo.Method, this.TestMethodInfo.Name);
                runtime.WaitAsync().Wait();

                // Invokes the user-specified iteration disposal method.
                this.TestMethodInfo.DisposeCurrentIteration();

                // Invoke the per iteration callbacks, if any.
                foreach (var callback in this.PerIterationCallbacks)
                {
                    callback(iteration);
                }

                // Checks that no monitor is in a hot state at termination. Only
                // checked if no safety property violations have been found.
                if (!runtime.Scheduler.BugFound)
                {
                    runtime.CheckNoMonitorInHotStateAtTermination();
                }

                if (runtime.Scheduler.BugFound)
                {
                    this.ErrorReporter.WriteErrorLine(runtime.Scheduler.BugReport);
                }

                runtime.LogWriter.LogCompletion();

                this.GatherIterationStatistics(runtime);

                if (this.TestReport.NumOfFoundBugs > 0)
                {
                    if (runtimeLogger != null)
                    {
                        this.ReadableTrace = runtimeLogger.ToString();
                        this.ReadableTrace += this.TestReport.GetText(this.Configuration, "<StrategyLog>");
                    }

                    this.ConstructReproducableTrace(runtime);
                }
            }
            finally
            {
                if (!this.Configuration.IsVerbose)
                {
                    // Restores the standard output and error streams.
                    Console.SetOut(stdOut);
                    Console.SetError(stdErr);
                }

                if (this.Configuration.PerformFullExploration && runtime.Scheduler.BugFound)
                {
                    this.Logger.WriteLine($"..... Iteration #{iteration + 1} " +
                        $"triggered bug #{this.TestReport.NumOfFoundBugs} " +
                        $"[task-{this.Configuration.TestingProcessId}]");
                }

                // Cleans up the runtime before the next iteration starts.
                runtimeLogger?.Dispose();
                runtime?.Dispose();
            }
        }

        /// <summary>
        /// Returns a report with the testing results.
        /// </summary>
        public override string GetReport()
        {
            return this.TestReport.GetText(this.Configuration, "...");
        }

        /// <summary>
        /// Tries to emit the testing traces, if any.
        /// </summary>
        public override IEnumerable<string> TryEmitTraces(string directory, string file)
        {
            int index = 0;
            // Find the next available file index.
            Regex match = new Regex("^(.*)_([0-9]+)_([0-9]+)");
            foreach (var path in Directory.GetFiles(directory))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith(file))
                {
                    var result = match.Match(name);
                    if (result.Success)
                    {
                        string value = result.Groups[3].Value;
                        if (int.TryParse(value, out int i))
                        {
                            index = Math.Max(index, i + 1);
                        }
                    }
                }
            }

            if (!this.Configuration.PerformFullExploration)
            {
                // Emits the human readable trace, if it exists.
                if (!string.IsNullOrEmpty(this.ReadableTrace))
                {
                    string readableTracePath = directory + file + "_" + index + ".txt";

                    this.Logger.WriteLine($"..... Writing {readableTracePath}");
                    File.WriteAllText(readableTracePath, this.ReadableTrace);
                    yield return readableTracePath;
                }
            }

            if (this.Configuration.IsXmlLogEnabled)
            {
                string xmlPath = directory + file + "_" + index + ".trace.xml";
                this.Logger.WriteLine($"..... Writing {xmlPath}");
                File.WriteAllText(xmlPath, this.XmlLog.ToString());
                yield return xmlPath;
            }

            if (this.Graph != null)
            {
                string graphPath = directory + file + "_" + index + ".dgml";
                this.Graph.SaveDgml(graphPath, true);
                this.Logger.WriteLine($"..... Writing {graphPath}");
                yield return graphPath;
            }

            if (!this.Configuration.PerformFullExploration)
            {
                // Emits the reproducable trace, if it exists.
                if (!string.IsNullOrEmpty(this.ReproducableTrace))
                {
                    string reproTracePath = directory + file + "_" + index + ".schedule";

                    this.Logger.WriteLine($"..... Writing {reproTracePath}");
                    File.WriteAllText(reproTracePath, this.ReproducableTrace);
                    yield return reproTracePath;
                }
            }

            this.Logger.WriteLine($"... Elapsed {this.Profiler.Results()} sec.");
        }

        /// <summary>
        /// Gathers the exploration strategy statistics for the latest testing iteration.
        /// </summary>
        private void GatherIterationStatistics(SystematicTestingRuntime runtime)
        {
            TestReport report = runtime.Scheduler.GetReport();
            if (this.Configuration.ReportActivityCoverage)
            {
                report.CoverageInfo.CoverageGraph = this.Graph;
            }

            var coverageInfo = runtime.GetCoverageInfo();
            report.CoverageInfo.Merge(coverageInfo);
            this.TestReport.Merge(report);

            // Also save the graph snapshot of the last iteration, if there is one.
            this.Graph = coverageInfo.CoverageGraph;
        }

        /// <summary>
        /// Constructs a reproducable trace.
        /// </summary>
        private void ConstructReproducableTrace(SystematicTestingRuntime runtime)
        {
            StringBuilder stringBuilder = new StringBuilder();

            if (this.Strategy.IsFair())
            {
                stringBuilder.Append("--fair-scheduling").Append(Environment.NewLine);
            }

            if (this.Configuration.IsLivenessCheckingEnabled)
            {
                stringBuilder.Append("--liveness-temperature-threshold:" +
                    this.Configuration.LivenessTemperatureThreshold).
                    Append(Environment.NewLine);
            }

            if (!string.IsNullOrEmpty(this.Configuration.TestMethodName))
            {
                stringBuilder.Append("--test-method:" +
                    this.Configuration.TestMethodName).
                    Append(Environment.NewLine);
            }

            for (int idx = 0; idx < runtime.Scheduler.ScheduleTrace.Count; idx++)
            {
                ScheduleStep step = runtime.Scheduler.ScheduleTrace[idx];
                if (step.Type == ScheduleStepType.SchedulingChoice)
                {
                    stringBuilder.Append($"({step.ScheduledOperationId})");
                }
                else if (step.BooleanChoice != null)
                {
                    stringBuilder.Append(step.BooleanChoice.Value);
                }
                else
                {
                    stringBuilder.Append(step.IntegerChoice.Value);
                }

                if (idx < runtime.Scheduler.ScheduleTrace.Count - 1)
                {
                    stringBuilder.Append(Environment.NewLine);
                }
            }

            this.ReproducableTrace = stringBuilder.ToString();
        }

        /// <summary>
        /// Returns true if the engine should print the current iteration.
        /// </summary>
        private bool ShouldPrintIteration(int iteration)
        {
            if (iteration > this.PrintGuard * 10)
            {
                var count = iteration.ToString().Length - 1;
                var guard = "1" + (count > 0 ? string.Concat(Enumerable.Repeat("0", count)) : string.Empty);
                this.PrintGuard = int.Parse(guard);
            }

            return iteration % this.PrintGuard == 0;
        }
    }
}
