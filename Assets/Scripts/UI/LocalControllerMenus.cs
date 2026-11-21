#if !UNITY_SERVER || UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Processors;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Local controller navigation for the existing menu event systems.</summary>
public sealed class LocalControllerMenus : MonoBehaviour
{
    private readonly HashSet<InputAction> configured = new HashSet<InputAction>();
    private readonly HashSet<InputAction> configuredHat = new HashSet<InputAction>();
    private readonly Dictionary<InputActionAsset, bool> configuredPlayers = new Dictionary<InputActionAsset, bool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        InputSystem.RegisterProcessor<RockHatUp>("rockHatUp");
        InputSystem.RegisterProcessor<RockHatDown>("rockHatDown");
        InputSystem.RegisterProcessor<RockHatLeft>("rockHatLeft");
        InputSystem.RegisterProcessor<RockHatRight>("rockHatRight");
        if (FindFirstObjectByType<LocalControllerMenus>() != null) return;
        var root = new GameObject("Local controller menus");
        DontDestroyOnLoad(root);
        root.AddComponent<LocalControllerMenus>();
    }

    private void Update()
    {
        ConfigurePlayerController();
        EventSystem events = EventSystem.current;
        if (events == null || !events.isActiveAndEnabled) return;
        var input = events.GetComponent<InputSystemUIInputModule>();
        if (input == null || !input.isActiveAndEnabled) return;
        ConfigureSubmit(input.submit != null ? input.submit.action : null);
        ConfigureNavigate(input.move != null ? input.move.action : null);
        ConfigureCancel(input.cancel != null ? input.cancel.action : null);
        if (IsRockCandy(Joystick.current)) ConfigureHat(input.move != null ? input.move.action : null, false);

        if (PausePressed())
        {
            var settings = FindFirstObjectByType<LocalAudioSettingsUI>();
            if (settings != null) settings.Toggle();
            return;
        }

        if (BackPressed() && TryGoBack()) return;

        if (!ControllerUsed()) return;
        var selected = events.currentSelectedGameObject;
        if (selected != null && selected.activeInHierarchy)
        {
            var selectable = selected.GetComponent<Selectable>();
            if (selectable != null && selectable.IsInteractable())
            {
                if (JoystickSubmitPressed()) ExecuteEvents.Execute(selected, new BaseEventData(events), ExecuteEvents.submitHandler);
                return;
            }
        }

        // Don't capture controller input for gameplay just because Settings exists.
        bool inMenu = SceneManager.GetSceneByName("MainMenu").isLoaded;
        var match = FindFirstObjectByType<MatchManager>();
        bool ready = match != null && match.IsSpawned &&
            match.CurrentPhase == MatchManager.MatchPhase.Overview;
        bool results = match != null && match.IsSpawned &&
            (match.CurrentPhase == MatchManager.MatchPhase.RoundResults || match.CurrentPhase == MatchManager.MatchPhase.MatchFinished);
        if (!results)
            foreach (var item in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (item.name == "Return To Menu Button" && item.isActiveAndEnabled && item.IsInteractable()) { results = true; break; }
        if (!inMenu && !ready && !results) return;

        Selectable fallback = null;
        foreach (var item in FindObjectsByType<Selectable>(FindObjectsSortMode.None))
        {
            if (!(item is Button) || !item.isActiveAndEnabled || !item.IsInteractable()) continue;
            string name = item.name;
            if (name == "Ready Button" || name == "Join Online Button" || name == "Online Game Button" || name == "Return To Menu Button")
            { fallback = item; break; }
            if (fallback == null && name != "Settings button") fallback = item;
        }
        if (fallback != null) events.SetSelectedGameObject(fallback.gameObject);
    }

    private static bool BackPressed()
    {
        var gamepad = Gamepad.current;
        if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame) return true;
        var joystick = Joystick.current;
        var right = joystick != null ? joystick.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(IsRockCandy(joystick) ? "Button 3" : "Button 2") : null;
        return right != null && right.wasPressedThisFrame;
    }

    private static bool TryGoBack()
    {
        var settings = FindFirstObjectByType<LocalAudioSettingsUI>();
        if (settings != null && settings.IsOpen) { settings.Close(); return true; }
        if (!SceneManager.GetSceneByName("MainMenu").isLoaded) return false;
        foreach (string name in new[] { "Close Controls Button", "Close Credits Button", "Close Attributions Button", "Join Back Button", "Back Button" })
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (button.name != name || !button.isActiveAndEnabled || !button.IsInteractable()) continue;
                button.onClick.Invoke();
                return true;
            }
        }
        return false;
    }

    private void ConfigureSubmit(InputAction action)
    {
        if (action == null || !configured.Add(action)) return;
        ReplaceBindings(action, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space", "<Gamepad>/buttonSouth");
    }

    private void ConfigureCancel(InputAction action)
    {
        if (action == null || !configured.Add(action)) return;
        // Controller Back is handled once by TryGoBack; avoid generic joystick
        // wildcard bindings treating every controller button as Cancel.
        ReplaceBindings(action, "<Keyboard>/escape");
    }

    private void ConfigureNavigate(InputAction action)
    {
        if (action == null || !configured.Add(action)) return;
        AddBindings(action, "<Gamepad>/leftStick", "<Joystick>/stick");
    }

    private static void AddBindings(InputAction action, params string[] paths)
    {
        bool enabled = action.enabled;
        if (enabled) action.Disable();
        foreach (string path in paths)
        {
            bool found = false;
            foreach (var binding in action.bindings)
                if (!binding.isPartOfComposite && binding.path == path) { found = true; break; }
            if (!found) action.AddBinding(path);
        }
        if (enabled) action.Enable();
    }

    private static void ReplaceBindings(InputAction action, params string[] paths)
    {
        bool enabled = action.enabled;
        if (enabled) action.Disable();
        int count = action.bindings.Count;
        for (int i = 0; i < count; i++) action.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
        foreach (string path in paths) action.AddBinding(path);
        if (enabled) action.Enable();
    }

    private static bool PausePressed()
    {
        var gamepad = Gamepad.current;
        if (gamepad != null && gamepad.startButton.wasPressedThisFrame) return true;
        var joystick = Joystick.current;
        var start = joystick != null ? joystick.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(IsRockCandy(joystick) ? "Button 13" : "Button 10") : null;
        return start != null && start.wasPressedThisFrame;
    }

    private static bool ControllerUsed()
    {
        var gamepad = Gamepad.current;
        if (gamepad != null && (gamepad.leftStick.ReadValue().sqrMagnitude > .25f ||
            gamepad.dpad.ReadValue().sqrMagnitude > .25f || gamepad.buttonSouth.wasPressedThisFrame)) return true;
        var joystick = Joystick.current;
        if (joystick == null) return false;
        if (joystick.stick.ReadValue().sqrMagnitude > .25f) return true;
        if (IsRockCandy(joystick))
        {
            var hat = joystick.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("Axis 8");
            if (hat != null && hat.ReadValue() >= -1.1f && hat.ReadValue() <= 1.1f) return true;
        }
        var bottom = joystick.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(IsRockCandy(joystick) ? "Button 2" : "Button 1");
        return bottom != null && bottom.wasPressedThisFrame;
    }

    private static bool IsRockCandy(Joystick joystick)
    {
        return joystick != null && joystick.description.product != null &&
            joystick.description.product.IndexOf("Rock Candy Wired Controller for Nintendo Switch", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool JoystickSubmitPressed()
    {
        var joystick = Joystick.current;
        var button = joystick != null ? joystick.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(IsRockCandy(joystick) ? "Button 2" : "Button 1") : null;
        return button != null && button.wasPressedThisFrame;
    }

    private void ConfigurePlayerController()
    {
        foreach (var player in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
        {
            if (!player.isActiveAndEnabled || player.actions == null) continue;
            Joystick joystick = null;
            foreach (var device in player.devices) if (device is Joystick found) { joystick = found; break; }
            if (joystick == null) continue;
            bool rock = IsRockCandy(joystick);
            if (configuredPlayers.TryGetValue(player.actions, out bool previous) && previous == rock) continue;
            configuredPlayers[player.actions] = rock;
            OverrideJoystick(player.actions.FindAction("Jump"), rock ? "Button 2" : "Button 1");
            OverrideJoystick(player.actions.FindAction("Punch"), rock ? "Button 1" : "Button 3");
            OverrideJoystick(player.actions.FindAction("Beast"), "Button 4");
            if (rock) ConfigureHat(player.actions.FindAction("Move"), true);
        }
    }

    private static void OverrideJoystick(InputAction action, string control)
    {
        if (action == null) return;
        for (int i = 0; i < action.bindings.Count; i++)
            if (action.bindings[i].path != null && action.bindings[i].path.StartsWith("<Joystick>/Button "))
                action.ApplyBindingOverride(i, new InputBinding { overridePath = "<Joystick>/" + control });
    }

    private void ConfigureHat(InputAction action, bool grouped)
    {
        if (action == null || !configuredHat.Add(action)) return;
        bool enabled = action.enabled;
        if (enabled) action.Disable();
        string group = grouped ? "Joystick" : null;
        action.AddCompositeBinding("2DVector")
            .With("Up", "<Joystick>/Axis 8", groups: group, processors: "rockHatUp")
            .With("Down", "<Joystick>/Axis 8", groups: group, processors: "rockHatDown")
            .With("Left", "<Joystick>/Axis 8", groups: group, processors: "rockHatLeft")
            .With("Right", "<Joystick>/Axis 8", groups: group, processors: "rockHatRight");
        if (enabled) action.Enable();
    }
}

// Rock Candy's unstandardized Web HID reports the D-pad as a hat axis;
// values above 1 are neutral, and the eight directions run from -1 to 1.
public sealed class RockHatUp : InputProcessor<float> { public override float Process(float v, UnityEngine.InputSystem.InputControl c) => v >= -1.1f && v < -.75f || v > .85f && v < 1.1f ? 1f : 0f; }
public sealed class RockHatRight : InputProcessor<float> { public override float Process(float v, UnityEngine.InputSystem.InputControl c) => v >= -.8f && v <= -.28f ? 1f : 0f; }
public sealed class RockHatDown : InputProcessor<float> { public override float Process(float v, UnityEngine.InputSystem.InputControl c) => v >= -.25f && v <= .3f ? 1f : 0f; }
public sealed class RockHatLeft : InputProcessor<float> { public override float Process(float v, UnityEngine.InputSystem.InputControl c) => v >= .3f && v <= .85f ? 1f : 0f; }
#endif
