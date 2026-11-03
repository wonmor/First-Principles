using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// -----------------------------------------------------------------------------
// Space3DCameraRig: orbit / pan / zoom around a target, or follow the player (third person).
// -----------------------------------------------------------------------------
//   Mouse: left drag orbit, right or middle drag pan, wheel zoom.
//   Touch: one finger orbit, two fingers pinch zoom + pan (in follow mode only the right half orbits;
//          the left half is the move stick, see Space3DStageView).
//   Gamepad: right stick orbit, triggers zoom (left stick is left to UI navigation / walking).
//   Keyboard (not while typing): Q / E orbit, R / F tilt, + / − zoom.
// A short tap / click that did not drag raises Tapped(screenPos) (inspect a point).
// -----------------------------------------------------------------------------

public class Space3DCameraRig : MonoBehaviour
{
    public Vector3 Target;
    public float Yaw = -38f;
    public float Pitch = 26f;
    public float Distance = 18f;
    public float MinDistance = 2.5f, MaxDistance = 80f;
    public float MinPitch = -80f, MaxPitch = 85f;

    /// <summary>Third person: target follows this world point every frame.</summary>
    public Func<Vector3> FollowPoint;

    /// <summary>Touch orbit allowed at this screen position (follow mode: right half only). Null = anywhere.</summary>
    public Func<Vector2, bool> TouchOrbitRegion;

    public bool InputEnabled = true;
    public bool AllowPan = true;

    public event Action<Vector2> Tapped;

    Camera cam;
    Vector2 pressPos;
    bool pressOverUi, dragging;
    float pressTime;
    readonly Dictionary<int, bool> touchOverUi = new Dictionary<int, bool>();
    readonly Dictionary<int, Vector2> touchStart = new Dictionary<int, Vector2>();
    bool touchDragged;
    int orbitTouchId = -1;

    public Camera Cam => cam != null ? cam : (cam = GetComponent<Camera>());

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (!EnhancedTouchSupport.enabled)
            EnhancedTouchSupport.Enable();
    }

    public void Frame(Box3 box, float yaw = -38f, float pitch = 26f)
    {
        Target = Space3DRender.ToWorld((box.XMin + box.XMax) * 0.5f, (box.YMin + box.YMax) * 0.5f, (box.ZMin + box.ZMax) * 0.5f);
        Yaw = yaw;
        Pitch = pitch;
        float size = box.Size;
        float fov = Cam != null ? Cam.fieldOfView : 40f;
        float aspectFix = Cam != null && Cam.aspect < 1.3f ? 1.3f / Mathf.Max(0.5f, Cam.aspect) : 1f;
        Distance = Mathf.Clamp(size * 0.95f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspectFix, MinDistance, MaxDistance);
        Apply();
    }

    public static bool PointerOverUi(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null)
            return false;
        var ped = new PointerEventData(es) { position = screenPos };
        var hits = new List<RaycastResult>();
        es.RaycastAll(ped, hits);
        return hits.Count > 0;
    }

    public static bool TypingInField()
    {
        var es = EventSystem.current;
        var go = es != null ? es.currentSelectedGameObject : null;
        var f = go != null ? go.GetComponent<TMP_InputField>() : null;
        return f != null && f.isFocused;
    }

    void Update()
    {
        if (InputEnabled)
        {
            HandleMouse();
            HandleTouch();
            HandleGamepadAndKeys();
        }

        if (FollowPoint != null)
        {
            var p = FollowPoint();
            Target = Vector3.Lerp(Target, p, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));
        }

        Apply();
    }

    void Apply()
    {
        Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
        Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
        var rot = Quaternion.Euler(Pitch, Yaw, 0f);
        transform.SetPositionAndRotation(Target - rot * Vector3.forward * Distance, rot);
    }

    void HandleMouse()
    {
        var m = Mouse.current;
        if (m == null || ETouch.activeTouches.Count > 0)
            return;
        Vector2 pos = m.position.ReadValue();
        bool l = m.leftButton.isPressed, r = m.rightButton.isPressed || m.middleButton.isPressed;
        if (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)
        {
            pressPos = pos;
            pressTime = Time.unscaledTime;
            pressOverUi = PointerOverUi(pos);
            dragging = false;
        }

        Vector2 d = m.delta.ReadValue();
        if ((l || r) && !pressOverUi)
        {
            if ((pos - pressPos).sqrMagnitude > 36f)
                dragging = true;
            if (dragging)
            {
                if (l && !r)
                    Orbit(d * 0.25f);
                else if (AllowPan)
                    Pan(d);
            }
        }

        if (m.leftButton.wasReleasedThisFrame && !pressOverUi && !dragging && Time.unscaledTime - pressTime < 0.5f)
            Tapped?.Invoke(pos);

        float scroll = m.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi(pos))
            Distance *= Mathf.Pow(0.9f, Mathf.Clamp(scroll / 120f, -3f, 3f) * (Mathf.Abs(scroll) < 10f ? 10f : 1f));
    }

    void HandleTouch()
    {
        var touches = ETouch.activeTouches;
        if (touches.Count == 0)
        {
            touchOverUi.Clear();
            touchStart.Clear();
            orbitTouchId = -1;
            return;
        }

        foreach (var t in touches)
        {
            if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                bool over = PointerOverUi(t.screenPosition);
                bool region = TouchOrbitRegion == null || TouchOrbitRegion(t.screenPosition);
                touchOverUi[t.touchId] = over || !region;
                touchStart[t.touchId] = t.screenPosition;
                if (touches.Count == 1)
                    touchDragged = false;
            }
        }

        // Fingers that are ours (started on the 3D view, in our region).
        var mine = new List<ETouch>();
        foreach (var t in touches)
        {
            if (touchOverUi.TryGetValue(t.touchId, out bool over) && !over)
                mine.Add(t);
        }

        if (mine.Count == 1)
        {
            var t = mine[0];
            if (touchStart.TryGetValue(t.touchId, out var s) && (t.screenPosition - s).sqrMagnitude > 100f)
                touchDragged = true;
            if (touchDragged)
                Orbit(t.delta * 0.22f);
            if ((t.phase == UnityEngine.InputSystem.TouchPhase.Ended) && !touchDragged && t.time - t.startTime < 0.45)
                Tapped?.Invoke(t.screenPosition);
        }
        else if (mine.Count >= 2)
        {
            touchDragged = true;
            var a = mine[0];
            var b = mine[1];
            Vector2 pa = a.screenPosition, pb = b.screenPosition;
            Vector2 qa = pa - a.delta, qb = pb - b.delta;
            float now = (pa - pb).magnitude, before = (qa - qb).magnitude;
            if (before > 10f && now > 10f)
                Distance *= before / now;
            if (AllowPan)
                Pan((a.delta + b.delta) * 0.5f);
        }
    }

    void HandleGamepadAndKeys()
    {
        float dt = Time.unscaledDeltaTime;
        var gp = Gamepad.current;
        if (gp != null)
        {
            Vector2 rs = gp.rightStick.ReadValue();
            if (rs.sqrMagnitude > 0.02f)
                Orbit(new Vector2(rs.x, -rs.y) * 180f * dt / 0.25f * 0.25f);
            float z = gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
            if (Mathf.Abs(z) > 0.05f)
                Distance *= Mathf.Pow(0.35f, z * dt);
        }

        var k = Keyboard.current;
        if (k == null || TypingInField())
            return;
        float yaw = (k.eKey.isPressed ? 1f : 0f) - (k.qKey.isPressed ? 1f : 0f);
        float tilt = (k.rKey.isPressed ? 1f : 0f) - (k.fKey.isPressed ? 1f : 0f);
        if (yaw != 0f || tilt != 0f)
            Orbit(new Vector2(yaw, -tilt) * 120f * dt / 0.25f * 0.25f);
        float zoom = (k.equalsKey.isPressed || k.numpadPlusKey.isPressed ? 1f : 0f) - (k.minusKey.isPressed || k.numpadMinusKey.isPressed ? 1f : 0f);
        if (zoom != 0f)
            Distance *= Mathf.Pow(0.4f, zoom * dt);
    }

    /// <param name="deg">Degrees: x = yaw, y = pitch (screen drag up tilts the view down onto the plot).</param>
    public void Orbit(Vector2 deg)
    {
        Yaw += deg.x;
        Pitch -= deg.y;
    }

    void Pan(Vector2 screenDelta)
    {
        if (FollowPoint != null)
            return;
        float scale = Distance * 2f * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
        Target -= (transform.right * screenDelta.x + transform.up * screenDelta.y) * scale;
    }

    /// <summary>Horizontal forward of the view (world), for camera-relative walking.</summary>
    public Vector3 FlatForward
    {
        get
        {
            var f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }
    }
}
