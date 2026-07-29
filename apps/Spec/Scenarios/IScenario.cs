using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public interface IScenario
{
    TestSuite BuildTests(Spec<TimerState> spec, TimerState initialState);
}

public abstract record TestSuite
{
    private TestSuite() { }

    public sealed record Sequential(IList<SequentialTestCase> Cases) : TestSuite;

    public sealed record Concurrent(IList<ConcurrentTestCase> Cases) : TestSuite;
}
