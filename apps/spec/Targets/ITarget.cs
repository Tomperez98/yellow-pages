namespace Spec.Targets;

public interface ITarget
{
    Task AsyncReset();
    Task<string> AsyncSend<TRequest>(string name, TRequest request);
}
