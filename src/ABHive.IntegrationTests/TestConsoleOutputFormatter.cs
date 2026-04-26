using ABHive;
using ABHive.Application;
using System.Threading;

namespace ABHive.IntegrationTests;

/// <summary>
/// A test-friendly implementation of IConsoleOutputFormatter that auto-continues
/// instead of blocking on console input.
/// </summary>
public sealed class TestConsoleOutputFormatter : IConsoleOutputFormatter
{
    public void ShowStepProgress(int current, int total, Step step)
    {
        // No-op in tests
    }

    public string? ShowStepSuccess(StepExecutionResult result, Step step, CancellationToken ct = default)
    {
        // Return null to auto-continue (no user input needed)
        return null;
    }

    public void ShowStepFailure(StepExecutionResult result)
    {
        // No-op in tests
    }
}
