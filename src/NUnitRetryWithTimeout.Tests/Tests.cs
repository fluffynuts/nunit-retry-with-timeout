using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NExpect;
using PeanutButter.Utils;
using static NExpect.Expectations;
using ConsumerTests = NUnitRetryWithTimeout.Consumer.Tests;

namespace NUnitRetryWithTimeout.Tests;

[TestFixture]
public class Tests
{
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
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
                s =>
                {
                    var trimmed = s.Trim();
                    return trimmed.Contains("passed", StringComparison.OrdinalIgnoreCase) &&
                        trimmed.Contains(
                            nameof(ConsumerTests.ShouldEventuallyPassWhenSometimesSlow),
                            StringComparison.OrdinalIgnoreCase
                        );
                }
            );
        // Assert
    }

    [Test]
    public void ShouldEventuallyPassWhenOneTestAttemptDoesNotThrow()
    {
        // Arrange
        // Act
                    
        // Assert
    }
    
    
}