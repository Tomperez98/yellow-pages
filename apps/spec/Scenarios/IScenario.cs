using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public interface IScenario
{
    TestSuite BuildTests(Spec<YellowPagesState> spec, YellowPagesState initialState);
}

public abstract record TestSuite
{
    private TestSuite() { }

    public sealed record Sequential(IList<SequentialTestCase> Cases) : TestSuite;

    public sealed record Concurrent(IList<ConcurrentTestCase> Cases) : TestSuite;
}
