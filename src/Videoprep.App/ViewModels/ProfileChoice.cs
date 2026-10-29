using Videoprep.Core.Profiles;

namespace Videoprep.App.ViewModels;

/// <summary>A profile in a combo box. <see cref="Profile"/> null = "(sin cambios)" / "(valores propios)".</summary>
public sealed record ProfileChoice(FormatProfile? Profile, string Label)
{
    public override string ToString() => Label;
}
