using System.Collections.Generic;
using UnityEngine;

// Groups are local to the client. A group only describes who fits in a view.
internal sealed class ArenaCameraClusters
{
    internal readonly struct Fighter
    {
        internal readonly ulong Id;
        internal readonly Bounds Bounds;
        internal readonly int Team;

        internal Fighter(ulong id, Bounds bounds, int team)
        {
            Id = id;
            Bounds = bounds;
            Team = team;
        }
    }

    internal sealed class Group
    {
        internal readonly List<ulong> Members = new(8);
        internal float ChangeAt;
    }

    private readonly List<Group> groups = new(8);
    private readonly List<Group> splitGroups = new(8);
    private readonly List<Group> changed = new(8);
    private readonly Dictionary<ulong, Group> sharedViews = new(8);
    private readonly Dictionary<ulong, Group> companionViews = new(8);
    private readonly List<ulong> spectatorMembers = new(8);
    internal int FighterCount { get; private set; }

    internal void Reset()
    {
        groups.Clear();
        FighterCount = 0;
        sharedViews.Clear();
        companionViews.Clear();
        spectatorMembers.Clear();
    }

    internal void Update(List<Fighter> fighters, float now, float persistence, float mergeDistance,
        float splitDistance, float mergeSize, float splitSize, float aspect, Vector2 padding)
    {
        FighterCount = fighters.Count;
        // Keep surviving memberships when fighters are removed or added.
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            for (int j = group.Members.Count - 1; j >= 0; j--)
                if (Find(fighters, group.Members[j]) < 0)
                {
                    sharedViews.Remove(group.Members[j]);
                    group.Members.RemoveAt(j);
                }
            if (group.Members.Count == 0) groups.RemoveAt(i);
        }
        foreach (var fighter in fighters)
            if (FindCore(fighter.Id) == null) groups.Add(Singleton(fighter.Id, now));

        if (fighters.Count == 2)
        {
            if (groups.Count > 1)
            {
                groups[0].Members.AddRange(groups[1].Members);
                groups[0].ChangeAt = now + persistence;
                groups.RemoveAt(1);
            }
            sharedViews.Clear();
            companionViews.Clear();
            return;
        }

        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (group.Members.Count < 2 || now < group.ChangeAt ||
                (Fits(GetBounds(group, fighters), splitSize, aspect, padding) &&
                 Connected(group, fighters, splitDistance))) continue;

            // Rebuild only the group that spread out. Nearby fighters can stay together.
            splitGroups.Clear();
            foreach (ulong member in group.Members) splitGroups.Add(Singleton(member, now));
            Merge(splitGroups, fighters, now, 0, mergeDistance, mergeSize, aspect, padding);
            foreach (var part in splitGroups) part.ChangeAt = now + persistence;
            groups.RemoveAt(i);
            groups.AddRange(splitGroups);
        }

        Merge(groups, fighters, now, persistence, mergeDistance, mergeSize, aspect, padding);
        AssignSharedViews(fighters, mergeDistance, now, persistence);
    }

    internal Group FindGroup(ulong id) =>
        companionViews.TryGetValue(id, out var view) ? view : AssignedView(id);

    private Group AssignedView(ulong id) =>
        sharedViews.TryGetValue(id, out var view) ? view : FindCore(id);

    private Group FindCore(ulong id)
    {
        foreach (var group in groups)
            if (group.Members.Contains(id)) return group;
        return null;
    }

    private void AssignSharedViews(List<Fighter> fighters, float mergeDistance, float now, float persistence)
    {
        // Outliers share a view with a nearby group without pulling its regular members out of their own view.
        foreach (var fighter in fighters)
        {
            var core = FindCore(fighter.Id);
            if (core.Members.Count > 1 || fighters.Count < 2)
            {
                sharedViews.Remove(fighter.Id);
                continue;
            }

            Group nearest = null;
            ulong nearestId = ulong.MaxValue;
            float distance = float.PositiveInfinity;
            sharedViews.TryGetValue(fighter.Id, out var view);
            ulong? prior = view != null && view.Members.Count > 1 ? view.Members[1] : null;
            foreach (var other in fighters)
            {
                if (other.Id == fighter.Id) continue;
                float gap = Mathf.Sqrt(DistanceSquared(fighter.Id, other.Id, fighters));
                if (prior == other.Id)
                    gap = now < view.ChangeAt ? -1f : gap - mergeDistance * .25f;
                if (gap < distance || (gap == distance && other.Id < nearestId))
                {
                    nearestId = other.Id;
                    nearest = FindCore(other.Id);
                    distance = gap;
                }
            }

            if (view == null || prior != nearestId)
                sharedViews[fighter.Id] = view = new Group { ChangeAt = now + persistence };
            view.Members.Clear();
            view.Members.Add(fighter.Id);
            view.Members.Add(nearestId);
            foreach (ulong member in nearest.Members)
                if (member != nearestId) view.Members.Add(member);
        }

        // At least one companion also sees the outlier. Other engagements keep their usual views.
        companionViews.Clear();
        foreach (var fighter in fighters)
        {
            if (!sharedViews.TryGetValue(fighter.Id, out var view)) continue;
            ulong companion = view.Members[1];
            if (!companionViews.TryGetValue(companion, out var reciprocal))
            {
                reciprocal = new Group();
                reciprocal.Members.AddRange(AssignedView(companion).Members);
                companionViews.Add(companion, reciprocal);
            }
            if (!reciprocal.Members.Contains(fighter.Id)) reciprocal.Members.Add(fighter.Id);
        }
    }

    internal Group SpectatorGroup()
    {
        Group best = null;
        bool foundSurvivors = false;
        foreach (var group in groups)
        {
            bool related = group.Members.Exists(spectatorMembers.Contains);
            if (foundSurvivors && !related) continue;
            if (best == null || (related && !foundSurvivors) || group.Members.Count > best.Members.Count ||
                (group.Members.Count == best.Members.Count && LowestId(group) < LowestId(best)))
            {
                best = group;
                foundSurvivors = related;
            }
        }

        if (best == null) return null;
        spectatorMembers.Clear();
        spectatorMembers.AddRange(best.Members);
        return best.Members.Count == 1 ? FindGroup(best.Members[0]) : best;
    }

    private static ulong LowestId(Group group)
    {
        ulong lowest = ulong.MaxValue;
        foreach (ulong id in group.Members)
            if (id < lowest) lowest = id;
        return lowest;
    }

    internal static int Find(List<Fighter> fighters, ulong id)
    {
        for (int i = 0; i < fighters.Count; i++)
            if (fighters[i].Id == id) return i;
        return -1;
    }

    internal static Bounds GetBounds(Group group, List<Fighter> fighters)
    {
        Bounds bounds = fighters[Find(fighters, group.Members[0])].Bounds;
        for (int i = 1; i < group.Members.Count; i++)
            bounds.Encapsulate(fighters[Find(fighters, group.Members[i])].Bounds);
        return bounds;
    }

    internal static float SizeFor(Bounds bounds, float aspect, Vector2 padding) =>
        Mathf.Max((bounds.extents.x + padding.x) / aspect, bounds.extents.y + padding.y);

    private static bool Fits(Bounds bounds, float size, float aspect, Vector2 padding) =>
        SizeFor(bounds, aspect, padding) <= size;

    private static Group Singleton(ulong id, float changeAt)
    {
        var group = new Group { ChangeAt = changeAt };
        group.Members.Add(id);
        return group;
    }

    private void Merge(List<Group> candidates, List<Fighter> fighters, float now, float persistence,
        float distance, float size, float aspect, Vector2 padding)
    {
        // Prefer the closest engagement that still fits. Finish the whole merge pass before locking it.
        changed.Clear();
        while (true)
        {
            int first = -1, second = -1;
            float best = distance * distance;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (now < candidates[i].ChangeAt) continue;
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (now < candidates[j].ChangeAt) continue;
                    float gap = GapSquared(candidates[i], candidates[j], fighters);
                    if (gap > best || (gap == best && first >= 0)) continue;
                    Bounds combined = GetBounds(candidates[i], fighters);
                    combined.Encapsulate(GetBounds(candidates[j], fighters));
                    if (!Fits(combined, size, aspect, padding)) continue;
                    first = i;
                    second = j;
                    best = gap;
                }
            }

            if (first < 0) break;
            var merged = candidates[first];
            merged.Members.AddRange(candidates[second].Members);
            candidates.RemoveAt(second);
            if (!changed.Contains(merged)) changed.Add(merged);
        }
        foreach (var group in changed) group.ChangeAt = now + persistence;
    }

    private static float GapSquared(Group a, Group b, List<Fighter> fighters)
    {
        float gap = float.PositiveInfinity;
        foreach (ulong member in a.Members)
            foreach (ulong other in b.Members)
                gap = Mathf.Min(gap, DistanceSquared(member, other, fighters));
        return gap;
    }

    private static float DistanceSquared(ulong a, ulong b, List<Fighter> fighters) =>
        ((Vector2)(fighters[Find(fighters, a)].Bounds.center - fighters[Find(fighters, b)].Bounds.center)).sqrMagnitude;

    private static bool Connected(Group group, List<Fighter> fighters, float distance)
    {
        int visited = 1;
        int all = (1 << group.Members.Count) - 1;
        float limit = distance * distance;
        while (visited != all)
        {
            int previous = visited;
            for (int i = 0; i < group.Members.Count; i++)
            {
                if ((visited & (1 << i)) == 0) continue;
                for (int j = 0; j < group.Members.Count; j++)
                    if ((visited & (1 << j)) == 0 && DistanceSquared(group.Members[i], group.Members[j], fighters) <= limit)
                        visited |= 1 << j;
            }
            if (visited == previous) return false;
        }
        return true;
    }
}
