using System;
using System.Collections.Generic;

namespace A2Meter.Dps;

/// Authoritative roster of players currently in the party / nearby.
/// Keyed by characterId (the protocol's stable identity), not by EntityId
/// (which is per-zone and can shift when re-entering a map).
internal sealed class PartyTracker
{
    private readonly Dictionary<uint, PartyMember> _members = new();
    public IReadOnlyDictionary<uint, PartyMember> Members => _members;

    public event Action? Changed;

    public void Upsert(PartyMember member)
    {
        _members[member.CharacterId] = member;
        Changed?.Invoke();
    }

    public void Remove(uint characterId)
    {
        if (_members.Remove(characterId)) Changed?.Invoke();
    }

    public void Clear()
    {
        if (_members.Count == 0) return;
        _members.Clear();
        Changed?.Invoke();
    }
}
