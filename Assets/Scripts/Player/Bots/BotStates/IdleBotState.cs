using Player.Bots;

public sealed class IdleBotState : IBotState
{
    public IBotState NextState(PlayerNetworkBridge observations) => BotBrain.Choose(observations, this);
    public BotInputFrame ReadController(PlayerNetworkBridge observations) => default;
}
