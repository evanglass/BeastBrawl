using Unity.Netcode;

public struct BotInputFrame : INetworkSerializable
{
    public float Horizontal;
    public bool Jump;
    public bool Punch;
    public bool Beast;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Horizontal);
        serializer.SerializeValue(ref Jump);
        serializer.SerializeValue(ref Punch);
        serializer.SerializeValue(ref Beast);
    }
}
