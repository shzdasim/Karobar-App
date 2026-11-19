using System;
using System.Collections.Generic;

namespace Karobar.Host.Mascot;

/// <summary>
/// Cross-platform autonomous animation, roaming, eye-tracking, blink, sleep/wake, and
/// focus-collision-avoidance FSM for the bottom Cube mascot (1-to-1 parity with `RoamingCubeCompanion`).
/// </summary>
public sealed class MascotRoamingController
{
    public static readonly MascotTrick[] StationaryTricks =
    [
        MascotTrick.Rubiks,
        MascotTrick.Headphones,
        MascotTrick.Backflip,
        MascotTrick.Dance,
        MascotTrick.Juggle,
        MascotTrick.Shades,
        MascotTrick.Charge,
        MascotTrick.Scan
    ];

    public static readonly IReadOnlyDictionary<MascotTrick, string> TrickBadges = new Dictionary<MascotTrick, string>
    {
        [MascotTrick.Rubiks] = "Solving Cube",
        [MascotTrick.Headphones] = "Vibing",
        [MascotTrick.Backflip] = "Backflip!",
        [MascotTrick.Dance] = "Grooving",
        [MascotTrick.Juggle] = "Juggling",
        [MascotTrick.Shades] = "Deal With It",
        [MascotTrick.Charge] = "100% Charged",
        [MascotTrick.Scan] = "Scanning...",
        [MascotTrick.Hoverboard] = "Hover-Glide",
        [MascotTrick.Walk] = "Patrolling",
        [MascotTrick.Sleep] = "Resting...",
        [MascotTrick.Wake] = "Awake!",
        [MascotTrick.Spy] = "Inspecting"
    };

    private readonly Random _rng;
    private double _stateTimerSec;
    private double _stateDurationSec = 3.6;
    private double _idleSec;
    private double _blinkTimerSec;
    private bool _pendingDoubleBlink;
    private double _doubleBlinkGapSec;
    private int _stepCounter;
    private int _lastTrickIndex = -1;
    private double _moveStartX;
    private double _moveDurationSec;
    private double _moveElapsedSec;
    private double _lastCursorMoveSec = 10.0;
    private int _lastCursorX = int.MinValue;
    private int _lastCursorY = int.MinValue;

    public const double IdleSleepThresholdSec = 45.0;

    public double CurrentX { get; private set; } = 360;
    public double TargetX { get; private set; } = 360;
    public bool FacingRight { get; private set; } = true;
    public MascotTrick ActiveTrick { get; private set; } = MascotTrick.None;
    public bool IsHovered { get; set; }
    public bool IsSpeaking { get; set; }
    public bool RoamingEnabled { get; set; } = true;
    public bool IsSleeping => ActiveTrick == MascotTrick.Sleep;
    public bool IsMoving => ActiveTrick is MascotTrick.Walk or MascotTrick.Hoverboard;
    public bool IsVanishing { get; private set; }
    public double VanishProgress { get; private set; }

    public double AnimTimeSec { get; private set; }
    public double TrickElapsedSec => _stateTimerSec;
    public float EyeOffsetX { get; private set; }
    public float EyeOffsetY { get; private set; }
    public bool IsBlinking { get; private set; }
    public int TalkPhase => IsSpeaking ? ((int)(AnimTimeSec / 0.22) % 3) : 0;

    public int SidebarLeftMargin { get; set; } = 240;
    public int RightReservedWidth { get; set; } = 24;
    public string? BackgroundTaskStatusText { get; set; }

    public MascotRoamingController(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public string GetPillBadgeText()
    {
        if (IsHovered) return "Ask Guide Bot";
        if (!string.IsNullOrWhiteSpace(BackgroundTaskStatusText)) return BackgroundTaskStatusText!;
        return TrickBadges.TryGetValue(ActiveTrick, out var badge) ? badge : "";
    }

    public void NotifyUserActivity()
    {
        _idleSec = 0;
        if (ActiveTrick == MascotTrick.Sleep)
        {
            ActiveTrick = MascotTrick.Wake;
            _stateTimerSec = 0;
            _stateDurationSec = 0.48;
        }
    }

    public void TriggerVanishClick()
    {
        IsVanishing = true;
        VanishProgress = 0;
    }

    public void ResetAfterDrawerClosed()
    {
        IsVanishing = false;
        VanishProgress = 0;
        _idleSec = 0;
        ActiveTrick = MascotTrick.Backflip;
        _stateTimerSec = 0;
        _stateDurationSec = 1.6;
    }

    public void AvoidFocusedRect(int rectLeft, int rectTop, int rectRight, int rectBottom, int viewportWidth, int viewportHeight)
    {
        if (IsHovered || IsVanishing || !RoamingEnabled) return;
        // Bottom strip is bottom 136px of client area
        if (rectBottom < viewportHeight - 140) return;

        var mascotLeft = CurrentX;
        var mascotRight = CurrentX + 116;
        if (rectRight < mascotLeft - 24 || rectLeft > mascotRight + 24) return;

        var minX = Math.Max(16, SidebarLeftMargin + 12);
        var maxX = Math.Max(minX + 40, viewportWidth - RightReservedWidth - 128);
        double newTarget;
        if (CurrentX + 180 <= maxX)
        {
            newTarget = Math.Min(maxX, CurrentX + 180);
        }
        else if (CurrentX - 180 >= minX)
        {
            newTarget = Math.Max(minX, CurrentX - 180);
        }
        else
        {
            newTarget = CurrentX > (minX + maxX) / 2.0 ? minX : maxX;
        }

        StartMove(newTarget, forceFast: true);
    }

    public void ClampToViewport(int viewportWidth)
    {
        var minX = Math.Max(16, SidebarLeftMargin + 12);
        var maxX = Math.Max(minX + 20, viewportWidth - RightReservedWidth - 128);
        if (CurrentX < minX) CurrentX = minX;
        if (CurrentX > maxX) CurrentX = maxX;
        if (TargetX < minX) TargetX = minX;
        if (TargetX > maxX) TargetX = maxX;
    }

    public void Tick(double dtSec, int cursorClientX, int cursorClientY, int viewportWidth, int viewportHeight)
    {
        if (dtSec <= 0 || dtSec > 0.5) dtSec = 0.033;
        AnimTimeSec += dtSec;

        ClampToViewport(viewportWidth);

        if (IsVanishing)
        {
            VanishProgress = Math.Min(1.0, VanishProgress + dtSec / 0.14);
            return;
        }

        // 1. Blink cadence (4.2s with 30% double-blink chance)
        UpdateBlink(dtSec);

        // 2. Live cursor eye-tracking vs autonomous gaze
        UpdateEyeTracking(dtSec, cursorClientX, cursorClientY, viewportHeight);

        // 3. Hover pauses FSM
        if (IsHovered)
        {
            _idleSec = 0;
            if (ActiveTrick == MascotTrick.Sleep)
            {
                ActiveTrick = MascotTrick.None;
            }
            return;
        }

        // 4. Idle sleep after 45s
        _idleSec += dtSec;
        if (_idleSec >= IdleSleepThresholdSec && ActiveTrick != MascotTrick.Sleep)
        {
            ActiveTrick = MascotTrick.Sleep;
            _stateTimerSec = 0;
            _stateDurationSec = 9999;
            return;
        }

        _stateTimerSec += dtSec;

        // 5. Wake -> celebratory Backflip transition
        if (ActiveTrick == MascotTrick.Wake)
        {
            if (_stateTimerSec >= _stateDurationSec)
            {
                ActiveTrick = MascotTrick.Backflip;
                _stateTimerSec = 0;
                _stateDurationSec = 1.65;
            }
            return;
        }

        if (ActiveTrick == MascotTrick.Sleep) return;

        // 6. Active horizontal move interpolation
        if (IsMoving)
        {
            _moveElapsedSec += dtSec;
            var t = _moveDurationSec > 0.01 ? Math.Clamp(_moveElapsedSec / _moveDurationSec, 0.0, 1.0) : 1.0;
            // Smooth ease-in-out
            var eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            CurrentX = _moveStartX + (TargetX - _moveStartX) * eased;
            if (t >= 1.0)
            {
                CurrentX = TargetX;
                ActiveTrick = MascotTrick.None;
                _stateTimerSec = 0;
                _stateDurationSec = 1.4;
            }
            return;
        }

        // 7. FSM transition: alternate 2 stationary tricks with 1 horizontal move
        if (_stateTimerSec >= _stateDurationSec)
        {
            _stepCounter++;
            if (RoamingEnabled && _stepCounter % 3 == 0 && viewportWidth >= 520)
            {
                PickNextMoveSlot(viewportWidth);
            }
            else
            {
                PickNextStationaryTrick();
            }
        }
    }

    private void PickNextStationaryTrick()
    {
        var idx = _rng.Next(StationaryTricks.Length);
        if (idx == _lastTrickIndex) idx = (idx + 1) % StationaryTricks.Length;
        _lastTrickIndex = idx;
        ActiveTrick = StationaryTricks[idx];
        _stateTimerSec = 0;
        _stateDurationSec = ActiveTrick == MascotTrick.Backflip ? 1.7 : (3.4 + _rng.NextDouble() * 2.4);
    }

    private void PickNextMoveSlot(int viewportWidth)
    {
        var minX = Math.Max(16, SidebarLeftMargin + 12);
        var maxX = Math.Max(minX + 40, viewportWidth - RightReservedWidth - 128);
        var slots = new List<double>();
        for (double x = minX; x <= maxX; x += 36)
        {
            if (Math.Abs(x - CurrentX) >= 60) slots.Add(x);
        }
        if (slots.Count == 0)
        {
            PickNextStationaryTrick();
            return;
        }
        var chosen = slots[_rng.Next(slots.Count)];
        StartMove(chosen, forceFast: false);
    }

    private void StartMove(double targetX, bool forceFast)
    {
        _moveStartX = CurrentX;
        TargetX = targetX;
        FacingRight = targetX >= CurrentX;
        var dist = Math.Abs(targetX - CurrentX);
        var useHoverboard = !forceFast && dist >= 140 && _rng.NextDouble() < 0.55;
        ActiveTrick = useHoverboard ? MascotTrick.Hoverboard : MascotTrick.Walk;
        var speedPxPerSec = forceFast ? 380.0 : (useHoverboard ? 115.0 : 75.0);
        _moveDurationSec = Math.Max(0.35, dist / speedPxPerSec);
        _moveElapsedSec = 0;
        _stateTimerSec = 0;
        _stateDurationSec = _moveDurationSec + 0.1;
    }

    private void UpdateBlink(double dtSec)
    {
        if (ActiveTrick == MascotTrick.Sleep)
        {
            IsBlinking = false;
            return;
        }

        _blinkTimerSec += dtSec;
        if (_pendingDoubleBlink)
        {
            _doubleBlinkGapSec += dtSec;
            if (_doubleBlinkGapSec is >= 0.13 and <= 0.24)
            {
                IsBlinking = true;
            }
            else if (_doubleBlinkGapSec > 0.24)
            {
                IsBlinking = false;
                _pendingDoubleBlink = false;
                _blinkTimerSec = 0;
            }
            return;
        }

        if (_blinkTimerSec >= 4.2 && _blinkTimerSec <= 4.325)
        {
            IsBlinking = true;
        }
        else if (_blinkTimerSec > 4.325)
        {
            IsBlinking = false;
            if (_rng.NextDouble() < 0.30)
            {
                _pendingDoubleBlink = true;
                _doubleBlinkGapSec = 0;
            }
            else
            {
                _blinkTimerSec = 0;
            }
        }
    }

    private void UpdateEyeTracking(double dtSec, int cursorClientX, int cursorClientY, int viewportHeight)
    {
        if (cursorClientX != _lastCursorX || cursorClientY != _lastCursorY)
        {
            _lastCursorX = cursorClientX;
            _lastCursorY = cursorClientY;
            _lastCursorMoveSec = 0;
        }
        else
        {
            _lastCursorMoveSec += dtSec;
        }

        float targetEx, targetEy;
        if (ActiveTrick == MascotTrick.Sleep)
        {
            targetEx = 0;
            targetEy = 1.2f;
        }
        else if (IsMoving)
        {
            targetEx = FacingRight ? 2.6f : -2.6f;
            targetEy = -0.3f;
        }
        else if (_lastCursorMoveSec < 1.2 && cursorClientX >= 0 && cursorClientY >= 0)
        {
            var headCenterX = (float)(CurrentX + 64);
            var headCenterY = viewportHeight - 62f;
            var dx = cursorClientX - headCenterX;
            var dy = cursorClientY - headCenterY;
            var dist = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
            targetEx = Math.Clamp((dx / dist) * 3.5f, -3.5f, 3.5f);
            targetEy = Math.Clamp((dy / dist) * 2.0f, -2.0f, 2.0f);
        }
        else
        {
            // Autonomous idle gaze
            var phase = (int)(AnimTimeSec / 2.8) % 5;
            (targetEx, targetEy) = phase switch
            {
                1 => (2.0f, -0.6f),
                2 => (-2.2f, -0.3f),
                3 => (0.5f, -1.4f),
                4 => (-1.2f, 0.8f),
                _ => (0f, 0f)
            };
        }

        var lerp = (float)Math.Min(1.0, dtSec * 10.0);
        EyeOffsetX += (targetEx - EyeOffsetX) * lerp;
        EyeOffsetY += (targetEy - EyeOffsetY) * lerp;
    }
}
