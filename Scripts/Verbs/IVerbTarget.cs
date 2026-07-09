using System.Collections.Generic;

namespace Scavengineers.Scripts.Verbs;

public interface IVerbTarget
{
    IReadOnlyList<Verb> AvailableVerbs { get; }

    void ExecuteVerb(Verb verb);
}
