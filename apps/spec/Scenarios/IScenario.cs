using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public interface IScenario
{
    InputSet BuildInputs(Spec<YellowPagesState> spec);
    TestGenerationOptions Options { get; }
}
