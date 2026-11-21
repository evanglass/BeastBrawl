public enum MatchGameMode { FreeForAll, TeamFight }

public static class MatchRules
{
    public const int Human = 0;
    public const int RandomBot = -1;
    public const int NoTeam = 0;
    public const int MaxSlots = 8;

    public static int TeamForSlot(MatchGameMode mode, int teamSize, int slot) =>
        mode == MatchGameMode.TeamFight ? slot / teamSize + 1 : NoTeam;

    public static int LastCharacterForTeam(int team) =>
        (int)(team == 2 ? PlayerVisual.Character.Mamba : PlayerVisual.Character.Snake);

    public static bool CharacterAllowed(int character, int team) =>
        character >= (int)PlayerVisual.Character.Eagle && character <= LastCharacterForTeam(team);

    public static bool ValidRoster(MatchGameMode mode, int teamSize, int[] slots, bool simulation)
    {
        if (slots == null || (mode != MatchGameMode.FreeForAll && mode != MatchGameMode.TeamFight))
            return false;
        if (mode == MatchGameMode.TeamFight)
        {
            if (teamSize < 2 || teamSize > 4 || slots.Length != teamSize * 2) return false;
        }
        else if (slots.Length < 2 || slots.Length > 4) return false;

        int humans = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            if (slot == Human) humans++;
            else if (slot != RandomBot && !CharacterAllowed(slot, TeamForSlot(mode, teamSize, i))) return false;
        }
        return !simulation || humans == 0;
    }

    // Reserved bots count toward team balance even before their fighters spawn.
    public static int FindHumanSlot(MatchGameMode mode, int teamSize, int[] slots, int occupiedMask, int requiredTeam = 0)
    {
        int first = -1;
        int second = -1;
        int firstCount = 0;
        int secondCount = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            int team = TeamForSlot(mode, teamSize, i);
            bool occupied = slots[i] != Human || (occupiedMask & (1 << i)) != 0;
            if (team == 2)
            {
                if (occupied) secondCount++;
                else if (second < 0) second = i;
            }
            else
            {
                if (occupied) firstCount++;
                else if (first < 0) first = i;
            }
        }
        if (requiredTeam == 1) return first;
        if (requiredTeam == 2) return second;
        return first >= 0 && (second < 0 || firstCount <= secondCount) ? first : second;
    }

    public static bool AreOpposingTeams(int first, int second) => first == NoTeam || first != second;

    public static int WinningTeam(int survivingTeams) =>
        survivingTeams == 1 ? 1 : survivingTeams == 2 ? 2 : NoTeam;

    public static string ModeLabel(MatchGameMode mode, int teamSize) =>
        mode == MatchGameMode.TeamFight ? $"Team Fight · {teamSize}v{teamSize}" : "Free For All";
}
