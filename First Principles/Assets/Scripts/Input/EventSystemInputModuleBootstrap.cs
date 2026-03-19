#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Replaces legacy <see cref="StandaloneInputModule"/> with <see cref="InputSystemUIInputModule"/>
/// on each scene load so uGUI works when <b>Active Input Handling</b> is set to <b>Input System Package</b>.
/// Idempotent: scenes that already use <see cref="InputSystemUIInputModule"/> are left alone.
/// </summary>
public static class EventSystemInputModuleBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInputSystemUiModule()
    {
        var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        foreach (var es in eventSystems)
        {
            if (es == null)
                continue;

            var go = es.gameObject;
            var standalone = go.GetComponent<StandaloneInputModule>();
            var inputModule = go.GetComponent<InputSystemUIInputModule>();

            if (standalone != null)
            {
                // Remove immediately so EventSystem never sees two BaseInputModules in one frame.
                Object.DestroyImmediate(standalone);
                if (go.GetComponent<InputSystemUIInputModule>() == null)
                    go.AddComponent<InputSystemUIInputModule>();
            }
            else if (inputModule != null && inputModule.actionsAsset == null)
            {
                inputModule.AssignDefaultActions();
            }
        }
    }
}
#endif
