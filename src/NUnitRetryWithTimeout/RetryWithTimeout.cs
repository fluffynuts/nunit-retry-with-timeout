using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace NUnit;

public class RetryWithTimeout : NUnitAttribute, IRepeatTest
{
    public int Retries { get; }
    public int TestTimeoutMilliseconds { get; }
    public int OverallTimeoutMilliseconds { get; }

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
            var count = _tryCount;
            var overallStopwatch = new Stopwatch();

            while (count-- > 0)
            {
                // try
                // {
                //     context.CurrentResult = innerCommand.Execute(context);
                // }
                // // Commands are supposed to catch exceptions, but some don't
                // // and we want to look at restructuring the API in the future.
                // catch (Exception ex)
                // {
                //     context.CurrentResult ??= context.CurrentTest.MakeTestResult();
                //     context.CurrentResult.RecordException(ex);
                // }
                // finally
                // {
                // }

                overallStopwatch.Start();
                var ex = Run.UntilAnyCompletes(
                    () => context.CurrentResult = innerCommand.Execute(context),
                    () =>
                    {
                        Thread.Sleep(_testTimeoutMilliSeconds);
                        throw new TimeoutException(
                            $"{context.CurrentTest.Name} exceeded execution time of {TimeSpan.FromMilliseconds(_testTimeoutMilliSeconds)}"
                        );
                    }
                );
                overallStopwatch.Stop();
                if (ex is not null)
                {
                    context.CurrentResult.RecordException(ex);
                }

                if (context.CurrentResult.ResultState.Status != ResultState.Failure.Status)
                {
                    break;
                }

                if (overallStopwatch.Elapsed.TotalMilliseconds >= _overallTimeoutMilliSeconds)
                {
                    context.CurrentResult.RecordException(
                        new TimeoutException(
                            $"{context.CurrentTest.Name} exceeded overall max execution time of {TimeSpan.FromMilliseconds(_overallTimeoutMilliSeconds)} after {_tryCount - count} attempts"
                        )
                    );
                    break;
                }

                // Clear result for retry
                if (count > 0)
                {
                    context.CurrentResult = context.CurrentTest.MakeTestResult();
                    context.CurrentRepeatCount++; // increment Retry count for next iteration. will only happen if we are guaranteed another iteration
                }
            }

            return context.CurrentResult;
        }
    }

    internal static class Run
    {
        public static Exception UntilAnyCompletes(
            params Action[] actions
        )
        {
            var lck = new object();
            var ev = new ManualResetEventSlim();
            var result = null as Exception;
            var cancellationTokenSource = new CancellationTokenSource();
            foreach (var action in actions)
            {
                Task.Run(
                    () =>
                    {
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
                    },
                    cancellationTokenSource.Token
                );
            }

            ev.Wait();
            cancellationTokenSource.Cancel();
            return result;
        }
    }
}