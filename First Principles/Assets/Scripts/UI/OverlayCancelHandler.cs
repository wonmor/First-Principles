using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Marks a runtime overlay as the current cancel target for controller B / Esc input.
/// <see cref="GamepadSupport"/> clicks <see cref="cancelButton"/> when cancel is pressed on
/// the topmost registered overlay, and UI focus moves onto <see cref="initialFocus"/> while
/// a controller is the active device so d-pad navigation starts inside the overlay.
/// </summary>
public class OverlayCancelHandler : MonoBehaviour
{
    private static readonly List<OverlayCancelHandler> stack = new List<OverlayCancelHandler>();

    private Button cancelButton;
    private Selectable initialFocus;

    public static OverlayCancelHandler Topmost => stack.Count > 0 ? stack[stack.Count - 1] : null;

    public GameObject InitialFocusObject
    {
        get
        {
            if (initialFocus != null)
                return initialFocus.gameObject;
            return cancelButton != null ? cancelButton.gameObject : null;
        }
    }

    /// <summary>
    /// Registers <paramref name="overlayRoot"/> (idempotent — safe on the reuse/reshow path).
    /// <paramref name="cancelButton"/> is invoked on cancel; focus lands on
    /// <paramref name="initialFocus"/> (defaults to the cancel button).
    /// </summary>
    public static OverlayCancelHandler Attach(GameObject overlayRoot, Button cancelButton, Selectable initialFocus = null)
    {
        var handler = overlayRoot.GetComponent<OverlayCancelHandler>();
        if (handler == null)
            handler = overlayRoot.AddComponent<OverlayCancelHandler>();
        handler.cancelButton = cancelButton;
        handler.initialFocus = initialFocus != null ? initialFocus : cancelButton;
        handler.FocusInitial();
        return handler;
    }

    public void Cancel()
    {
        if (cancelButton != null && cancelButton.isActiveAndEnabled)
            cancelButton.onClick.Invoke();
        else
            gameObject.SetActive(false);
    }

    public void FocusInitial()
    {
#if ENABLE_INPUT_SYSTEM
        if (!GamepadSupport.ControllerActive)
            return;
        var es = EventSystem.current;
        var go = InitialFocusObject;
        if (es != null && go != null && go.activeInHierarchy)
            es.SetSelectedGameObject(go);
#endif
    }

    private void OnEnable()
    {
        stack.Remove(this);
        stack.Add(this);
        FocusInitial();
    }

    private void OnDisable()
    {
        stack.Remove(this);
    }
}
