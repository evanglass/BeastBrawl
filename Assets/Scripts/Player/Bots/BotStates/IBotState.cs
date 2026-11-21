public interface IBotState
{
    IBotState NextState(PlayerNetworkBridge observations);
    BotInputFrame ReadController(PlayerNetworkBridge observations);
}
