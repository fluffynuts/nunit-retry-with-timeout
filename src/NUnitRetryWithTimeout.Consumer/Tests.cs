using System;
using System.Threading;
using NUnit;
using NUnit.Framework;

namespace NUnitRetryWithTimeout.Consumer;

public class Tests
{
    [RetryWithTimeout(5, 500)]
    [Test]
    public void ShouldEventuallyPassWhenSometimesSlow()
    {
        // Arrange
        Console.Error.WriteLine($"Eventually Passing Slow Attempt: {++_eventuallyPassSometimesSlowAttempt}");
        // Act
        if (_eventuallyPassSometimesSlowAttempt < 5)
        {
            Thread.Sleep(1000);
        }

        // Assert
        Assert.Pass("yay");
    }

    [RetryWithTimeout(5, 500)]
    [Test]
    public void ShouldEventuallyPassWhenSometimesThrows()
    {
        // Arrange
        Console.Error.WriteLine($"Eventually Passing Throwing Attempt: {++_eventuallyPassSometimesThrowsAttempt}");
        // Act
        if (_eventuallyPassSometimesThrowsAttempt < 5)
        {
            Thread.Sleep(100);
            throw new Exception("nope");
        }

        // Assert
        Assert.Pass("yay");
    }

    [RetryWithTimeout(5, 500)]
    [Test]
    public void ShouldEventuallyFailDueToTestTimeout()
    {
        // Arrange
        Console.Error.WriteLine($"Test Timeout Attempt: {++_failDueToIndividualTimeoutAttempt}");
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
        Console.Error.WriteLine($"Overall Timeout Attempt: {++_failDueToOverallTimeoutAttempt}");
        // Act
        if (_failDueToOverallTimeoutAttempt < 5)
        {
            Thread.Sleep(1000);
        }
        else
        {
            Thread.Sleep(400); // should not time out by itself, but overall test timeout should kick in
        }

        // Assert
        Assert.Pass("yay");
    }

    private int _failDueToOverallTimeoutAttempt;
    private int _failDueToIndividualTimeoutAttempt;
    private int _eventuallyPassSometimesSlowAttempt;
    private int _eventuallyPassSometimesThrowsAttempt;
}