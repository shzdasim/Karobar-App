// resources/js/components/GuideBotTrigger.jsx
import React, { useState, useEffect, useRef, useCallback } from "react";

const MASCOT_STYLE_KEY = "karobar_guide_bot_mascot_style";
const OPEN_STATE_KEY = "karobar_guide_bot_is_open";

/**
 * Shared persistent open-state hook so the Guide Bot stays open across all page
 * navigations (including standalone routes like Barcode POS).
 */
export function useGuideBotOpenState() {
  const [isOpen, setIsOpenState] = useState(() => {
    try {
      return sessionStorage.getItem(OPEN_STATE_KEY) === "1";
    } catch {
      return false;
    }
  });

  const setIsOpen = useCallback((nextVal) => {
    setIsOpenState((prev) => {
      const resolved = typeof nextVal === "function" ? nextVal(prev) : Boolean(nextVal);
      try {
        sessionStorage.setItem(OPEN_STATE_KEY, resolved ? "1" : "0");
      } catch {
        /* ignore */
      }
      window.dispatchEvent(
        new CustomEvent("guide-bot-open-state-changed", { detail: resolved })
      );
      return resolved;
    });
  }, []);

  useEffect(() => {
    const onSync = (e) => {
      if (typeof e?.detail === "boolean") {
        setIsOpenState(e.detail);
      }
    };
    window.addEventListener("guide-bot-open-state-changed", onSync);
    return () => window.removeEventListener("guide-bot-open-state-changed", onSync);
  }, []);

  return [isOpen, setIsOpen];
}

// Autonomous gaze + cute expression keyframes when cursor is idle
const IDLE_FRAMES = [
  { x: 0, y: 0, happyArcs: false, bigSmile: true },
  { x: -3.2, y: -0.5, happyArcs: false, bigSmile: true },
  { x: 3.2, y: -0.5, happyArcs: false, bigSmile: true },
  { x: 0, y: -0.4, happyArcs: true, bigSmile: true }, // Cute ^ ^ happy eye arcs
  { x: -2.2, y: 1.1, happyArcs: false, bigSmile: false },
  { x: 2.4, y: -1.1, happyArcs: false, bigSmile: true },
  { x: 0, y: 0, happyArcs: false, bigSmile: true },
];

// Presenter gaze frames when the big bot is on stage explaining the screen below
const PRESENTER_FRAMES = [
  { x: 0, y: 0, happyArcs: false, bigSmile: true },
  { x: -2.6, y: 1.6, happyArcs: false, bigSmile: true }, // Glancing down-left at explanation
  { x: 0, y: 0, happyArcs: true, bigSmile: true },      // Proud ^ ^ smile at user
  { x: 2.6, y: 1.6, happyArcs: false, bigSmile: true },  // Glancing down-right at explanation
  { x: -3.0, y: -0.4, happyArcs: false, bigSmile: true },
  { x: 3.0, y: -0.4, happyArcs: false, bigSmile: true },
  { x: 0, y: 0, happyArcs: false, bigSmile: true },
];

/**
 * Cube Mascot Avatar:
 * - `fullBody={false}`: Compact solid-black Cube head icon for the Topbar button.
 * - `fullBody={true}`: Full-Body Cube character with a matching solid-black geometric
 *   cube torso, glowing chest core/equalizer, articulated swinging/waving cube arms,
 *   and stepping white-soled cube boots for roaming at the bottom of the app & presenting.
 */
export function GuideBotAvatar({
  className = "w-9 h-8",
  interactive = true,
  forceStyle = null,
  speaking = false,
  presenter = false,
  fullBody = false,
  walking = false,
  walkDirection = "right",
  waving = false,
}) {
  const svgRef = useRef(null);
  const [gaze, setGaze] = useState({ x: 0, y: 0 });
  const [isBlinking, setIsBlinking] = useState(false);
  const [happyArcs, setHappyArcs] = useState(false);
  const [bigSmile, setBigSmile] = useState(true);
  const [talkPhase, setTalkPhase] = useState(0); // 0 = smile, 1 = open oval, 2 = wide D-talk
  const [isHovered, setIsHovered] = useState(false);
  const [mascotStyle, setMascotStyle] = useState(() => {
    try {
      return localStorage.getItem(MASCOT_STYLE_KEY) || "cube";
    } catch {
      return "cube";
    }
  });
  const lastMouseMoveRef = useRef(0);

  useEffect(() => {
    const onStyleChange = (e) => {
      if (e?.detail) setMascotStyle(e.detail);
    };
    window.addEventListener("guide-bot-mascot-changed", onStyleChange);
    return () => window.removeEventListener("guide-bot-mascot-changed", onStyleChange);
  }, []);

  // 1. Smooth Mouse Cursor Eye Tracking
  useEffect(() => {
    if (!interactive || typeof window === "undefined") return;

    let rafId = null;
    const handleMouseMove = (e) => {
      if (rafId) return;
      rafId = window.requestAnimationFrame(() => {
        rafId = null;
        const el = svgRef.current;
        if (!el) return;
        const rect = el.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) return;

        const centerX = rect.left + rect.width / 2;
        const centerY = rect.top + rect.height / 2;
        const dx = e.clientX - centerX;
        const dy = e.clientY - centerY;
        const dist = Math.hypot(dx, dy) || 1;

        const factor = Math.min(dist / 80, 1);
        const nx = (dx / dist) * 3.5 * factor;
        const ny = (dy / dist) * 2.0 * factor;

        lastMouseMoveRef.current = Date.now();
        setGaze({
          x: Number(nx.toFixed(2)),
          y: Number(ny.toFixed(2)),
        });
      });
    };

    window.addEventListener("mousemove", handleMouseMove, { passive: true });
    return () => {
      window.removeEventListener("mousemove", handleMouseMove);
      if (rafId) window.cancelAnimationFrame(rafId);
    };
  }, [interactive]);

  // 2. Autonomous Eye Glancing & Cute Expressions when cursor is idle
  useEffect(() => {
    if (typeof window === "undefined") return;
    let idx = 0;
    const frames = presenter ? PRESENTER_FRAMES : IDLE_FRAMES;

    const timer = window.setInterval(() => {
      const idleMs = Date.now() - lastMouseMoveRef.current;
      idx = (idx + 1) % frames.length;
      const frame = frames[idx];

      setHappyArcs(frame.happyArcs);
      setBigSmile(frame.bigSmile);

      if (idleMs > 1200) {
        if (walking) {
          setGaze({
            x: walkDirection === "left" ? -2.8 : 2.8,
            y: 0.2,
          });
        } else {
          setGaze({ x: frame.x, y: frame.y });
        }
      }
    }, presenter ? 1500 : 1900);

    return () => window.clearInterval(timer);
  }, [presenter, walking, walkDirection]);

  // Update gaze immediately when starting to walk while cursor is idle
  useEffect(() => {
    if (walking && Date.now() - lastMouseMoveRef.current > 900) {
      setGaze({
        x: walkDirection === "left" ? -2.8 : 2.8,
        y: 0.2,
      });
    }
  }, [walking, walkDirection]);

  // 3. Natural Periodic Eye Blinking
  useEffect(() => {
    if (typeof window === "undefined") return;
    let t1 = null;
    let t2 = null;
    let t3 = null;

    const triggerBlink = () => {
      setIsBlinking(true);
      t1 = window.setTimeout(() => {
        setIsBlinking(false);
        if (Math.random() < 0.3) {
          t2 = window.setTimeout(() => {
            setIsBlinking(true);
            t3 = window.setTimeout(() => setIsBlinking(false), 110);
          }, 130);
        }
      }, 125);
    };

    const blinkInterval = window.setInterval(triggerBlink, 3000);
    return () => {
      window.clearInterval(blinkInterval);
      if (t1) window.clearTimeout(t1);
      if (t2) window.clearTimeout(t2);
      if (t3) window.clearTimeout(t3);
    };
  }, []);

  // 4. Animated Talking Mouth when `speaking` is true
  useEffect(() => {
    if (!speaking || typeof window === "undefined") {
      setTalkPhase(0);
      return;
    }
    const talkTimer = window.setInterval(() => {
      setTalkPhase((prev) => (prev + 1) % 3);
    }, 170);
    return () => window.clearInterval(talkTimer);
  }, [speaking]);

  const activeStyle = forceStyle || mascotStyle;
  const showArcs = isHovered || happyArcs;
  const isWaving = waving || isHovered || speaking;

  // =========================================================================
  // FULL-BODY CUBE CHARACTER (for Bottom Roaming Companion & Popup Stage)
  // =========================================================================
  if (fullBody) {
    const leanDeg = walking ? (walkDirection === "left" ? -3.2 : 3.2) : 0;

    return (
      <svg
        ref={svgRef}
        viewBox="0 0 68 84"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        onMouseEnter={() => setIsHovered(true)}
        onMouseLeave={() => setIsHovered(false)}
        className={`guide-bot-avatar guide-bot-fullbody select-none ${
          walking ? "cube-is-walking" : "cube-is-idle"
        } ${isWaving ? "cube-is-waving" : ""} ${className}`}
        aria-hidden="true"
      >
        {/* Soft Ground Shadow */}
        <ellipse
          cx="34"
          cy="79.5"
          rx={walking ? "19" : "17"}
          ry="3.2"
          fill="rgba(15, 23, 42, 0.24)"
          className="cube-ground-shadow"
        />

        {/* =============================================================== */}
        {/* CUBE LEGS & WHITE-SOLED BOOTS (Alternating Walk Cycle)          */}
        {/* =============================================================== */}
        <g className="cube-legs-group">
          {/* Left Leg */}
          <g className="cube-leg cube-leg-left" style={{ transformOrigin: "26px 62px" }}>
            <rect
              x="21.5"
              y="60"
              width="9"
              height="13"
              rx="3.5"
              fill="#09090b"
              stroke="#27272a"
              strokeWidth="1.2"
            />
            {/* Left Boot */}
            <rect
              x="19.5"
              y="71"
              width="12"
              height="6"
              rx="3"
              fill="#09090b"
              stroke="#27272a"
              strokeWidth="1.1"
            />
            {/* Crisp White Sneaker Sole Accent */}
            <rect x="21" y="74.8" width="9" height="1.6" rx="0.8" fill="#ffffff" opacity="0.92" />
          </g>

          {/* Right Leg */}
          <g className="cube-leg cube-leg-right" style={{ transformOrigin: "42px 62px" }}>
            <rect
              x="37.5"
              y="60"
              width="9"
              height="13"
              rx="3.5"
              fill="#09090b"
              stroke="#27272a"
              strokeWidth="1.2"
            />
            {/* Right Boot */}
            <rect
              x="36.5"
              y="71"
              width="12"
              height="6"
              rx="3"
              fill="#09090b"
              stroke="#27272a"
              strokeWidth="1.1"
            />
            {/* Crisp White Sneaker Sole Accent */}
            <rect x="38" y="74.8" width="9" height="1.6" rx="0.8" fill="#ffffff" opacity="0.92" />
          </g>
        </g>

        {/* =============================================================== */}
        {/* UPPER RIG: TORSO + ARMS + SIGNATURE CUBE HEAD                   */}
        {/* =============================================================== */}
        <g
          className="cube-upper-rig"
          style={{
            transformOrigin: "34px 56px",
            transform: `rotate(${leanDeg}deg)`,
            transition: "transform 220ms cubic-bezier(0.22, 1, 0.36, 1)",
          }}
        >
          {/* Left Cube Arm */}
          <g className="cube-arm cube-arm-left" style={{ transformOrigin: "16px 45px" }}>
            <rect
              x="10"
              y="42.5"
              width="7.5"
              height="16.5"
              rx="3.75"
              fill="#09090b"
              stroke="#27272a"
              strokeWidth="1.2"
            />
            {/* Crisp White Wrist Cuff */}
            <rect x="11.5" y="54" width="4.5" height="2" rx="1" fill="#ffffff" opacity="0.9" />
          </g>

          {/* Right Cube Arm (Swings when walking, raises to wave when hovered/speaking) */}
          <g className="cube-arm cube-arm-right" style={{ transformOrigin: "52px 45px" }}>
            <rect
              x="50.5"
              y="42.5"
              width="7.5"
              height="16.5"
              rx="3.75"
              fill="#09090b"
              stroke="#27272a"
              strokeWidth="1.2"
            />
            {/* Crisp White Wrist Cuff */}
            <rect x="52" y="54" width="4.5" height="2" rx="1" fill="#ffffff" opacity="0.9" />
          </g>

          {/* Neck Joint */}
          <rect x="28" y="38" width="12" height="6" rx="2" fill="#18181b" />

          {/* Matching Rounded-Cube Torso */}
          <rect
            x="18.5"
            y="41.5"
            width="31"
            height="21.5"
            rx={activeStyle === "pixel" ? "3.5" : "6.5"}
            fill="#09090b"
            stroke="#27272a"
            strokeWidth="1.3"
          />

          {/* Recessed Chest Core Screen */}
          <rect
            x="23.5"
            y="45.5"
            width="21"
            height="13.5"
            rx={activeStyle === "pixel" ? "2" : "4"}
            fill="#18181b"
            stroke="#27272a"
            strokeWidth="1"
          />

          {/* Glowing Chest Equalizer / Core Bars */}
          <g className="cube-chest-core">
            <rect
              x="27.2"
              y={speaking && talkPhase === 1 ? "48.2" : "50.2"}
              width="3"
              height={speaking && talkPhase === 1 ? "8" : "4.2"}
              rx="1.5"
              fill="#38bdf8"
            />
            <rect
              x="32.5"
              y={speaking && talkPhase === 2 ? "47.5" : "48.5"}
              width="3"
              height={speaking && talkPhase === 2 ? "9.5" : "7.5"}
              rx="1.5"
              fill="#ffffff"
            />
            <rect
              x="37.8"
              y={speaking && talkPhase === 1 ? "48.8" : "50.2"}
              width="3"
              height={speaking && talkPhase === 1 ? "7" : "4.2"}
              rx="1.5"
              fill="#38bdf8"
            />
          </g>

          {/* ============================================================= */}
          {/* CUBE HEAD SILHOUETTE (shifted to center x=12..56, y=8..40)     */}
          {/* ============================================================= */}
          <g transform="translate(12, 8)">
            {activeStyle === "pixel" ? (
              <g fill="#09090b">
                <rect x="8" y="0" width="6" height="4" rx="1" />
                <rect x="30" y="0" width="6" height="4" rx="1" />
                <rect
                  x="4"
                  y="3"
                  width="36"
                  height="28"
                  rx="4"
                  stroke="#27272a"
                  strokeWidth="1.2"
                />
              </g>
            ) : activeStyle === "bot" ? (
              <g>
                <rect x="20.5" y="-1" width="3" height="5" rx="1.5" fill="#09090b" />
                <circle cx="22" cy="-1.5" r="2" fill="#38bdf8" />
                <rect x="1" y="12" width="3" height="10" rx="1.5" fill="#09090b" />
                <rect x="40" y="12" width="3" height="10" rx="1.5" fill="#09090b" />
                <rect
                  x="3.5"
                  y="3"
                  width="37"
                  height="29"
                  rx="11"
                  fill="#09090b"
                  stroke="#27272a"
                  strokeWidth="1.3"
                />
              </g>
            ) : (
              /* Default 'cube': Pure Solid Black Rounded Cube Head + Top Cube Node */
              <g>
                <rect
                  x="18.5"
                  y="-1.5"
                  width="7"
                  height="4.5"
                  rx="2"
                  fill="#09090b"
                  stroke="#27272a"
                  strokeWidth="1.1"
                />
                <circle cx="22" cy="0.5" r="1.2" fill="#ffffff" opacity="0.9" />
                <rect
                  x="2"
                  y="2"
                  width="40"
                  height="31"
                  rx="8.5"
                  fill="#09090b"
                  stroke="#27272a"
                  strokeWidth="1.3"
                />
              </g>
            )}

            {/* Moving Solid White Eyes & Smile */}
            <g
              style={{
                transform: `translate(${gaze.x}px, ${gaze.y}px)`,
                transition: "transform 180ms cubic-bezier(0.22, 1, 0.36, 1)",
              }}
            >
              <g
                style={{
                  transformOrigin: "22px 15px",
                  transform: isBlinking ? "scaleY(0.08)" : "scaleY(1)",
                  transition: "transform 110ms ease-in-out",
                }}
              >
                {showArcs ? (
                  <g>
                    <path
                      d="M 11.5 16.5 Q 15.2 11.2 19.0 16.5"
                      stroke="#ffffff"
                      strokeWidth="2.8"
                      strokeLinecap="round"
                      fill="none"
                    />
                    <path
                      d="M 25.0 16.5 Q 28.8 11.2 32.5 16.5"
                      stroke="#ffffff"
                      strokeWidth="2.8"
                      strokeLinecap="round"
                      fill="none"
                    />
                  </g>
                ) : (
                  <g>
                    <rect
                      x="11.8"
                      y="10"
                      width="6.4"
                      height="9.6"
                      rx={activeStyle === "pixel" ? "1.5" : "3.2"}
                      fill="#ffffff"
                    />
                    <rect
                      x="25.8"
                      y="10"
                      width="6.4"
                      height="9.6"
                      rx={activeStyle === "pixel" ? "1.5" : "3.2"}
                      fill="#ffffff"
                    />
                  </g>
                )}
              </g>

              {speaking && talkPhase === 1 ? (
                <path d="M 17.5 22.8 Q 22 28.8 26.5 22.8 Z" fill="#ffffff" />
              ) : speaking && talkPhase === 2 ? (
                <rect
                  x="18.8"
                  y="22.4"
                  width="6.4"
                  height="4.0"
                  rx="2.0"
                  fill="#ffffff"
                />
              ) : (
                <path
                  d={
                    bigSmile || isHovered
                      ? "M 17.5 23.2 Q 22 27.6 26.5 23.2"
                      : "M 18.2 23.5 Q 22 26.5 25.8 23.5"
                  }
                  stroke="#ffffff"
                  strokeWidth="2.4"
                  strokeLinecap="round"
                  fill="none"
                />
              )}
            </g>
          </g>
        </g>
      </svg>
    );
  }

  // =========================================================================
  // COMPACT HEAD ICON (for Topbar Button)
  // =========================================================================
  return (
    <svg
      ref={svgRef}
      viewBox="0 0 44 36"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      className={`guide-bot-avatar select-none ${className}`}
      aria-hidden="true"
    >
      {activeStyle === "pixel" ? (
        <g fill="#09090b">
          <rect x="8" y="1" width="6" height="4" rx="1" />
          <rect x="30" y="1" width="6" height="4" rx="1" />
          <rect x="4" y="4" width="36" height="26" rx="4" stroke="#27272a" strokeWidth="1" />
          <rect x="10" y="30" width="6" height="4" rx="1" />
          <rect x="28" y="30" width="6" height="4" rx="1" />
        </g>
      ) : activeStyle === "bot" ? (
        <g>
          <rect x="20.5" y="1" width="3" height="4" rx="1.5" fill="#09090b" />
          <rect x="1" y="13" width="3" height="10" rx="1.5" fill="#09090b" />
          <rect x="40" y="13" width="3" height="10" rx="1.5" fill="#09090b" />
          <rect
            x="3.5"
            y="4"
            width="37"
            height="29"
            rx="11"
            fill="#09090b"
            stroke="#27272a"
            strokeWidth="1.2"
          />
        </g>
      ) : (
        <rect
          x="2"
          y="2.5"
          width="40"
          height="31"
          rx="8"
          fill="#09090b"
          stroke="#27272a"
          strokeWidth="1.2"
        />
      )}

      <g
        style={{
          transform: `translate(${gaze.x}px, ${gaze.y}px)`,
          transition: "transform 180ms cubic-bezier(0.22, 1, 0.36, 1)",
        }}
      >
        <g
          style={{
            transformOrigin: "22px 15px",
            transform: isBlinking ? "scaleY(0.08)" : "scaleY(1)",
            transition: "transform 110ms ease-in-out",
          }}
        >
          {showArcs ? (
            <g>
              <path
                d="M 11.5 16.5 Q 15.2 11.2 19.0 16.5"
                stroke="#ffffff"
                strokeWidth="2.8"
                strokeLinecap="round"
                fill="none"
              />
              <path
                d="M 25.0 16.5 Q 28.8 11.2 32.5 16.5"
                stroke="#ffffff"
                strokeWidth="2.8"
                strokeLinecap="round"
                fill="none"
              />
            </g>
          ) : (
            <g>
              <rect
                x="11.8"
                y="10"
                width="6.4"
                height="9.6"
                rx={activeStyle === "pixel" ? "1.5" : "3.2"}
                fill="#ffffff"
              />
              <rect
                x="25.8"
                y="10"
                width="6.4"
                height="9.6"
                rx={activeStyle === "pixel" ? "1.5" : "3.2"}
                fill="#ffffff"
              />
            </g>
          )}
        </g>

        {speaking && talkPhase === 1 ? (
          <path d="M 17.5 22.8 Q 22 28.8 26.5 22.8 Z" fill="#ffffff" />
        ) : speaking && talkPhase === 2 ? (
          <rect
            x="18.8"
            y="22.4"
            width="6.4"
            height="4.0"
            rx="2.0"
            fill="#ffffff"
          />
        ) : (
          <path
            d={
              bigSmile || isHovered
                ? "M 17.5 23.2 Q 22 27.6 26.5 23.2"
                : "M 18.2 23.5 Q 22 26.5 25.8 23.5"
            }
            stroke="#ffffff"
            strokeWidth="2.4"
            strokeLinecap="round"
            fill="none"
          />
        )}
      </g>
    </svg>
  );
}

export function setGuideBotMascotStyle(style) {
  try {
    localStorage.setItem(MASCOT_STYLE_KEY, style);
  } catch {
    /* ignore */
  }
  window.dispatchEvent(new CustomEvent("guide-bot-mascot-changed", { detail: style }));
}

export function getGuideBotMascotStyle() {
  try {
    return localStorage.getItem(MASCOT_STYLE_KEY) || "cube";
  } catch {
    return "cube";
  }
}

/**
 * Top Navigation Trigger Button for Guide Bot (English only, no Urdu).
 */
export function GuideBotTopbarButton({
  onClick,
  isOpen = false,
  compact = false,
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-haspopup="dialog"
      aria-expanded={isOpen}
      aria-keyshortcuts="Alt+G"
      title="Guide Bot (Alt+G)"
      className="group relative inline-flex items-center gap-2 h-10 px-2.5 rounded-xl border border-slate-200 dark:border-slate-700/80 bg-white hover:bg-slate-50 dark:bg-slate-900 dark:hover:bg-slate-800/90 text-slate-900 dark:text-slate-100 transition-all duration-150 cursor-pointer shrink-0 whitespace-nowrap shadow-2xs hover:shadow-sm active:scale-[0.98]"
    >
      {/* Cube Mascot Icon */}
      <span className="inline-flex items-center justify-center shrink-0 transition-transform duration-200 group-hover:scale-105">
        <GuideBotAvatar className="w-8 h-[26px]" speaking={isOpen} fullBody={false} />
      </span>

      {/* English-Only Label */}
      {!compact && (
        <span className="hidden sm:inline text-xs font-extrabold tracking-tight">
          Guide Bot
        </span>
      )}

      <kbd className="hidden 2xl:inline-block px-1.5 py-0.5 text-[0.625rem] font-mono font-semibold rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-700 shrink-0">
        Alt+G
      </kbd>
    </button>
  );
}
