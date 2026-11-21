namespace Player.Bots {
    public static class BotReflexes {
        
        // Returns a new reflex state or lets the current state decide
        public static IBotState Check(PlayerNetworkBridge observations, IBotState currentState) {

            if (observations.Eliminated) {
                return (currentState is IdleBotState) ? null : new IdleBotState();

            }

            if (!observations.IsConscious) {
                return (currentState is WakeUpBotState) ? null : new WakeUpBotState();
            }

            if (!observations.OnFloor && !observations.HasGroundBelow &&
                observations.ActiveBotBeast?.ControlsMovement != true) {
                return currentState is RecoverBotState ? null : new RecoverBotState();
            }
            return null;

        }
    }
}
