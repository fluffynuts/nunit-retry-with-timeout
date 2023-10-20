using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;
using PeanutButter.Utils;

namespace NUnit;

/// <summary>
/// Decoration to apply to NUnit tests which allow
/// for retrying the test with a per-attempt timeout
/// and an optional overall timeout
/// </summary>
public class RetryWithTimeout : NUnitAttribute, IRepeatTest
{
    /// <summary>
    /// To be used in testing - disable the automatic behavior
    /// to not time things out when a debugger is attached
    /// </summary>
    public static bool DisableTimeoutsWhenDebugging { get; set; }

    /// <summary>
    /// For test purposes: normally, when debugging, the test will
    /// be run inline, instead of in a new process, so you can debug it
    /// - however, _i_ need to debug the retry behavior...
    /// </summary>
    public static bool DebugInline { get; set; }

    /// <summary>
    /// How many times to retry the test on _any_ exception
    /// </summary>
    public int Retries { get; }

    /// <summary>
    /// Maximum amount of time to wait for any individual
    /// execution of this test to succeed
    /// </summary>
    public int TestTimeoutMilliseconds { get; }

    /// <summary>
    /// (Optional) Maximum amount of time to wait for
    /// any attempt to succeed overall. If not specified, this
    /// is assumed to be {retries} * {test timeout}
    /// </summary>
    public int OverallTimeoutMilliseconds { get; }

    /// <summary>
    /// Specifies that the test should be retried up
    /// to the specified number of times, with a time
    /// limit per retry and an optional overall time limit
    /// </summary>
    /// <param name="retries"></param>
    /// <param name="testTimeoutMilliseconds"></param>
    /// <param name="overallTimeoutMilliseconds"></param>
    public RetryWithTimeout(
        int retries,
        int testTimeoutMilliseconds,
        int overallTimeoutMilliseconds = 0
    )
    {
        Retries = retries;
        TestTimeoutMilliseconds = testTimeoutMilliseconds;
        OverallTimeoutMilliseconds = overallTimeoutMilliseconds > 0
            ? overallTimeoutMilliseconds
            /* allow some leeway for time taken outside the tests */
            : (retries * testTimeoutMilliseconds) + (500 * retries);
    }

    /// <inheritdoc />
    public TestCommand Wrap(TestCommand command)
    {
        return new RetryWithTimeoutCommand(
            command,
            Retries,
            TestTimeoutMilliseconds,
            OverallTimeoutMilliseconds
        );
    }

    internal class RetryWithTimeoutCommand
        : DelegatingTestCommand
    {
        // allow up to 10 seconds for dotnet to get itself together
        // for a testing process
        public const int DOTNET_TEST_PROCESS_MAX_OVERHEAD_MS = 10000;
        public const int MAX_WAIT_TIME_MS = int.MaxValue - 1;
        private readonly int _tryCount;
        private readonly int _testTimeoutMilliSeconds;
        private readonly int _overallTimeoutMilliSeconds;

        private bool _isChild;

        internal RetryWithTimeoutCommand(
            TestCommand innerCommand,
            int tryCount,
            int testTimeoutMilliSeconds,
            int overallTimeoutMilliSeconds
        ) : base(innerCommand)
        {
            _tryCount = tryCount;
            _testTimeoutMilliSeconds = testTimeoutMilliSeconds;
            _overallTimeoutMilliSeconds = overallTimeoutMilliSeconds;
        }

        private readonly Guid _identifier = Guid.NewGuid();

        private const string ISOLATION_MARKER_ENVIRONMENT_VARIABLE =
            "__RETRY_WITH_TIMEOUT_SINGLE_TEST_RUN_IDENTIFIER__";

        private const string TEST_ATTEMPT_ENVIRONMENT_VARIABLE = "__TEST_ATTEMPT__";

        private string GenerateIsolationMarker()
        {
            return $">>> test execution starts: {_identifier}";
        }

        /// <summary>
        /// Runs the test, saving a TestResult in the supplied TestExecutionContext.
        /// </summary>
        /// <param name="context">The context in which the test should run.</param>
        /// <returns>A TestResult</returns>
        public override TestResult Execute(TestExecutionContext context)
        {
            var envVar = Environment.GetEnvironmentVariable(ISOLATION_MARKER_ENVIRONMENT_VARIABLE);
            if (envVar is not null)
            {
                _isChild = true;
                LogDebug(
                    $"Will enforce timings: {EnforceTimings} (debugger timeouts disabled: {DisableTimeoutsWhenDebugging}, debugger attached: {Debugger.IsAttached})"
                );
                Console.Error.WriteLine(envVar);
                Console.ReadLine();
                context.CurrentResult = innerCommand.Execute(context);
                return context.CurrentResult;
            }

            LogDebug(
                $"Will enforce timings: {EnforceTimings} (debugger timeouts disabled: {DisableTimeoutsWhenDebugging}, debugger attached: {Debugger.IsAttached})"
            );

            var count = _tryCount;
            var overallTimer = new Stopwatch();
            Exception lastException = null;
            for (var i = 0; i < count; i++)
            {
                LogDebug($"i :: {i}");
                if (i == 4)
                {
                    DisableTimeoutsWhenDebugging = true;
                }

                context.CurrentResult = context.CurrentTest.MakeTestResult();
                if (SuccessfullyRanOnce(i + 1, context, overallTimer, out var attemptResult, out lastException))
                {
                    context.CurrentResult.RecordTestCompletion();
                    return context.CurrentResult;
                }

                LogInfo(
                    $"{context.CurrentTest.Name}: Test attempt {$"{attemptResult}".ToKebabCase().Replace("-", " ")} ({i + 1} / {count})"
                );

                if (EnforceTimings && overallTimer.Elapsed.TotalMilliseconds > _overallTimeoutMilliSeconds)
                {
                    LogDebug(
                        $"overall timout ({_overallTimeoutMilliSeconds}ms exceeded (ran for {overallTimer.Elapsed.TotalMilliseconds})"
                    );
                    LogInfo(" ---- overall timeout exceeded - giving up ----");
                    break;
                }
            }

            context.CurrentResult.RecordException(
                lastException ?? new TimeoutException(
                    $"Unable to complete {_tryCount} test attempts for {context.CurrentTest.Name} within {TimeSpan.FromMilliseconds(_overallTimeoutMilliSeconds)}"
                )
            );

            return context.CurrentResult;
        }

        public static bool EnforceTimings =>
            !(Debugger.IsAttached && !DisableTimeoutsWhenDebugging);

        public static bool ShouldDebugInline =>
            Debugger.IsAttached && DebugInline;


        // /// <summary>
        // /// For test purposes:
        // /// normally, timings will be ignored when you debug a test so
        // /// as not to interfere with the debug session
        // /// </summary>
        // // ReSharper disable once MemberHidesStaticFromOuterClass
        // public static bool DisableTimeoutsWhenDebugging { get; set; } = true;
        //
        // /// <summary>
        // /// For test purposes: normally, when debugging, the test will
        // /// be run inline, instead of in a new process, so you can debug it
        // /// - however, _i_ need to debug the retry behavior...
        // /// </summary>
        // // ReSharper disable once MemberHidesStaticFromOuterClass
        // public static bool DebugInline { get; set; } = true;

        private static int _logIndex;

        [Conditional("ENABLE_DEBUG_LOGGING")]
        private void LogDebug(string str)
        {
            Console.Error.WriteLine($"({++_logIndex}{(_isChild ? "*" : "")}) [{nameof(RetryWithTimeout)}] :: {str}");
        }

        private void LogInfo(string str)
        {
            Console.Error.WriteLine($"[{DateTime.Now}] {str}");
        }

        private bool SuccessfullyRanOnce(
            int attempt,
            TestExecutionContext context,
            Stopwatch overallTimer,
            out TestAttemptResult attemptResult,
            out Exception exception
        )
        {
            using var testPasses = new ManualResetEventSlim();
            using var testFails = new ManualResetEventSlim();
            using var testProcessTimesOut = new ManualResetEventSlim();
            using var testTimesOut = new ManualResetEventSlim();
            using var logsCollected = new ManualResetEventSlim();
            using var testHasStarted = new Barrier(2);
            var logs = new ConcurrentQueue<string>();

            var myTimer = new Stopwatch();
            var cancellationTokenSource = new CancellationTokenSource();
            RunInSeparateProcess(
                attempt,
                context,
                testPasses,
                testFails,
                testProcessTimesOut,
                testTimesOut,
                logsCollected,
                testHasStarted,
                cancellationTokenSource.Token,
                logs
            );
            // ReSharper disable once MethodSupportsCancellation
            testHasStarted.SignalAndWait();
            myTimer.Start();
            overallTimer.Start();
            var maxWait = EnforceTimings
                ? MAX_WAIT_TIME_MS
                : DOTNET_TEST_PROCESS_MAX_OVERHEAD_MS + _overallTimeoutMilliSeconds;
            if (!WaitFor.AnyOf(maxWait, testTimesOut, testPasses, testFails))
            {
                cancellationTokenSource.Cancel();
                overallTimer.Stop();
                LogDebug($"Test timeout of {_testTimeoutMilliSeconds}ms exceeded (waited {maxWait}ms)");
                exception = new TimeoutException(
                    $"Test took more than {TimeSpan.FromMilliseconds(_testTimeoutMilliSeconds)} to run:\n{DumpLogs()}"
                );
                attemptResult = TestAttemptResult.TimedOut;
                return false;
            }

            overallTimer.Stop();
            myTimer.Stop();

            cancellationTokenSource.Cancel();
            if (testPasses.IsSet)
            {
                attemptResult = TestAttemptResult.Passed;
            }
            else if (testFails.IsSet)
            {
                attemptResult = TestAttemptResult.Failed;
            }
            else if (testTimesOut.IsSet)
            {
                attemptResult = TestAttemptResult.TimedOut;
            }
            else
            {
                attemptResult = TestAttemptResult.Inconclusive;
            }

            if (EnforceTimings && overallTimer.Elapsed.TotalMilliseconds > _overallTimeoutMilliSeconds)
            {
                LogInfo(
                    $"overall time: {overallTimer.Elapsed} vs overall timeout: {TimeSpan.FromMilliseconds(_overallTimeoutMilliSeconds)}"
                );
                LogDebug(
                    $"Overall time ({overallTimer.Elapsed.TotalMilliseconds}) exceeded overall timeout of {_overallTimeoutMilliSeconds}ms\n{DumpLogs()}"
                );
                exception = new TimeoutException(
                    $"Overall test execution time exceeded limit of {TimeSpan.FromMilliseconds(_overallTimeoutMilliSeconds)}\n{DumpLogs()}"
                );
                return false;
            }

            if (attemptResult == TestAttemptResult.TimedOut)
            {
                LogDebug(
                    $"test has exceeded runtime {_testTimeoutMilliSeconds}ms ({context.CurrentTest.Name})\n{DumpLogs()}"
                );
                exception = new TimeoutException(
                    $"test has exceeded runtime {_testTimeoutMilliSeconds}ms ({context.CurrentTest.Name})\n{DumpLogs()}"
                );
                return false;
            }

            exception = null;
            LogDebug($"state:\n{attemptResult}");
            LogDebug($"Test passes in time (attempt: {attempt})! ({myTimer.Elapsed})\n{DumpLogs()}");
            return true;

            string DumpLogs()
            {
                logsCollected.Wait();
                return string.Join("\n", logs);
            }
        }

        private void RunInSeparateProcess(
            int attempt,
            TestExecutionContext context,
            ManualResetEventSlim testPasses,
            ManualResetEventSlim testFails,
            ManualResetEventSlim testProcessTimesOut,
            ManualResetEventSlim testTimesOut,
            ManualResetEventSlim logsCollected,
            Barrier testHasStarted,
            CancellationToken cancellationToken,
            ConcurrentQueue<string> logs
        )
        {
            Task.Run(
                () =>
                {
                    var asm = context.CurrentTest.Method?.MethodInfo.DeclaringType?.Assembly;
                    if (asm is null)
                    {
                        throw new InvalidOperationException(
                            $"Can't find assembly info for test {context.CurrentTest.Name}"
                        );
                    }

                    logs.Enqueue($"Start test attempt {attempt} for {context.CurrentTest.Name} on {asm}");

                    var asmLocation = new Uri(asm.Location).LocalPath;
                    var timeoutMs = EnforceTimings
                        ? _testTimeoutMilliSeconds
                        : MAX_WAIT_TIME_MS;
                    var isolationMarker = GenerateIsolationMarker();
                    using var io = ProcessIO
                        .WithEnvironment(
                            new Dictionary<string, string>()
                            {
                                [ISOLATION_MARKER_ENVIRONMENT_VARIABLE] = isolationMarker,
                                [TEST_ATTEMPT_ENVIRONMENT_VARIABLE] = $"{attempt}"
                            }
                        ).Start(
                            "dotnet",
                            "test",
                            asmLocation,
                            "--filter",
                            Sanitize(context.CurrentTest.FullName)
                        );
                    var markerSeen = io.WaitForOutput(
                        StandardIo.StdOutOrStdErr,
                        s => s.Contains(isolationMarker),
                        DOTNET_TEST_PROCESS_MAX_OVERHEAD_MS
                    );
                    if (!markerSeen)
                    {
                        testProcessTimesOut.Set();
                        CollectLogs();
                        return;
                    }

                    io.StandardInput.WriteLine("");
                    testHasStarted.SignalAndWait();
                    Task.Run(
                        () =>
                        {
                            LogDebug($"---> will auto-fail this test in {timeoutMs}ms");
                            Thread.Sleep(timeoutMs);
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            testTimesOut.Set();
                        },
                        cancellationToken
                    );

                    var exitCode = io.WaitForExit(
                        timeoutMs
                    );

                    if (!io.HasExited)
                    {
                        io.Kill();
                    }

                    if (exitCode is null)
                    {
                        LogDebug("no exit code - test process did not complete");
                        SetIfNotCancelled(testProcessTimesOut);
                    }
                    else if (exitCode == 0)
                    {
                        LogDebug("exit code 0 - test passes!");
                        SetIfNotCancelled(testPasses);
                    }
                    else
                    {
                        LogDebug("test fails");
                        SetIfNotCancelled(testFails);
                    }

                    CollectLogs();

                    void CollectLogs()
                    {
                        foreach (var line in io.StandardOutputAndErrorInterleavedSnapshot)
                        {
                            logs.Enqueue(line);
                        }

                        logsCollected.Set();
                    }

                    void SetIfNotCancelled(ManualResetEventSlim ev)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            ev.Set();
                        }
                    }
                },
                cancellationToken
            );
        }


        private string Sanitize(string currentTestFullName)
        {
            return currentTestFullName.Replace("(", "\\(")
                .Replace(")", "\\");
        }
    }
}

/// <summary>
/// 
/// </summary>
public static class WaitFor
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="maxTimeMilliseconds"></param>
    /// <param name="events"></param>
    public static bool AnyOf(
        int maxTimeMilliseconds,
        params ManualResetEventSlim[] events
    )
    {
        using var mine = new ManualResetEventSlim();
        var waitResults = new List<bool>();
        var complete = false;
        var barrier = new Barrier(events.Length);
        var cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;
        var tasks = events.Select(
            ev => Task.Run(
                () =>
                {
                    barrier.SignalAndWait(token);
                    var wasSet = ev.Wait(maxTimeMilliseconds, token);
                    if (wasSet)
                    {
                        cancellationTokenSource.Cancel();
                    }

                    lock (waitResults)
                    {
                        if (!complete)
                        {
                            waitResults.Add(wasSet);
                        }
                    }
                }
            )
        ).ToArray();
        Task.WhenAny(tasks)
            .ContinueWith(CreateFinalAction());


        Action<Task> CreateFinalAction()
        {
            return _ =>
            {
                mine.Set();
            };
        }

        mine.Wait();
        lock (waitResults)
        {
            complete = true;
        }

        return waitResults.Any(o => o);
    }
}

internal class LabelAttribute : Attribute
{
    public string Label { get; }

    public LabelAttribute(string label)
    {
        Label = label;
    }
}

internal enum TestAttemptResult
{
    Passed,
    Failed,
    TimedOut,
    Inconclusive
}