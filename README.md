# nunit-retry-with-timeout
Provides a test decorator, `[RetryWithTimeout(...)]` which allows retrying tests with per-test 
and overall timeouts, working around a common problem with `[Retry(...)]` and `[Timeout(...)]`
being on the same test.

examples:

The most common one: you'd like to retry a test if it fails, and only allow
a certain amount of time per test attempt. Currently, with NUnit, if the `[Timeout]`
attribute causes a test to fail due to timeout, `[Retry]` is essentially ignored.

Also, NUnit's [Retry] only retries on AssertionExceptions - not if some other
exception is thrown, which is particularly annoying if you're using an assertions
framework other than NUnit's, because you have to wrap the entire test in
```csharp
Assert.That(() =>
    {
    ... // original test code
    }, Throws.Nothing
);
```

Instead, try:

```csharp
/*
* Retries the test up to 3 times, after _any_ exception, not just NUnit exceptions,
*  with a per-test timeout of 5 seconds.
*/
[RetryWithTimeout(3, 5000)]
[Test]
public void SomeFlakyTest()
{
}
```

There's also an edge case for when you'd like to allow up to a certain amount of time
per test, but less time than `retries` x `timeout` for the overall test, eg a test
which passes most of the time, but _sometimes_ hangs, perhaps due to network issues:

```csharp
/*
* Retries the test up to 3 times, with a per-test
*  timeout of 2 seconds and an overall timeout of 
*  5 seconds - so if the first two attempts fail,
*  the last one will essentially have only 1s to run
*/
[RetryWithTimeout(3, 2000, 5000)]
[Test]
public void AnotherFlakyTest()
{
}

```
