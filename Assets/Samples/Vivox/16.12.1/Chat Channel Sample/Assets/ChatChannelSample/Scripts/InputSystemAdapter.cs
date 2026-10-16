using UnityEngine;
using UnityEngine.EventSystems;
#if INPUT_SYSTEM_PRESENT
using UnityEngine.InputSystem.UI;
#endif

// When com.unity.inputsystem is installed and set as the exclusive input handler,
// StandaloneInputModule throws InvalidOperationException because it calls
// UnityEngine.Input (legacy) internally. This script swaps it for
// InputSystemUIInputModule in Awake(), before EventSystem.Update() can fire.
// If the legacy input system is active (or both), StandaloneInputModule is left alone.
[RequireComponent(typeof(EventSystem))]
public class InputSystemAdapter : MonoBehaviour
{
    void Awake()
    {
#if INPUT_SYSTEM_PRESENT && !ENABLE_LEGACY_INPUT_MANAGER
            if (TryGetComponent<StandaloneInputModule>(out var legacy))
            {
                legacy.enabled = false;
                Destroy(legacy);
            }

            if (!TryGetComponent<InputSystemUIInputModule>(out _))
            {
                gameObject.AddComponent<InputSystemUIInputModule>();
            }
#endif
    }
}
