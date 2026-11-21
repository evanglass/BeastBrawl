using System;

[Serializable]
public sealed class CustomLobbyListing
{
    public string id;
    public string host;
    public int mode;
    public int teamSize;
    public int occupied;
    public int capacity;
    public int availableHumans;
    public int humanCapacity;
}
