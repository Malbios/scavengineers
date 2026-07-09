using System.Collections.Generic;

namespace Scavengineers.Scripts.Verbs;

public interface IVerbTarget
{
    IReadOnlyList<Verb> AvailableVerbs { get; }

    /// <summary>Null when no verb is currently in progress on this target, else 0..1.</summary>
    float? CurrentVerbProgress { get; }

    void ExecuteVerb(Verb verb);

    /// <summary>Cancels whatever verb is currently in progress on this target, if any,
    /// reverting to its idle state without applying the verb's effect.</summary>
    void CancelVerb();
}
