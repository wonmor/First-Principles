using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// =============================================================================
// PlayerControllerUI2D — UI RectTransform “character” in graph grid space
// =============================================================================
// Simulation runs in abstract grid units (same as GraphWorld / GridRect). Each frame:
//   • Reads horizontal intent (keys → axis → MobileInputBridge touch cluster).
//   • Applies gravity, resolves AABB vs platforms, hazards, fall death Y, finish band.
// Visual position maps grid → pixels via unitWidth/Height from LevelManager.
// =============================================================================

/// <summary>Side-scroller controller built for uGUI under the Cartesian plane.</summary>
public class PlayerControllerUI2D : MonoBehaviour
{
    [Header("Movement (Grid Units)")]
    [SerializeField] private float moveSpeedGridPerSec = 7f;
    [SerializeField] private float gravityGridPerSec2 = 15.5f;
    [SerializeField] private float jumpVelocityGridPerSec = 11.2f;

    [Header("Feel (1.3)")]
    [Tooltip("Ground acceleration toward the target speed (grid/s²). High enough that full speed arrives in ~0.09 s.")]
    [SerializeField] private float groundAccel = 80f;
    [SerializeField] private float groundDecel = 95f;
    [SerializeField] private float airAccel = 60f;
    [Tooltip("Grace window to jump after walking off a ledge or during a landing bounce.")]
    [SerializeField] private float coyoteSeconds = 0.1f;
    [Tooltip("A jump pressed this long before landing still fires on touchdown.")]
    [SerializeField] private float jumpBufferSeconds = 0.13f;
    [Tooltip("Releasing jump while rising cuts the upward speed (short hop). Holding gives the full, level-tuned arc.")]
    [Range(0.2f, 1f)] [SerializeField] private float jumpCutMultiplier = 0.6f;
    [Tooltip("Gravity multiplier near the apex while jump is held (a touch of hang time).")]
    [Range(0.3f, 1f)] [SerializeField] private float apexGravityMultiplier = 0.7f;
    [SerializeField] private float apexSpeedBand = 1.6f;
    [SerializeField] private float maxFallSpeed = 24f;

    [Header("f′ line — air jump")]
    [Tooltip("Grid-space proximity to f′ (player center vs polyline). Wider = easier to register a 'hit'.")]
    [SerializeField] private float derivativeTouchRadiusGrid = 0.58f;
    [Tooltip("Upward impulse when you press jump while airborne and overlapping f′ (once per approach until you leave the band).")]
    [SerializeField] private float doubleJumpVelocityMultiplier = 0.9f;
    [SerializeField] private float derivativeHighlightLerpSpeed = 7f;
    [Tooltip("First frame the avatar enters the f′ band (not every frame while sliding). On mobile, vibration replaces sound.")]
    [SerializeField] private bool derivativeHitHaptic = true;
    [Tooltip("Short click when haptic is unavailable (PC / editor / haptic off). Ignored on mobile when haptic is on.")]
    [SerializeField] private bool derivativeHitSound = true;
    [SerializeField] private AudioClip derivativeLineHitClip;
    [SerializeField] [Range(0f, 1f)] private float derivativeLineHitVolume = 0.65f;
    [Tooltip("Brief cool tint on the graph backdrop (Game scene → Grid Background) when you graze f′ — mobile & desktop.")]
    [SerializeField] private bool derivativeHitBackgroundTint = true;
    [SerializeField] private float derivativeHitBackgroundTintSeconds = 0.32f;
    [SerializeField] private Color derivativeHitBackgroundTintDelta = new Color(0.048f, 0.06f, 0.1f, 0f);

    [Header("Player Size (Grid Units)")]
    [SerializeField] private float playerWidthGrid = 0.78f;
    [SerializeField] private float playerHeightGrid = 1.12f;

    [Header("Bouncy Ball")]
    [Tooltip("Restitution on platform landings (0 = stick, 1 = perfect bounce). Kept modest so 59 levels still platform cleanly.")]
    [Range(0f, 1f)] [SerializeField] private float groundBounciness = 0.42f;
    [Tooltip("Below this downward speed (grid/s), landings settle instead of bouncing.")]
    [SerializeField] private float bounceVelocityCutoffGridPerSec = 2.2f;
    [SerializeField] private float landingSquashSeconds = 0.18f;
    [Range(0f, 0.6f)] [SerializeField] private float landingSquashStrength = 0.22f;

    [Header("Skyblue Aura")]
    [SerializeField] private bool showSphereAura = true;
    [SerializeField] private Color sphereAuraColor = new Color(0.49f, 0.78f, 1f, 0.55f);
    [Tooltip("Sphere overlay size relative to the player AABB (1 = same size).")]
    [SerializeField] private float sphereAuraScale = 1.55f;

    private RectTransform playerRect;
    private Image playerImage;

    private float unitWidth;
    private float unitHeight;

    private GraphWorld world;
    private Vector2 posGrid;
    private Vector2 velGrid;
    private bool grounded;

    private float deathMinYGrid = -2f;
    private bool isDead;
    private bool isFinished;

    private Action deathCallback;
    private Action finishCallback;
    /// <summary>Invoked on the first frame the avatar enters the f′ proximity band (with hit feedback).</summary>
    private Action derivativeLineGrazeScoringCallback;

    /// <summary>When true (e.g. stage intro overlay), physics/input integration is skipped so the avatar stays frozen.</summary>
    private bool inputLocked;

    private DerivRendererUI derivativeLineRenderer;
    private bool wasTouchingDerivativeLine;
    /// <summary>True after last call to <see cref="UpdateDerivativeTouchAndHighlight"/> this frame.</summary>
    private bool touchingDerivativeNow;
    /// <summary>After using f′ air jump, stay true until you leave the band or land (prevents spam while sliding on the line).</summary>
    private bool derivativeAirJumpConsumedThisBand;
    /// <summary>Fresh level entry: first grounded jump uses a small boost once (~12% over base).</summary>
    private bool strongFirstGroundJumpPending;
    private float derivativeHighlightSmoothed;
    private AudioSource derivativeHitAudio;

    private Image gridBackgroundImage;
    private Color gridBackgroundRestColor;
    private float derivativeBgTintPhase;

    private Image sphereAura;
    private float squashTimer;
    private float stretchTimer;
    const float StretchSeconds = 0.14f;

    private float coyoteTimer;
    private float jumpBufferTimer;
    private bool jumpCutAvailable;
    private float groundJumpBoost = 1f;
    private bool touchJumpHeld;

    /// <summary>Raised when a ground jump fires (argument = velocity multiplier, &gt; 1 on a spring).</summary>
    public event Action<float> GroundJumped;
    /// <summary>Raised on an f′ air jump.</summary>
    public event Action AirJumped;
    /// <summary>Raised when the avatar lands (bounce or settle) with the downward speed.</summary>
    public event Action<float> LandedWithSpeed;

    public bool Grounded => grounded;
    public Vector2 VelocityGrid => velGrid;
    public Vector2 PlayerSizeGrid => new Vector2(playerWidthGrid, playerHeightGrid);
    public bool IsDeadOrFinished => isDead || isFinished;

    /// <summary>Spring pads call this every frame the avatar stands on them (reset after each jump/frame).</summary>
    public void SetGroundJumpBoost(float multiplier) => groundJumpBoost = Mathf.Max(groundJumpBoost, multiplier);

    /// <summary>Moving hazards (CourseExtras) end the run through the normal death flow.</summary>
    public void Kill() => Die();

    /// <summary>Checkpoint respawn: stand on top of the given platform column.</summary>
    public void TeleportTo(float xGrid, float topYGrid)
    {
        posGrid = new Vector2(xGrid, topYGrid + playerHeightGrid / 2f + 0.01f);
        velGrid = Vector2.zero;
        ClampPositionToPlayBounds();
        ApplyVisualPosition();
    }

    const string GridBackgroundObjectName = "Grid Background";

    public Vector2 PlayerCenterGrid => posGrid;

    /// <summary>Current collision world (hint markers read its platforms / gaps).</summary>
    public GraphWorld World => world;

    /// <summary>Root <see cref="RectTransform"/> for the avatar (grid-anchored). For screen-space UI positioning.</summary>
    public RectTransform PlayerVisualRect => playerRect;

    public void BindDerivativeRenderer(DerivRendererUI derivativeRenderer)
    {
        derivativeLineRenderer = derivativeRenderer;
        derivativeHighlightSmoothed = 0f;
        wasTouchingDerivativeLine = false;
        touchingDerivativeNow = false;
        derivativeAirJumpConsumedThisBand = false;
        if (derivativeLineRenderer != null)
            derivativeLineRenderer.playerProximityHighlight = 0f;
    }

    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        if (locked)
            velGrid = Vector2.zero;
    }

    public void BindVisual(RectTransform rect, Image img)
    {
        playerRect = rect;
        playerImage = img;
        EnsureSphereAura();
    }

    void EnsureSphereAura()
    {
        if (!showSphereAura || playerRect == null || sphereAura != null)
            return;

        var auraGo = new GameObject("SkyblueAura");
        var auraRt = auraGo.AddComponent<RectTransform>();
        auraRt.SetParent(playerRect, false);
        auraRt.anchorMin = Vector2.zero;
        auraRt.anchorMax = Vector2.one;
        auraRt.offsetMin = Vector2.zero;
        auraRt.offsetMax = Vector2.zero;
        auraRt.localScale = Vector3.one * sphereAuraScale;
        auraRt.SetAsFirstSibling();

        var auraImg = auraGo.AddComponent<Image>();
        auraImg.sprite = RuntimeUiPolish.SoftCharacterBlob;
        auraImg.color = sphereAuraColor;
        auraImg.raycastTarget = false;
        sphereAura = auraImg;
    }

    public void SetDeathMinYGrid(float deathMinYGrid)
    {
        this.deathMinYGrid = deathMinYGrid;
    }

    public void SetGridToPixelUnits(float unitWidth, float unitHeight)
    {
        this.unitWidth = unitWidth;
        this.unitHeight = unitHeight;

        if (playerRect != null)
        {
            playerRect.sizeDelta = new Vector2(playerWidthGrid * unitWidth, playerHeightGrid * unitHeight);
        }
    }

    public void SetWorld(GraphWorld world)
    {
        this.world = world;
    }

    public void ResetToSpawn(GraphWorld world, bool grantStrongFirstGroundJump = false)
    {
        if (world == null)
            return;

        this.world = world;
        isDead = false;
        isFinished = false;
        grounded = false;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        jumpCutAvailable = false;
        groundJumpBoost = 1f;
        strongFirstGroundJumpPending = grantStrongFirstGroundJump;

        float halfH = playerHeightGrid / 2f;

        posGrid = new Vector2(world.spawnXGrid, world.spawnYTopGrid + halfH + 0.01f);
        velGrid = Vector2.zero;

        wasTouchingDerivativeLine = false;
        touchingDerivativeNow = false;
        derivativeAirJumpConsumedThisBand = false;
        derivativeHighlightSmoothed = 0f;
        if (derivativeLineRenderer != null)
        {
            derivativeLineRenderer.playerProximityHighlight = 0f;
            derivativeLineRenderer.RefreshHighlightGeometry();
        }

        derivativeBgTintPhase = 0f;
        RefreshGridBackgroundCacheAndRestoreColor();

        ApplyVisualPosition();
    }

    public void SetDeathCallback(Action deathCallback)
    {
        this.deathCallback = deathCallback;
    }

    public void SetFinishCallback(Action finishCallback)
    {
        this.finishCallback = finishCallback;
    }

    public void SetDerivativeLineGrazeScoringCallback(Action callback)
    {
        derivativeLineGrazeScoringCallback = callback;
    }

    /// <summary>Integrate movement, resolve collisions, invoke death/finish callbacks.</summary>
    private void Update()
    {
        if (world == null || playerRect == null)
            return;

        if (isDead || isFinished)
            return;

        if (inputLocked)
            return;

        float dt = Time.deltaTime;

        // Movement: keyboard (arrows / WASD), gamepad stick, then on-screen touch bridge.
        float inputX = ReadHorizontalInput();
        if (Mathf.Approximately(inputX, 0f))
            inputX = MobileInputBridge.TouchHorizontal;

        // Acceleration curve toward the target speed (top speed unchanged, so level tuning holds).
        float target = inputX * moveSpeedGridPerSec;
        float rate = !grounded ? airAccel
            : Mathf.Abs(target) < 0.01f ? groundDecel
            : Mathf.Sign(target) != Mathf.Sign(velGrid.x) && Mathf.Abs(velGrid.x) > 0.1f ? groundDecel * 1.4f
            : groundAccel;
        velGrid.x = Mathf.MoveTowards(velGrid.x, target, rate * dt);

        if (grounded)
        {
            derivativeAirJumpConsumedThisBand = false;
            coyoteTimer = coyoteSeconds;
        }
        else
            coyoteTimer -= dt;

        UpdateDerivativeTouchAndHighlight(dt);

        bool jumpPressed = ReadJumpPressed();
        if (!jumpPressed)
        {
            jumpPressed = MobileInputBridge.ConsumeJump();
            if (jumpPressed)
                touchJumpHeld = true; // taps have no duration: full arc
        }
        else
            touchJumpHeld = false;
        if (jumpPressed)
            jumpBufferTimer = jumpBufferSeconds;
        else
            jumpBufferTimer -= dt;

        if (jumpBufferTimer > 0f)
        {
            if (grounded || coyoteTimer > 0f)
            {
                float jv = jumpVelocityGridPerSec * groundJumpBoost;
                if (strongFirstGroundJumpPending)
                {
                    jv *= 1.12f;
                    strongFirstGroundJumpPending = false;
                }
                velGrid.y = jv;
                grounded = false;
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                jumpCutAvailable = true;
                stretchTimer = StretchSeconds;
                GameFeel.RaiseJump(false);
                GroundJumped?.Invoke(groundJumpBoost);
            }
            else if (jumpPressed && CanUseDerivativeAirJump())
            {
                velGrid.y = jumpVelocityGridPerSec * doubleJumpVelocityMultiplier;
                derivativeAirJumpConsumedThisBand = true;
                jumpBufferTimer = 0f;
                jumpCutAvailable = true;
                stretchTimer = StretchSeconds;
                GameFeel.RaiseJump(true);
                AirJumped?.Invoke();
            }
        }
        groundJumpBoost = 1f;

        // Variable jump height: let go early for a short hop (holding keeps the full tuned arc).
        if (grounded && velGrid.y <= 0f)
            touchJumpHeld = false;
        bool held = touchJumpHeld || ReadJumpHeld();
        if (jumpCutAvailable && !held && velGrid.y > 0f)
        {
            velGrid.y *= jumpCutMultiplier;
            jumpCutAvailable = false;
        }
        if (velGrid.y <= 0f)
            jumpCutAvailable = false;

        // Gravity (a little hang time at the apex while jump is held).
        float g = gravityGridPerSec2;
        if (held && Mathf.Abs(velGrid.y) < apexSpeedBand)
            g *= apexGravityMultiplier;
        velGrid.y -= g * dt;
        if (velGrid.y < -maxFallSpeed)
            velGrid.y = -maxFallSpeed;

        Vector2 nextPos = posGrid;

        // Horizontal step + collision.
        nextPos.x = posGrid.x + velGrid.x * dt;
        ResolveHorizontalPlatforms(ref nextPos);

        // Vertical sweep should use Y from before this frame's vertical move but X after horizontal
        // (matches diagonal motion; avoids false collisions when strafing along stepped platforms).
        Vector2 prevForVerticalSweep = new Vector2(nextPos.x, posGrid.y);

        // Vertical step + collision.
        nextPos.y = posGrid.y + velGrid.y * dt;
        ResolveVerticalPlatforms(prevForVerticalSweep, ref nextPos, ref grounded);

        // Hazard check.
        if (OverlapsAny(world.hazards, nextPos))
        {
            Die();
        }
        else if (nextPos.y - playerHeightGrid / 2f < deathMinYGrid)
        {
            Die();
        }

        // Finish check.
        if (IsInFinishZone(nextPos))
        {
            isFinished = true;
            if (playerRect != null)
                GameFeel.RaiseFinish(playerRect.position);
            finishCallback?.Invoke();
            return;
        }

        posGrid = nextPos;

        ClampPositionToPlayBounds();

        TickDerivativeBackgroundTint(dt);

        ApplyVisualPosition();
    }

    /// <summary>Hard AABB limits so the avatar stays inside the padded playfield (safe area + touch bar insets).</summary>
    private void ClampPositionToPlayBounds()
    {
        if (world == null || !world.hasPlayBounds)
            return;

        float halfW = playerWidthGrid * 0.5f;
        float halfH = playerHeightGrid * 0.5f;
        var b = world.playBounds;
        posGrid.x = Mathf.Clamp(posGrid.x, b.XMin + halfW, b.XMax - halfW);
        posGrid.y = Mathf.Clamp(posGrid.y, b.YMin + halfH, b.YMax - halfH);
    }

    /// <summary>After rotation or Cartesian plane resize: refresh pixel scale, play-bound grid inset, and clamp.</summary>
    public void ApplyResponsivePlaneLayout(RectTransform plane, Vector2Int gridSize)
    {
        if (world == null || plane == null || gridSize.x < 1 || gridSize.y < 1)
            return;

        float uw = plane.rect.width / gridSize.x;
        float uh = plane.rect.height / gridSize.y;
        SetGridToPixelUnits(uw, uh);

        if (world.hasPlayBounds)
        {
            world.playBounds = GameplayPlayBounds.Compute(plane, gridSize);
            world.RefreshFinishFromPlayBounds();
            SetDeathMinYGrid(world.playBounds.YMin - 0.4f);
        }

        ClampPositionToPlayBounds();
        ApplyVisualPosition();
    }

    /// <summary>Air jump only while overlapping f′ and not already used until you leave the band.</summary>
    private bool CanUseDerivativeAirJump()
    {
        return touchingDerivativeNow && !derivativeAirJumpConsumedThisBand;
    }

    /// <summary>
    /// f′ proximity: highlight, hit feedback, and <see cref="touchingDerivativeNow"/> for air jump.
    /// </summary>
    private void UpdateDerivativeTouchAndHighlight(float dt)
    {
        bool touching = false;
        if (derivativeLineRenderer != null)
        {
            var pts = derivativeLineRenderer.points;
            if (pts != null && pts.Count >= 2 && derivativeTouchRadiusGrid > 0.01f)
                touching = MinDistanceToPolylineSq(posGrid, pts) <= derivativeTouchRadiusGrid * derivativeTouchRadiusGrid;
        }

        // Leave f′ → can earn another air jump on the next approach while aloft.
        if (!touching)
            derivativeAirJumpConsumedThisBand = false;

        if (touching && !wasTouchingDerivativeLine)
        {
            derivativeLineGrazeScoringCallback?.Invoke();
            GameFeel.RaiseGraze();
            PlayDerivativeLineHitFeedback();
            PulseDerivativeBackgroundTint();
        }

        wasTouchingDerivativeLine = touching;
        touchingDerivativeNow = touching;

        if (derivativeLineRenderer != null)
        {
            float targetHl = touching ? 1f : 0f;
            derivativeHighlightSmoothed = Mathf.MoveTowards(
                derivativeHighlightSmoothed,
                targetHl,
                derivativeHighlightLerpSpeed * dt);
            derivativeLineRenderer.playerProximityHighlight = derivativeHighlightSmoothed;
            derivativeLineRenderer.RefreshHighlightGeometry();
        }
    }

    /// <summary>One-shot when the circle first crosses into the f′ proximity band.</summary>
    private void PlayDerivativeLineHitFeedback()
    {
        bool usedHaptic = derivativeHitHaptic && Application.isMobilePlatform;
#if UNITY_ANDROID || UNITY_IOS
        if (usedHaptic)
            Handheld.Vibrate();
#endif

        // Sound only where vibration isn’t used (no haptic on this platform/path).
        if (usedHaptic)
            return;

        if (!derivativeHitSound || derivativeLineHitClip == null)
            return;

        if (derivativeHitAudio == null)
        {
            derivativeHitAudio       = GetComponent<AudioSource>();
            if (derivativeHitAudio == null)
                derivativeHitAudio = gameObject.AddComponent<AudioSource>();
            derivativeHitAudio.playOnAwake = false;
            derivativeHitAudio.spatialBlend = 0f;
            derivativeHitAudio.loop = false;
        }

        derivativeHitAudio.PlayOneShot(derivativeLineHitClip, derivativeLineHitVolume);
    }

    private void EnsureGridBackgroundImage()
    {
        if (gridBackgroundImage != null)
            return;
        var go = GameObject.Find(GridBackgroundObjectName);
        if (go == null)
            return;
        gridBackgroundImage = go.GetComponent<Image>();
        if (gridBackgroundImage != null)
            gridBackgroundRestColor = gridBackgroundImage.color;
    }

    private void RefreshGridBackgroundCacheAndRestoreColor()
    {
        if (gridBackgroundImage == null)
            EnsureGridBackgroundImage();
        if (gridBackgroundImage == null)
            return;
        gridBackgroundRestColor = gridBackgroundImage.color;
        gridBackgroundImage.color = gridBackgroundRestColor;
    }

    private void PulseDerivativeBackgroundTint()
    {
        if (!derivativeHitBackgroundTint || derivativeHitBackgroundTintSeconds < 1e-4f)
            return;
        EnsureGridBackgroundImage();
        if (gridBackgroundImage == null)
            return;
        if (derivativeBgTintPhase <= 0.01f)
            gridBackgroundRestColor = gridBackgroundImage.color;
        derivativeBgTintPhase = 1f;
    }

    private void TickDerivativeBackgroundTint(float dt)
    {
        if (!derivativeHitBackgroundTint || gridBackgroundImage == null)
            return;
        if (derivativeBgTintPhase <= 0f)
            return;

        float dur = Mathf.Max(0.04f, derivativeHitBackgroundTintSeconds);
        derivativeBgTintPhase = Mathf.Max(0f, derivativeBgTintPhase - dt / dur);
        Color peak = gridBackgroundRestColor + derivativeHitBackgroundTintDelta;
        peak.r = Mathf.Clamp01(peak.r);
        peak.g = Mathf.Clamp01(peak.g);
        peak.b = Mathf.Clamp01(peak.b);
        peak.a = gridBackgroundRestColor.a;
        Color c = Color.Lerp(gridBackgroundRestColor, peak, derivativeBgTintPhase);
        c.a = gridBackgroundRestColor.a;
        gridBackgroundImage.color = c;

        if (derivativeBgTintPhase <= 0f)
            gridBackgroundImage.color = gridBackgroundRestColor;
    }

    private static float MinDistanceToPolylineSq(Vector2 p, List<Vector2> pts)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            float d = PointToSegmentDistanceSq(p, pts[i], pts[i + 1]);
            if (d < best)
                best = d;
        }

        return best;
    }

    private static float PointToSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float den = ab.sqrMagnitude;
        if (den < 1e-10f)
            return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
        Vector2 proj = a + t * ab;
        return (p - proj).sqrMagnitude;
    }

    private static float ReadHorizontalInput()
    {
        float inputX = 0f;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed)
                inputX -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed)
                inputX += 1f;
        }

        if (Mathf.Approximately(inputX, 0f))
        {
            var gp = Gamepad.current;
            if (gp != null)
            {
                float lx = gp.leftStick.x.ReadValue();
                if (Mathf.Abs(lx) > 0.5f)
                    inputX = Mathf.Sign(lx);
                else if (Mathf.Abs(lx) > 0.12f)
                    inputX = lx;
            }
        }

        return inputX;
    }

    /// <summary>Jump currently held (keyboard / gamepad). Touch jumps are taps with no hold, so they always get the full arc.</summary>
    private static bool ReadJumpHeld()
    {
        if (DeviceLayout.PreferOnScreenGameControls)
            return true;
        var kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.isPressed || kb.wKey.isPressed || kb.upArrowKey.isPressed))
            return true;
        var gp = Gamepad.current;
        return gp != null && gp.buttonSouth.isPressed;
    }

    private static bool ReadJumpPressed()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame ||
                kb.wKey.wasPressedThisFrame ||
                kb.upArrowKey.wasPressedThisFrame)
                return true;
        }

        var gp = Gamepad.current;
        if (gp != null && gp.buttonSouth.wasPressedThisFrame)
            return true;

        return false;
    }

    private void ResolveHorizontalPlatforms(ref Vector2 pos)
    {
        float halfW = playerWidthGrid / 2f;
        float halfH = playerHeightGrid / 2f;

        float pxMin = pos.x - halfW;
        float pxMax = pos.x + halfW;
        float pyMin = pos.y - halfH;
        float pyMax = pos.y + halfH;

        // Basic AABB side resolution.
        foreach (var p in world.platforms)
        {
            bool overlapsY = pyMin < p.yMax && pyMax > p.yMin;
            if (!overlapsY)
                continue;

            bool overlapsX = pxMin < p.xMax && pxMax > p.xMin;
            if (!overlapsX)
                continue;

            if (velGrid.x > 0f)
            {
                pos.x = p.xMin - halfW;
            }
            else if (velGrid.x < 0f)
            {
                pos.x = p.xMax + halfW;
            }
        }
    }

    /// <summary>Land on platform tops when falling; bonk head when rising into a true overhead solid.</summary>
    private void ResolveVerticalPlatforms(Vector2 prevPos, ref Vector2 pos, ref bool groundedOut)
    {
        groundedOut = false;

        float halfW = playerWidthGrid / 2f;
        float halfH = playerHeightGrid / 2f;

        float prevBottom = prevPos.y - halfH;
        float prevTop = prevPos.y + halfH;
        float nextBottom = pos.y - halfH;
        float nextTop = pos.y + halfH;

        float pxMin = pos.x - halfW;
        float pxMax = pos.x + halfW;

        const float eps = 0.001f;
        // Graph levels use ~1-column × thin-slab platforms along the curve. The slab's bottom (yMin) sits
        // under the walkable top (yMax); jumping forward along rising steps hits that underside and used to
        // zero vy mid-air. Skip that "bonk" while the feet are still clearly below the step top.
        const float maxLedgeSlabThicknessGrid = 0.95f;
        const float maxLedgeSlabWidthGrid = 1.28f;
        const float feetBelowStepTopClearanceGrid = 0.1f;

        foreach (var p in world.platforms)
        {
            bool overlapsX = pxMin < p.xMax && pxMax > p.xMin;
            if (!overlapsX)
                continue;

            // Falling onto platform.
            if (velGrid.y <= 0f)
            {
                bool crossedTopSurface = prevBottom >= p.yMax - eps && nextBottom <= p.yMax + eps;
                if (crossedTopSurface)
                {
                    pos.y = p.yMax + halfH;
                    float impact = -velGrid.y;
                    coyoteTimer = coyoteSeconds; // touching ground (even mid-bounce) counts for jumping
                    LandedWithSpeed?.Invoke(impact);
                    if (impact > bounceVelocityCutoffGridPerSec && groundBounciness > 0f)
                    {
                        velGrid.y = impact * groundBounciness;
                        groundedOut = false;
                    }
                    else
                    {
                        velGrid.y = 0f;
                        groundedOut = true;
                    }
                    TriggerLandingSquash(impact);
                }
            }
            else // Rising — only bonk real ceilings, not the underside lip of the next stair tread.
            {
                float platW = p.xMax - p.xMin;
                float platH = p.yMax - p.yMin;
                bool thinStairSlab = platW <= maxLedgeSlabWidthGrid && platH <= maxLedgeSlabThicknessGrid;
                if (thinStairSlab && nextBottom < p.yMax - feetBelowStepTopClearanceGrid)
                    continue;

                bool crossedBottomSurface = prevTop <= p.yMin + eps && nextTop >= p.yMin - eps;
                if (crossedBottomSurface)
                {
                    pos.y = p.yMin - halfH;
                    velGrid.y = 0f;
                }
            }
        }
    }

    private bool OverlapsAny(List<GridRect> rects, Vector2 center)
    {
        float halfW = playerWidthGrid / 2f;
        float halfH = playerHeightGrid / 2f;
        float pxMin = center.x - halfW;
        float pxMax = center.x + halfW;
        float pyMin = center.y - halfH;
        float pyMax = center.y + halfH;

        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            bool overlapX = pxMin < r.xMax && pxMax > r.xMin;
            bool overlapY = pyMin < r.yMax && pyMax > r.yMin;
            if (overlapX && overlapY)
                return true;
        }

        return false;
    }

    private bool IsInFinishZone(Vector2 center)
    {
        // Finish check uses center X only (y doesn't matter; the goal is to reach the end).
        return center.x >= world.finish.xMin && center.x <= world.finish.xMax;
    }

    private void ApplyVisualPosition()
    {
        if (playerRect == null)
            return;

        float halfW = playerWidthGrid / 2f;
        float halfH = playerHeightGrid / 2f;

        // RectTransform anchoredPosition uses bottom-left when pivot=(0,0) and anchors=(0,0).
        float px = (posGrid.x - halfW) * unitWidth;
        float py = (posGrid.y - halfH) * unitHeight;
        playerRect.anchoredPosition = new Vector2(px, py);

        ApplySquashScale();
    }

    void TriggerLandingSquash(float downwardSpeed)
    {
        if (landingSquashSeconds <= 0f || landingSquashStrength <= 0f)
            return;
        float refSpeed = Mathf.Max(0.1f, bounceVelocityCutoffGridPerSec * 3f);
        float intensity = Mathf.Clamp01(downwardSpeed / refSpeed);
        GameFeel.RaiseLand(intensity);
        squashTimer = Mathf.Max(squashTimer, landingSquashSeconds * intensity);
    }

    void ApplySquashScale()
    {
        if (playerRect == null)
            return;

        if (stretchTimer > 0f)
        {
            stretchTimer = Mathf.Max(0f, stretchTimer - Time.deltaTime);
            float k = Mathf.Sin((1f - stretchTimer / StretchSeconds) * Mathf.PI);
            playerRect.localScale = new Vector3(1f - 0.12f * k, 1f + 0.18f * k, 1f);
            return;
        }
        if (squashTimer <= 0f || landingSquashSeconds <= 0f)
        {
            playerRect.localScale = Vector3.one;
            return;
        }

        squashTimer = Mathf.Max(0f, squashTimer - Time.deltaTime);
        float t = 1f - squashTimer / landingSquashSeconds;
        float curve = Mathf.Sin(t * Mathf.PI);
        float yScale = 1f - landingSquashStrength * curve;
        float xScale = 1f + landingSquashStrength * 0.55f * curve;
        playerRect.localScale = new Vector3(xScale, yScale, 1f);
    }

    private void Die()
    {
        if (isDead || isFinished)
            return;

        isDead = true;
        if (playerRect != null)
            GameFeel.RaiseDeath(playerRect.position);
        deathCallback?.Invoke();
    }
}

