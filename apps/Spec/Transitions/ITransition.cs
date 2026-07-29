using Microsoft.Accordant;
using Spec;
using Spec.Model;

namespace Spec.Transitions;

/// <summary>
/// A hand-written, sequential conformance test. Implementations call the
/// <see cref="ApiClient"/> directly and validate each response with
/// <c>spec.Allows</c>, using <c>Invariant.Assert</c> on failure.
///
/// Pattern from the Accordant docs:
/// <code>
// var response = await client.CreateTimerAsync(request);
///   var (isValid, message, stateProfile) =
///       spec.Allows(createOp, request, response, stateProfile);
///   Invariant.Assert(isValid, message);
/// </code>
/// </summary>
public interface ITransition
{
    Task RunAsync(Spec<TimerState> spec, ApiClient client, TimerState initialState);
}
