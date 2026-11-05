namespace Miniprep.App.Services;

/// <summary>
/// Shared flags of the two tabs: only one download at a time (Prep's or the queue's), and the background completion of the queue's data
/// waits while Prep is reading a link (the person is waiting for that one).
/// </summary>
public sealed class RunGate
{
    public bool Busy { get; set; }
    /// <summary>Prep is reading the data of the link in the box.</summary>
    public bool InfoBusy { get; set; }
}
