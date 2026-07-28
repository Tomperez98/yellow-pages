using Microsoft.Accordant;

namespace Spec.Model;

[State]
public partial class Country : State
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
}

[State]
public partial class YellowPagesState : State
{
    public List<Country> Countries { get; set; } = [];
}
