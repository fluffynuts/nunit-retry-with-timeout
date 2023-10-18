using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;
using ThreadState = System.Threading.ThreadState;

namespace NUnit;

/// <summary>
/// Decoration to apply to NUnit tests which allow
/// for retrying the test with a per-attempt timeout
/// and an optional overall timeout
/// </summary>
public class RetryWithTimeout : NUnitAttribute, IRepeatTest
{
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
        private readonly int _tryCount;
        private readonly int _testTimeoutMilliSeconds;
        private readonly int _overallTimeoutMilliSeconds;

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

        /// <summary>
        /// Runs the test, saving a TestResult in the supplied TestExecutionContext.
        /// </summary>
        /// <param name="context">The context in which the test should run.</param>
        /// <returns>A TestResult</returns>
        public override TestResult Execute(TestExecutionContext context)
        {
            var setOverallTimeout = false;
            var count = _tryCount;
            var overallStopwatch = new Stopwatch();
            Exception lastException = null;
            context.TestCaseTimeout = _overallTimeoutMilliSeconds;

            while (count-- > 0)
            {
                var thisTimeout = (int) Math.Min(
                    _testTimeoutMilliSeconds,
                    _overallTimeoutMilliSeconds - overallStopwatch.Elapsed.TotalMilliseconds
                );
                overallStopwatch.Start();
                var ex = Run.UntilAnyCompletes(
                    () =>
                    {
                        if (CheckIfTimedOutOverall())
                        {
                            return;
                        }

                        context.CurrentResult = innerCommand.Execute(context);
                    },
                    () =>
                    {
                        Thread.Sleep(thisTimeout);
                        throw new TimeoutException(
                            $"{context.CurrentTest.Name} exceeded execution time of {TimeSpan.FromMilliseconds(_testTimeoutMilliSeconds)}"
                        );
                    }
                );
                overallStopwatch.Stop();
                lastException = ex;
                if (CheckIfTimedOutOverall())
                {
                    break;
                }

                // Clear result for retry
                if (count > 0)
                {
                    context.CurrentResult ??= context.CurrentTest.MakeTestResult();
                    context.CurrentRepeatCount++; // increment Retry count for next iteration. will only happen if we are guaranteed another iteration
                }
            }

            Log($"Total test time: {overallStopwatch.Elapsed}");
            CheckIfTimedOutOverall();
            Log("after overall timeout check");
            if (lastException is not null && TestHasNotFailed())
            {
                Log("recording last exception");
                context.CurrentResult.RecordException(lastException);
            }

            Log("-> leaving RetryWithTimeout logic");
            return context.CurrentResult;

            bool TestHasNotFailed()
            {
                return context.CurrentResult?.ResultState?.Status != ResultState.Failure.Status;
            }

            bool CheckIfTimedOutOverall()
            {
                if (setOverallTimeout)
                {
                    return true;
                }

                if (overallStopwatch.Elapsed.TotalMilliseconds >= _overallTimeoutMilliSeconds)
                {
                    Log("overall timeout exceeded");
                    context.CurrentResult.RecordException(
                        new TimeoutException(
                            $"{context.CurrentTest.Name} exceeded overall max execution time of {TimeSpan.FromMilliseconds(_overallTimeoutMilliSeconds)} after {_tryCount - count} attempts"
                        )
                    );
                    setOverallTimeout = true;
                    Log("-> done setting overall timeout failure");
                    return true;
                }

                Log("-> test did not time out overall");
                return false;
            }

            void Log(string str)
            {
                Console.Error.WriteLine(
                    $"[attr: {context.CurrentTest.Name}] ({_tryCount - count} / {_tryCount}) :: {str}"
                );
            }
        }
    }

    internal static class Run
    {
        public static Exception UntilAnyCompletes(
            params Action[] actions
        )
        {
            if (actions.Length < 2)
            {
                if (actions.Any())
                {
                    try
                    {
                        actions[0].Invoke();
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                }

                return null;
            }

            var lck = new object();
            var ev = new ManualResetEventSlim();
            var result = null as Exception;
            var threads = new List<Thread>();
            var barrier = new Barrier(actions.Length + 1);
            foreach (var action in actions)
            {
                threads.Add(
                    new Thread(
                        () =>
                        {
                            barrier.SignalAndWait();
                            try
                            {
                                action();
                                ev.Set();
                            }
                            catch (Exception ex)
                            {
                                lock (lck)
                                {
                                    if (result is null)
                                    {
                                        result = ex;
                                    }

                                    ev.Set();
                                }
                            }
                        }
                    ).Started()
                );
            }
            barrier.SignalAndWait();
            ev.Wait();
            foreach (var t in threads)
            {
                t.Join();
            }

            return result;
        }
    }
}

internal static class ThreadExtensions
{
    internal static Thread Started(
        this Thread thread
    )
    {
        if (thread.ThreadState == ThreadState.Unstarted)
        {
            thread.Start();
        }
        return thread;
    }
}