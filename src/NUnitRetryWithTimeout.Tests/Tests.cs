using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NExpect;
using NUnit;
using PeanutButter.Utils;
using static NExpect.Expectations;
using ConsumerTests = NUnitRetryWithTimeout.Consumer.Tests;
using static PeanutButter.RandomGenerators.RandomValueGen;

namespace NUnitRetryWithTimeout.Tests;

[TestFixture]
public class Tests
{
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        Console.Error.WriteLine("Running consumer tests & recording output, please wait");
        var consumer = FindConsumerProject();
        using var io = ProcessIO.Start(
            "dotnet",
            "test",
            consumer,
            "--verbosity",
            "normal"
        );
        StdErr = io.StandardError.ToArray();
        StdOut = io.StandardOutput.ToArray();
    }

    private string[] StdErr = Array.Empty<string>();
    private string[] StdOut = Array.Empty<string>();

    private string FindConsumerProject()
    {
        var myAsmLocation =
            new Uri(
                typeof(Tests).Assembly.Location
            ).LocalPath;
        var current = myAsmLocation;
        var project = "NUnitRetryWithTimeout.Consumer";
        while ((current = Path.GetDirectoryName(current)) is not null)
        {
            var seek = Path.Combine(current, project, $"{project}.csproj");
            if (File.Exists(seek))
            {
                return seek;
            }
        }

        throw new Exception(
            $"Unable to find {project}/{project}.csproj, starting at {Path.GetDirectoryName(myAsmLocation)} and walking upwards"
        );
    }

    [Test]
    public void ShouldEventuallyPassWhenOneTestAttemptDoesNotExceedTestTimeout()
    {
        // Arrange
        // Act
        Expect(StdOut)
            .To.Contain.Exactly(1)
            .Matched.By(
                s => s.ContainsInOrder(
                    StringComparison.OrdinalIgnoreCase,
                    "passed",
                    "FIXME" //nameof(ConsumerTests.ShouldEventuallyPassWhenSometimesSlow)
                )
            );
        // Assert
    }

    [Test]
    public void ShouldEventuallyPassWhenOneTestAttemptDoesNotThrow()
    {
        // Arrange
        // Act
        Expect(StdOut)
            .To.Contain.Exactly(1)
            .Matched.By(
                s => s.ContainsInOrder(
                    StringComparison.OrdinalIgnoreCase,
                    "passed",
                    "FIXME" //nameof(ConsumerTests.ShouldEventuallyPassWhenSometimesThrows)
                )
            );
        // Assert
    }

    [Test]
    public void ShouldFailWhenRetriesExceededAfterIndividualTimeouts()
    {
        // Arrange
        // Act
        Expect(StdOut)
            .To.Contain.Exactly(1)
            .Matched.By(
                s => s.ContainsInOrder(
                    StringComparison.OrdinalIgnoreCase,
                    "failed",
                    nameof(ConsumerTests.ShouldEventuallyFailDueToTestTimeout)
                )
            );
        // Assert
    }

    [Test]
    public void ShouldFailWhenOverallTimeoutExceeded()
    {
        // Arrange
        // Act
        Expect(StdOut)
            .To.Contain.Exactly(1)
            .Matched.By(
                s => s.ContainsInOrder(
                    StringComparison.OrdinalIgnoreCase,
                    "failed",
                    nameof(ConsumerTests.ShouldEventuallyFailDueToOverallTimeout)
                )
            );
        // Assert
    }

    [TestFixture]
    public class WaitForAnyEvent
    {
        [Test]
        public void ShouldWaitForFirstOfTwo()
        {
            // Arrange
            var ev1 = new ManualResetEventSlim();
            var ev2 = new ManualResetEventSlim();

            // Act
            Task.Run(
                () =>
                {
                    Thread.Sleep(10000);
                }
            );
            Task.Run(
                () =>
                {
                    Thread.Sleep(500);
                    ev2.Set();
                }
            );
            // Assert
            WaitFor.AnyOf(10000, ev1, ev2);
            Expect(ev1.IsSet)
                .To.Be.False();
            Expect(ev2.IsSet)
                .To.Be.True();
        }

        [Test]
        public void ShouldWaitForFirstOfN()
        {
            // Arrange
            var howMany = 10;
            var events = PyLike.Range(1, howMany)
                .Select(_ => new ManualResetEventSlim())
                .ToArray();

            // Act
            PyLike.Range(1, howMany)
                .ForEach(
                    i =>
                    {
                        Task.Run(
                            () =>
                            {
                                Thread.Sleep(
                                    GetRandomInt(1000, 5000)
                                );
                                events[i].Set();
                            }
                        );
                    }
                );
            // Assert
            var result = WaitFor.AnyOf(10000, events);
            Expect(result)
                .To.Be.True();
            Expect(events)
                .To.Contain.Exactly(1)
                .Matched.By(o => o.IsSet);
        }
    }
}