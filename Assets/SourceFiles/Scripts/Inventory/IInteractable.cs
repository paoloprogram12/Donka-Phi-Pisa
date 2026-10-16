namespace DonkaPhiPisa.Inventory
{
    /// <summary>
    /// Anything the player can look at and press E on: items now, later doors, switches, notes...
    /// The object also needs a NetworkIdentity (so the server knows which object you meant)
    /// and a Collider (so the look raycast can hit it).
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Shown after "[E] ", e.g. "Pick up Flashlight". Runs on the local client.</summary>
        string GetPrompt(PlayerInteractor player);

        /// <summary>Whether the prompt should show / E should work right now. Runs on the local client.</summary>
        bool CanInteract(PlayerInteractor player);

        /// <summary>The actual effect. Runs ON THE SERVER only, after the server checked distance.</summary>
        void ServerInteract(PlayerInteractor player);
    }
}
