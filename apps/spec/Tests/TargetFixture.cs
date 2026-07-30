using Spec.Model;
using Spec.Targets;
using Xunit;

namespace Spec.Tests;

/// <summary>
/// Creates every available <see cref="ITarget"/> so a single
/// <c>dotnet test</c> run covers all backends. Targets are
/// addressed by name from <see cref="TargetNames.All"/>.
///
/// <list type="bullet">
/// <item><c>"inmemory"</c> — always available, thread-safe</item>
/// <item><c>"http"</c> — enabled when <c>TIMER_URL</c> is set</item>
/// <item><c>"stdio"</c> — enabled when <c>TIMER_STDIO_PATH</c> is set</item>
/// </list>
///
/// Usage:
///   dotnet test
///   TIMER_URL=http://localhost:3000 dotnet test
///   TIMER_STDIO_PATH=../../../../stdio/stdio dotnet test
/// </summary>
public class TargetFixture : IAsyncLifetime
{
    /// <summary>All available targets keyed by name.</summary>
    public IReadOnlyDictionary<string, ApiClient> Clients { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var clients = new Dictionary<string, ApiClient>();

        foreach (var name in TargetNames.Available)
        {
            switch (name)
            {
                case "inmemory":
                    var server = new InMemoryServer(new TimerState(), threadSafe: true);
                    clients[name] = new ApiClient(new InMemoryTarget(server));
                    break;
                case "http":
                    var url = Environment.GetEnvironmentVariable("TIMER_URL")!;
                    clients[name] = new ApiClient(new HttpTarget(url));
                    break;
                case "stdio":
                    var path = Environment.GetEnvironmentVariable("TIMER_STDIO_PATH")!;
                    clients[name] = new ApiClient(new StdioTarget(path));
                    break;
            }
        }

        Clients = clients;

        foreach (var client in Clients.Values)
            await client.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Provides the target-name list for <c>[MemberData]</c>.
/// Single place that decides which targets are active based on
/// environment variables. Both xUnit discovery and
/// <see cref="TargetFixture"/> read from the same list.
/// </summary>
public static class TargetNames
{
    /// <summary>The set of target names currently enabled.</summary>
    public static IReadOnlyList<string> Available { get; }

    /// <summary>
    /// Returns every target name for <c>[MemberData]</c>.
    /// </summary>
    public static TheoryData<string> All()
    {
        var data = new TheoryData<string>();
        foreach (var name in Available)
            data.Add(name);
        return data;
    }

    static TargetNames()
    {
        var names = new List<string> { "inmemory" };

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIMER_URL")))
            names.Add("http");

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIMER_STDIO_PATH")))
            names.Add("stdio");

        Available = names;
    }
}
