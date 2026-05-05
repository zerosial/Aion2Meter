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

    /// Confirmed party member nicknames — bridges characterId (party protocol)
    /// with entityId (UserInfo/combat) since they use different ID spaces.
    private readonly HashSet<string> _partyNames = new(StringComparer.Ordinal);

    /// EntityId of the local player (set when UserInfo isSelf=1 arrives).
    public int? SelfEntityId { get; private set; }

    /// True when an actual party exists (at least one non-self member confirmed
    /// via a party protocol packet, not just seen nearby).
    public bool HasParty
    {
        get
        {
            foreach (var m in _members.Values)
                if (m.IsPartyMember && !m.IsSelf) return true;
            return false;
        }
    }

    /// Check if a given entityId belongs to a confirmed party member (or self).
    public bool IsInParty(uint entityId)
        => _members.TryGetValue(entityId, out var m) && (m.IsPartyMember || m.IsSelf);

    /// Check if a nickname belongs to a confirmed party member.
    public bool IsPartyName(string? name)
        => !string.IsNullOrEmpty(name) && _partyNames.Contains(name);

    public event Action? Changed;

    public void Upsert(PartyMember member)
    {
        if (member.IsSelf && member.CharacterId != 0)
            SelfEntityId = (int)member.CharacterId;

        // When a party protocol confirms a member, record their nickname
        // and retroactively mark any existing entry with the same name.
        if (member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname))
        {
            _partyNames.Add(member.Nickname);
            foreach (var m in _members.Values)
                if (m.Nickname == member.Nickname)
                    m.IsPartyMember = true;
        }

        // Bridge: if this member's nickname matches a confirmed party member, mark them.
        if (!member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname) && _partyNames.Contains(member.Nickname))
            member.IsPartyMember = true;

        // Preserve existing IsPartyMember flag when upserting identity-only data.
        if (!member.IsPartyMember && _members.TryGetValue(member.CharacterId, out var existing))
            member.IsPartyMember = existing.IsPartyMember;

        _members[member.CharacterId] = member;
        Changed?.Invoke();
    }

    public void Remove(uint characterId)
    {
        if (_members.Remove(characterId)) Changed?.Invoke();
    }

    /// Clear all party membership flags (called on party disband/leave).
    public void ClearPartyFlags()
    {
        _partyNames.Clear();
        foreach (var m in _members.Values)
            m.IsPartyMember = false;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_members.Count == 0) return;
        _partyNames.Clear();
        _members.Clear();
        Changed?.Invoke();
    }
}
