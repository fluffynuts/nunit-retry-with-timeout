using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit;
using NUnit.Framework;
using PeanutButter.Utils;

namespace NUnitRetryWithTimeout.Consumer;

public class Tests
{
    [SetUp]
    public void Setup()
    {
        ResetCounters();
    }

    public void ResetCounters()
    {
        var initValue = NumericEnvVar("__TEST_ATTEMPT__");
        _failDueToOverallTimeoutAttempt = initValue;
        _failDueToIndividualTimeoutAttempt = initValue;
        _eventuallyPassSometimesSlowAttempt = initValue;
        _eventuallyPassSometimesThrowsAttempt = initValue;
    }

    private int NumericEnvVar(
        string varname
    )
    {
        var value = Environment.GetEnvironmentVariable(varname);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }
        Log($"__TEST_ATTEMPT__ var set: {value}");

        if (!int.TryParse(value, out var result))
        {
            throw new InvalidOperationException(
                $"Expected env var '{varname}' (value: '{value}') to be an integer value"
            );
        }

        return result;
    }

    [RetryWithTimeout(5, 500)]
    [Test]
    public void ShouldEventuallyPassWhenSometimesSlow()
    {
        // RetryWithTimeout.EnableDebuggerBehavior = false;
        _eventuallyPassSometimesSlowAttempt = 0;
        // Arrange
        using var _ = new AutoLocker(_testLock);
        Log($"{++_eventuallyPassSometimesSlowAttempt}");
        // Act
        if (_eventuallyPassSometimesSlowAttempt < 5)
        {
            Console.Error.WriteLine("Sleeping for a second...");
            Thread.Sleep(1000);
        }
        else
        {
            Console.Error.WriteLine("No sleep - should pass!");
        }

        // Assert
        Assert.Pass("yay");
    }


    [RetryWithTimeout(5, 500)]
    [Test]
    public void ShouldEventuallyPassWhenSometimesThrows()
    {
        // Arrange
        using var _ = new AutoLocker(_testLock);
        Log($"{++_eventuallyPassSometimesThrowsAttempt}");
        // Act
        if (_eventuallyPassSometimesThrowsAttempt < 5)
        {
            Thread.Sleep(100);
            throw new Exception("nope");
        }

        // Assert
        Assert.Pass("yay");
    }

    public class SomeTestData
    {
        public int I { get; }

        public SomeTestData(int i)
        {
            I = i;
        }

        // public override string ToString()
        // {
        //     return $"{I}";
        // }
    }

    public static IEnumerable<SomeTestData> TestCaseSource()
    {
        yield return new SomeTestData(1);
    }

    [RetryWithTimeout(5, 500)]
    [Test]
    public void ShouldEventuallyFailDueToTestTimeout()
    {
        // Arrange
        using var _ = new AutoLocker(_testLock);
        Log($"{++_failDueToIndividualTimeoutAttempt}");
        // Act
        Thread.Sleep(1000);

        // Assert
        Assert.Pass("yay");
    }

    [RetryWithTimeout(5, 500, 2100)]
    [Test]
    public void ShouldEventuallyFailDueToOverallTimeout()
    {
        // Arrange
        using var _ = new AutoLocker(_testLock);
        Log($"{++_failDueToOverallTimeoutAttempt}");
        // Act
        if (_failDueToOverallTimeoutAttempt < 5)
        {
            Log("long sleep - should time out");
            Thread.Sleep(1000);
        }
        else
        {
            Log("shorter sleep - should time out overall");
            Thread.Sleep(400); // should not time out by itself, but overall test timeout should kick in
        }

        // Assert
        Log($"{nameof(ShouldEventuallyFailDueToOverallTimeout)} should pass");
        Assert.Pass("yay");
    }

    private static SemaphoreSlim _testLock = new(1, 1);

    private int _failDueToOverallTimeoutAttempt;
    private int _failDueToIndividualTimeoutAttempt;
    private int _eventuallyPassSometimesSlowAttempt;
    private int _eventuallyPassSometimesThrowsAttempt;

    void Log(string str, [CallerMemberName] string caller = null)
    {
        Console.Error.WriteLine($"[test: {caller}] :: {str}");
    }

    [Test]
    [Timeout(1000)]
    [Explicit]
    public void HowDoesTimeoutFail()
    {
        // Arrange
        using var _ = new Disposable();
        // Act
        Console.Error.WriteLine("- before sleep -");
        Thread.Sleep(1500);
        Console.Error.WriteLine("- so refreshed!");
        // Assert
    }

    private class Disposable : IDisposable
    {
        public void Dispose()
        {
            Console.Error.WriteLine("-- disposed --");
        }
    }
}