// resources/js/components/FeatureGuideBot.jsx
import React, { useState, useEffect, useRef, useMemo, useId, useCallback } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  SparklesIcon,
  XMarkIcon,
  Squares2X2Icon,
  CheckCircleIcon,
  BoltIcon,
  CommandLineIcon,
  ArrowTopRightOnSquareIcon,
  MagnifyingGlassIcon,
  PaperAirplaneIcon,
  ArrowPathIcon,
  QuestionMarkCircleIcon,
  ArrowsPointingOutIcon,
  ArrowsPointingInIcon,
  ChevronLeftIcon,
} from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext.jsx";
import {
  GuideBotAvatar,
  getGuideBotMascotStyle,
  setGuideBotMascotStyle,
  useGuideBotOpenState,
} from "./GuideBotTrigger.jsx";
import {
  SCREEN_GUIDES,
  MODULE_CATEGORIES,
  resolveScreenGuide,
  answerUserQuestion,
  routeUserQuery,
  DEMO_MANIFESTS,
  PRODUCT_VERSION,
} from "@/data/guideBotKnowledge.js";

const LANG_STORAGE_KEY = "karobar_guide_bot_lang";

// Feature-detect discrete display transitions (guards against Firefox bug 1882408)
let supportsDisplayTransition;
function canTransitionDisplay() {
  if (supportsDisplayTransition !== undefined) return supportsDisplayTransition;
  if (
    typeof window === "undefined" ||
    !window.CSS?.supports?.("transition-behavior", "allow-discrete") ||
    !document.body
  ) {
    return false;
  }
  const probe = document.createElement("div");
  probe.style.cssText = "transition: display 1s allow-discrete !important; display: block;";
  document.body.appendChild(probe);
  window.getComputedStyle(probe).display;
  probe.style.display = "none";
  supportsDisplayTransition = window.getComputedStyle(probe).display === "block";
  probe.remove();
  return supportsDisplayTransition;
}

function supportsTopLayerExit() {
  return (
    typeof window !== "undefined" &&
    Boolean(window.CSS?.supports?.("overlay", "auto")) &&
    canTransitionDisplay()
  );
}

/**
 * Renders inline markdown (**bold** and `code`) cleanly without raw asterisks.
 */
function renderInlineFormatting(line, keyPrefix = "ln") {
  if (!line) return null;
  const parts = line.split(/(\*\*[^*]+\*\*|`[^`]+`)/g);
  return parts.map((part, idx) => {
    if (part.startsWith("**") && part.endsWith("**") && part.length > 4) {
      return (
        <strong
          key={`${keyPrefix}-${idx}`}
          className="font-extrabold text-slate-900 dark:text-white"
        >
          {part.slice(2, -2)}
        </strong>
      );
    }
    if (part.startsWith("`") && part.endsWith("`") && part.length > 2) {
      return (
        <code
          key={`${keyPrefix}-${idx}`}
          className="px-1.5 py-0.5 mx-0.5 text-[11px] font-mono font-bold rounded bg-slate-200/80 dark:bg-slate-800 text-blue-700 dark:text-blue-300"
        >
          {part.slice(1, -1)}
        </code>
      );
    }
    return <React.Fragment key={`${keyPrefix}-${idx}`}>{part}</React.Fragment>;
  });
}

function FormattedBotMessage({ text, isUrdu = false }) {
  if (!text) return null;
  const lines = text.split("\n");
  return (
    <div
      lang={isUrdu ? "ur" : "en"}
      dir={isUrdu ? "rtl" : "ltr"}
      className={`space-y-1.5 text-[13.5px] ${
        isUrdu ? "guide-bot-urdu-block" : "guide-bot-body-en"
      } text-slate-700 dark:text-slate-200`}
    >
      {lines.map((line, i) => {
        const trimmed = line.trim();
        if (!trimmed) return <div key={i} className="h-1" />;
        return (
          <p key={i} className="leading-relaxed">
            {renderInlineFormatting(line, `p-${i}`)}
          </p>
        );
      })}
    </div>
  );
}

const INTERACTIVE_OBSTACLE_SELECTOR =
  'input, select, textarea, button, [role="button"], [role="combobox"], [contenteditable="true"], .react-select__control';

// Stationary tricks performed along the bottom
const STATIONARY_TRICKS = [
  { id: "backflip", durationMs: 2600, badge: "360° Flip! 🤸" },
  { id: "dance", durationMs: 3400, badge: "Robot Boogie ♪" },
  { id: "juggle", durationMs: 3200, badge: "Cube Juggle 🎲" },
  { id: "shades", durationMs: 3000, badge: "Cool Shades 🕶️" },
  { id: "charge", durationMs: 3000, badge: "Supercharge ⚡" },
  { id: "scan", durationMs: 2800, badge: "Screen Scan 🔍" },
];

const TRICK_BADGES = {
  backflip: "360° Flip! 🤸",
  dance: "Robot Boogie ♪",
  juggle: "Cube Juggle 🎲",
  shades: "Cool Shades 🕶️",
  charge: "Supercharge ⚡",
  scan: "Screen Scan 🔍",
  hoverboard: "Hover-Glide 🛹",
  sleep: "Resting... 💤",
  wake: "Ready! ⚡",
};

/**
 * Bottom Free-Roaming Full-Body Cube Companion (`RoamingCubeCompanion`):
 * - Roams freely along the bottom of the app with a cute walking cycle, full Cube body,
 *   and an autonomous Finite State Machine that performs 8 entertaining tricks:
 *   ('backflip', 'dance', 'hoverboard', 'juggle', 'shades', 'charge', 'scan', 'sleep' + 'wake').
 * - Never runs away from the user's cursor so it is always easy to watch and click.
 * - Avoids covering fields/buttons by picking open floor slots and stepping aside only if
 *   an overlapping input/control is focused.
 * - When clicked, opens the Guide Bot popup and disappears ("goes away") while the popup is open.
 */
function RoamingCubeCompanion({ onOpen, screenTitle }) {
  const mascotBtnRef = useRef(null);
  const stateRef = useRef({
    x: 320,
    bottomOffset: 6,
    isWalking: false,
    walkDirection: "left",
    moveDurationMs: 0,
    activeTrick: "dance",
    isVanishing: false,
    isHovered: false,
  });
  const roamTimerRef = useRef(null);
  const actionEndTimerRef = useRef(null);
  const lastTrickRef = useRef("dance");
  const lastUserActivityRef = useRef(Date.now());
  const stepCounterRef = useRef(0);

  const [state, setState] = useState(() => {
    const startX =
      typeof window !== "undefined"
        ? Math.max(140, Math.min(window.innerWidth - 140, Math.round(window.innerWidth * 0.56)))
        : 320;
    const initial = {
      x: startX,
      bottomOffset: 6,
      isWalking: false,
      walkDirection: "left",
      moveDurationMs: 0,
      activeTrick: "dance",
      isVanishing: false,
      isHovered: false,
    };
    stateRef.current = initial;
    return initial;
  });

  const updateState = useCallback((patch) => {
    setState((prev) => {
      const next = typeof patch === "function" ? patch(prev) : { ...prev, ...patch };
      stateRef.current = next;
      return next;
    });
  }, []);

  /**
   * Picks a random stationary trick without repeating the previous trick back-to-back.
   */
  const pickNextTrick = useCallback(() => {
    const candidates = STATIONARY_TRICKS.filter((t) => t.id !== lastTrickRef.current);
    const chosen =
      candidates[Math.floor(Math.random() * candidates.length)] || STATIONARY_TRICKS[0];
    lastTrickRef.current = chosen.id;
    return chosen;
  }, []);

  /**
   * Scans the viewport bottom to find safe X coordinates (and bottomOffset) that do not
   * cover any visible button, input, select, or textarea.
   */
  const findSafeSpot = useCallback((avoidX = null, currentX = stateRef.current.x) => {
    if (typeof window === "undefined" || typeof document === "undefined") {
      return { x: 320, bottomOffset: 6 };
    }

    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const cubeW = 68;
    const cubeH = 78;

    // Respect left sidebar if present
    let minX = 28;
    const sidebarEl = document.querySelector(".workspace-sidebar");
    if (sidebarEl) {
      const sbRect = sidebarEl.getBoundingClientRect();
      if (sbRect.width > 0 && sbRect.right > 0 && sbRect.left >= 0 && sbRect.right < vw * 0.5) {
        minX = Math.max(28, Math.ceil(sbRect.right + 20));
      }
    }
    const maxX = Math.max(minX + 40, vw - cubeW - 28);

    // Collect visible interactive elements in the lower 160px of the viewport
    const allNodes = Array.from(document.querySelectorAll(INTERACTIVE_OBSTACLE_SELECTOR));
    const obstacles = [];
    for (let i = 0; i < allNodes.length; i++) {
      const el = allNodes[i];
      if (el.closest("[data-roaming-cube]")) continue;
      const r = el.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0 || r.width > vw * 0.85) continue;
      if (r.bottom < vh - 160 || r.top > vh) continue;
      obstacles.push(r);
    }

    const candidateBottomOffsets = [6, 62];
    for (const bOffset of candidateBottomOffsets) {
      const boxTop = vh - bOffset - cubeH;
      const boxBottom = vh - bOffset;
      const safeSlots = [];

      for (let candX = minX; candX <= maxX; candX += 32) {
        const boxLeft = candX - 8;
        const boxRight = candX + cubeW + 8;

        const hitsObstacle = obstacles.some(
          (r) =>
            r.right >= boxLeft &&
            r.left <= boxRight &&
            r.bottom >= boxTop &&
            r.top <= boxBottom
        );

        if (!hitsObstacle) {
          safeSlots.push(candX);
        }
      }

      if (safeSlots.length > 0) {
        if (typeof avoidX === "number") {
          const farSlots = safeSlots.filter((sx) => Math.abs(sx + cubeW / 2 - avoidX) >= 140);
          if (farSlots.length > 0) {
            farSlots.sort(
              (a, b) => Math.abs(b + cubeW / 2 - avoidX) - Math.abs(a + cubeW / 2 - avoidX)
            );
            return { x: farSlots[0], bottomOffset: bOffset };
          }
        }

        const strollSlots = safeSlots.filter(
          (sx) => Math.abs(sx - currentX) >= 90 && Math.abs(sx - currentX) <= 360
        );
        const pool = strollSlots.length > 0 ? strollSlots : safeSlots;
        const chosen = pool[Math.floor(Math.random() * pool.length)];
        return { x: chosen, bottomOffset: bOffset };
      }
    }

    const fallbackX =
      typeof avoidX === "number"
        ? avoidX > (minX + maxX) / 2
          ? minX
          : maxX
        : Math.round((minX + maxX) * 0.62);
    return { x: fallbackX, bottomOffset: 6 };
  }, []);

  // 1. Continuous, Unbreakable Trick & Strolling State Machine Loop
  useEffect(() => {
    if (typeof window === "undefined") return;
    let isCancelled = false;

    const clearTimers = () => {
      if (roamTimerRef.current) window.clearTimeout(roamTimerRef.current);
      if (actionEndTimerRef.current) window.clearTimeout(actionEndTimerRef.current);
    };

    const runCycle = (delayMs = 900) => {
      if (isCancelled) return;
      if (roamTimerRef.current) window.clearTimeout(roamTimerRef.current);

      roamTimerRef.current = window.setTimeout(() => {
        if (isCancelled) return;

        if (typeof document !== "undefined" && document.hidden) {
          runCycle(2500);
          return;
        }

        const cur = stateRef.current;
        if (cur.isVanishing) return;

        // If user is hovering Cube right now, wait briefly and check again
        if (cur.isHovered) {
          runCycle(1200);
          return;
        }

        // Check if user has been inactive for > 45s -> enter cozy nap mode
        const inactiveMs = Date.now() - lastUserActivityRef.current;
        if (inactiveMs > 45000) {
          if (cur.activeTrick !== "sleep") {
            updateState({
              isWalking: false,
              moveDurationMs: 0,
              activeTrick: "sleep",
            });
          }
          runCycle(3000);
          return;
        }

        stepCounterRef.current += 1;
        // Perform 2 tricks for every 1 stroll so Cube is always entertaining!
        const doStationaryTrick = stepCounterRef.current % 3 !== 0;

        if (doStationaryTrick) {
          const trickSpec = pickNextTrick();
          updateState({
            isWalking: false,
            moveDurationMs: 0,
            activeTrick: trickSpec.id,
          });

          if (actionEndTimerRef.current) window.clearTimeout(actionEndTimerRef.current);
          actionEndTimerRef.current = window.setTimeout(() => {
            if (isCancelled) return;
            updateState((prev) =>
              prev.activeTrick === trickSpec.id ? { ...prev, activeTrick: null } : prev
            );
            runCycle(900);
          }, trickSpec.durationMs);
          return;
        }

        // Stroll (or glide on neon hoverboard!) to a new safe spot along the bottom
        const nextSpot = findSafeSpot(null, cur.x);
        const dist = Math.abs(nextSpot.x - cur.x);

        if (dist < 30 && nextSpot.bottomOffset === cur.bottomOffset) {
          const trickSpec = pickNextTrick();
          updateState({
            isWalking: false,
            moveDurationMs: 0,
            activeTrick: trickSpec.id,
          });
          if (actionEndTimerRef.current) window.clearTimeout(actionEndTimerRef.current);
          actionEndTimerRef.current = window.setTimeout(() => {
            if (isCancelled) return;
            updateState({ activeTrick: null });
            runCycle(900);
          }, trickSpec.durationMs);
          return;
        }

        const useHoverboard = dist >= 140 && Math.random() < 0.55;
        const speedPxPerSec = useHoverboard ? 115 : 75;
        const durationMs = Math.min(
          3400,
          Math.max(1200, Math.round((dist / speedPxPerSec) * 1000))
        );
        const dir = nextSpot.x < cur.x ? "left" : "right";

        updateState({
          x: nextSpot.x,
          bottomOffset: nextSpot.bottomOffset,
          isWalking: !useHoverboard,
          walkDirection: dir,
          moveDurationMs: durationMs,
          activeTrick: useHoverboard ? "hoverboard" : null,
        });

        if (actionEndTimerRef.current) window.clearTimeout(actionEndTimerRef.current);
        actionEndTimerRef.current = window.setTimeout(() => {
          if (isCancelled) return;
          updateState({
            isWalking: false,
            moveDurationMs: 0,
            activeTrick: null,
          });
          // Immediately perform a trick upon arriving at the new spot!
          runCycle(450);
        }, durationMs);
      }, delayMs);
    };

    // Place in an obstacle-free spot and kick off the first trick cycle
    const initialSpot = findSafeSpot(null, stateRef.current.x);
    updateState({
      x: initialSpot.x,
      bottomOffset: initialSpot.bottomOffset,
      moveDurationMs: 0,
      activeTrick: "dance",
    });
    runCycle(2600);

    return () => {
      isCancelled = true;
      clearTimers();
    };
  }, [findSafeSpot, pickNextTrick, updateState]);

  // 2. Wake up from sleep on user activity & step aside ONLY if an overlapping input is focused
  useEffect(() => {
    if (typeof window === "undefined") return;

    const recordUserActivity = () => {
      lastUserActivityRef.current = Date.now();
      if (stateRef.current.activeTrick === "sleep") {
        updateState({
          activeTrick: "wake",
          isWalking: false,
        });
        window.setTimeout(() => {
          updateState((prev) =>
            prev.activeTrick === "wake" ? { ...prev, activeTrick: "backflip" } : prev
          );
        }, 480);
      }
    };

    const handleFocusIn = (e) => {
      recordUserActivity();
      const target = e.target;
      if (!target || typeof target.getBoundingClientRect !== "function") return;
      if (target.closest?.("[data-roaming-cube]")) return;
      const r = target.getBoundingClientRect();
      const vh = window.innerHeight;
      const cur = stateRef.current;
      const cubeLeft = cur.x - 10;
      const cubeRight = cur.x + 78;
      const cubeTop = vh - cur.bottomOffset - 86;

      // Only step aside if the focused field actually overlaps Cube's current position
      if (r.bottom >= cubeTop && r.right >= cubeLeft && r.left <= cubeRight) {
        const next = findSafeSpot(r.left + r.width / 2, cur.x);
        const dir = next.x < cur.x ? "left" : "right";
        updateState({
          x: next.x,
          bottomOffset: next.bottomOffset,
          isWalking: true,
          walkDirection: dir,
          moveDurationMs: 420,
          activeTrick: null,
        });
        window.setTimeout(() => {
          updateState({ isWalking: false, moveDurationMs: 0 });
        }, 440);
      }
    };

    window.addEventListener("focusin", handleFocusIn, { passive: true });
    window.addEventListener("keydown", recordUserActivity, { passive: true });
    window.addEventListener("mousemove", recordUserActivity, { passive: true });

    return () => {
      window.removeEventListener("focusin", handleFocusIn);
      window.removeEventListener("keydown", recordUserActivity);
      window.removeEventListener("mousemove", recordUserActivity);
    };
  }, [findSafeSpot, updateState]);

  const handleClickMascot = () => {
    updateState({
      isVanishing: true,
      isWalking: false,
      activeTrick: null,
    });
    window.setTimeout(() => {
      onOpen?.();
    }, 140);
  };

  const pillText = state.isHovered
    ? "Ask Guide Bot ✨"
    : state.activeTrick && TRICK_BADGES[state.activeTrick]
    ? TRICK_BADGES[state.activeTrick]
    : null;

  return (
    <div
      data-roaming-cube="true"
      className="fixed bottom-0 left-0 z-40 pointer-events-none select-none"
      style={{
        transform: `translate3d(${state.x}px, -${state.bottomOffset}px, 0)`,
        transitionProperty: "transform",
        transitionDuration: `${state.moveDurationMs}ms`,
        transitionTimingFunction: "linear",
      }}
    >
      <button
        ref={mascotBtnRef}
        type="button"
        data-roaming-cube="true"
        onClick={handleClickMascot}
        onMouseEnter={() => updateState({ isHovered: true })}
        onMouseLeave={() => updateState({ isHovered: false })}
        aria-label="Open Guide Bot"
        title={screenTitle ? `Guide Bot — Explain ${screenTitle}` : "Click to open Guide Bot"}
        className={`group relative pointer-events-auto flex flex-col items-center cursor-pointer focus:outline-hidden transition-all duration-200 ${
          state.isVanishing
            ? "opacity-0 -translate-y-6 scale-75"
            : "opacity-100 translate-y-0 scale-100 hover:-translate-y-1"
        }`}
      >
        {/* Trick / Hover Status Pill */}
        <span
          className={`mb-1.5 px-2.5 py-0.5 rounded-full text-[10.5px] font-extrabold whitespace-nowrap shadow-md border transition-all duration-200 pointer-events-none ${
            pillText
              ? "opacity-100 translate-y-0 scale-100"
              : "opacity-0 translate-y-1 scale-95"
          } ${
            state.isHovered
              ? "bg-blue-600 text-white border-blue-400"
              : "bg-slate-900/95 text-white border-slate-700 dark:bg-white/95 dark:text-slate-900 dark:border-slate-200"
          }`}
        >
          {pillText || "Guide Bot ✨"}
        </span>

        {/* Full-Body Roaming Cube Mascot with Tricks */}
        <GuideBotAvatar
          className="w-[72px] h-[84px] drop-shadow-lg"
          fullBody={true}
          walking={state.isWalking}
          walkDirection={state.walkDirection}
          waving={state.isHovered}
          trick={state.activeTrick}
        />
      </button>
    </div>
  );
}
export default function FeatureGuideBot({ isOpen, onClose }) {
  const [, setSharedIsOpen] = useGuideBotOpenState();
  const location = useLocation();
  const navigate = useNavigate();
  const { isDark } = useTheme();
  const dialogRef = useRef(null);
  const askInputRef = useRef(null);
  const chatEndRef = useRef(null);
  const typingTimerRef = useRef(null);
  const closingPromiseRef = useRef(null);
  const titleId = useId();

  // Keep DOM mounted while smooth exit animation plays
  const [isRendered, setIsRendered] = useState(isOpen);
  const [isExpandedWidth, setIsExpandedWidth] = useState(false);

  // Single active language ('en' | 'ur')
  const [lang, setLang] = useState(() => {
    try {
      const saved = localStorage.getItem(LANG_STORAGE_KEY);
      return saved === "ur" ? "ur" : "en";
    } catch {
      return "en";
    }
  });

  const isUr = lang === "ur";

  const handleSetLang = useCallback((nextLang) => {
    setLang(nextLang);
    try {
      localStorage.setItem(LANG_STORAGE_KEY, nextLang);
    } catch {
      /* ignore */
    }
    window.dispatchEvent(new CustomEvent("guide-bot-lang-changed", { detail: nextLang }));
  }, []);

  const [showDirectory, setShowDirectory] = useState(false);
  const [previewKey, setPreviewKey] = useState(null);

  // Resolved guide for the user's current live screen
  const sensedScreenGuide = useMemo(
    () =>
      resolveScreenGuide(location.pathname, location.search, location.hash) ||
      SCREEN_GUIDES["/dashboard"],
    [location.pathname, location.search, location.hash]
  );

  const displayedGuide = useMemo(() => {
    if (previewKey && SCREEN_GUIDES[previewKey]) {
      return SCREEN_GUIDES[previewKey];
    }
    return sensedScreenGuide || SCREEN_GUIDES["/dashboard"];
  }, [previewKey, sensedScreenGuide]);

  const isPreviewingOther = Boolean(previewKey && previewKey !== sensedScreenGuide.key);

  const [visualTalking, setVisualTalking] = useState(false);
  const [isTyping, setIsTyping] = useState(false);
  const [questionInput, setQuestionInput] = useState("");

  // Cube Companion Drawer state (conversational thread + progressive topic chips)
  const [usedTopics, setUsedTopics] = useState([]);
  const [thread, setThread] = useState([]);

  // Cube Interactive Walkthrough State
  const [activeDemo, setActiveDemo] = useState(null);
  const [highlightStatus, setHighlightStatus] = useState(null);
  const highlightTimerRef = useRef(null);

  const triggerBotTalking = useCallback((durationMs = 1800) => {
    setVisualTalking(true);
    const t = window.setTimeout(() => setVisualTalking(false), durationMs);
    return () => window.clearTimeout(t);
  }, []);

  /**
   * Highlights a live DOM element on the user's current screen during a Cube Walkthrough.
   * Never fabricates UI state: reports 'found' or 'not_found' honestly.
   */
  const highlightElementOnScreen = useCallback((selector) => {
    if (!selector || typeof document === "undefined") return;
    if (highlightTimerRef.current) {
      window.clearTimeout(highlightTimerRef.current);
      highlightTimerRef.current = null;
    }
    document
      .querySelectorAll(".guide-bot-dom-highlight")
      .forEach((el) => el.classList.remove("guide-bot-dom-highlight"));

    let target = null;
    try {
      target = document.querySelector(selector);
    } catch {
      target = null;
    }

    if (target) {
      target.scrollIntoView({ behavior: "smooth", block: "center" });
      target.classList.add("guide-bot-dom-highlight");
      setHighlightStatus("found");
      highlightTimerRef.current = window.setTimeout(() => {
        target.classList.remove("guide-bot-dom-highlight");
        setHighlightStatus(null);
      }, 3200);
    } else {
      setHighlightStatus("not_found");
      highlightTimerRef.current = window.setTimeout(() => {
        setHighlightStatus(null);
      }, 2600);
    }
  }, []);

  /**
   * Launches a Grounded Interactive Step-by-Step Walkthrough in Cube.
   */
  const startCubeDemo = useCallback(
    (workflowId) => {
      if (!workflowId || !DEMO_MANIFESTS[workflowId]) return;
      setShowDirectory(false);
      setHighlightStatus(null);
      setActiveDemo({
        workflowId,
        stepIndex: 0,
        pausedForDetour: false,
      });
      triggerBotTalking(1800);
    },
    [triggerBotTalking]
  );

  // Detect if the currently displayed screen has a matching interactive Cube Demo Manifest
  const currentScreenWorkflowId = useMemo(() => {
    const key = displayedGuide?.key || "";
    if (key.startsWith("/purchase-orders")) return "create_purchase_order";
    if (key.startsWith("/purchase-invoices")) return "create_purchase_invoice";
    if (key.startsWith("/sale-invoices")) return "pos_counter_billing";
    if (key.startsWith("/customer-ledger") || key.startsWith("/customers")) {
      return "customer_udhar_recovery";
    }
    const found = Object.values(DEMO_MANIFESTS).find((m) =>
      m.steps?.some((s) => s.screen === key)
    );
    return found ? found.id || found.workflowId : null;
  }, [displayedGuide]);

  const resetToGreeting = useCallback(
    (guide) => {
      if (typingTimerRef.current) {
        window.clearTimeout(typingTimerRef.current);
        typingTimerRef.current = null;
      }
      setIsTyping(false);
      setUsedTopics([]);
      setThread([
        {
          id: `greeting-${guide.key}-${Date.now()}`,
          role: "bot",
          kind: "greeting",
          guideKey: guide.key,
        },
      ]);
      triggerBotTalking(1800);
    },
    [triggerBotTalking]
  );

  useEffect(() => {
    setPreviewKey(null);
    setShowDirectory(false);
    setHighlightStatus(null);
  }, [location.pathname, location.search, location.hash]);

  useEffect(() => {
    resetToGreeting(displayedGuide);
  }, [displayedGuide, resetToGreeting]);

  useEffect(() => {
    return () => {
      if (typingTimerRef.current) window.clearTimeout(typingTimerRef.current);
      if (highlightTimerRef.current) window.clearTimeout(highlightTimerRef.current);
    };
  }, []);

  /**
   * Smoothly animates the <dialog> out of the top layer before closing and unmounting.
   */
  const runSmoothClose = useCallback(
    async (notifyParent = true) => {
      const dialog = dialogRef.current;
      if (!dialog) {
        setIsRendered(false);
        if (notifyParent) onClose?.();
        return;
      }

      if (closingPromiseRef.current) {
        return closingPromiseRef.current;
      }

      const closeTask = (async () => {
        dialog.setAttribute("data-closing", "");
        const animations =
          typeof dialog.getAnimations === "function"
            ? dialog.getAnimations({ subtree: true })
            : [];

        if (animations.length > 0) {
          await Promise.race([
            Promise.allSettled(animations.map((a) => a.finished)),
            new Promise((r) => window.setTimeout(r, 260)),
          ]);
        } else if (!supportsTopLayerExit()) {
          await new Promise((r) => window.setTimeout(r, 220));
        }

        if (dialog.hasAttribute("data-closing")) {
          dialog.removeAttribute("data-closing");
        }
        if (dialog.open && typeof dialog.close === "function") {
          try {
            dialog.close();
          } catch {
            dialog.removeAttribute("open");
          }
        }
        closingPromiseRef.current = null;
        setIsRendered(false);
        if (notifyParent) onClose?.();
      })();

      closingPromiseRef.current = closeTask;
      return closeTask;
    },
    [onClose]
  );

  // Sync `isOpen` prop changes with smooth open & smooth close
  useEffect(() => {
    if (isOpen) {
      setIsRendered(true);
      const dialog = dialogRef.current;
      if (dialog) {
        dialog.removeAttribute("data-closing");
      }
      triggerBotTalking(1600);
      window.setTimeout(() => askInputRef.current?.focus(), 60);
    } else if (isRendered) {
      runSmoothClose(false);
    }
  }, [isOpen, isRendered, runSmoothClose, triggerBotTalking]);

  // Open <dialog> in top layer when rendered, and route Esc (`cancel`) + backdrop clicks through `runSmoothClose`
  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog || !isRendered || !isOpen) return;

    dialog.removeAttribute("data-closing");
    if (typeof dialog.showModal === "function" && !dialog.open) {
      try {
        dialog.showModal();
      } catch {
        dialog.setAttribute("open", "");
      }
    }

    const handleCancel = (event) => {
      event.preventDefault();
      runSmoothClose(true);
    };

    const handleDialogClick = (event) => {
      if (event.target !== dialog) return;
      const rect = dialog.getBoundingClientRect();
      const isInsideContent =
        rect.top <= event.clientY &&
        event.clientY <= rect.top + rect.height &&
        rect.left <= event.clientX &&
        event.clientX <= rect.left + rect.width;
      if (isInsideContent) return;
      event.preventDefault();
      runSmoothClose(true);
    };

    dialog.addEventListener("cancel", handleCancel);
    dialog.addEventListener("click", handleDialogClick);
    return () => {
      dialog.removeEventListener("cancel", handleCancel);
      dialog.removeEventListener("click", handleDialogClick);
    };
  }, [isRendered, isOpen, runSmoothClose]);

  const scrollToBottom = useCallback(() => {
    window.setTimeout(() => {
      chatEndRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
    }, 45);
  }, []);

  /**
   * Handles progressive-disclosure chip clicks in Cube Drawer.
   */
  const handleTopicChipClick = (topicType, payload = null) => {
    if (topicType === "walkthrough" && payload?.workflowId) {
      startCubeDemo(payload.workflowId);
      return;
    }

    const topicId = payload?.id || topicType;
    setUsedTopics((prev) => (prev.includes(topicId) ? prev : [...prev, topicId]));

    let userLabel = { en: "", ur: "" };
    if (topicType === "steps") {
      userLabel = {
        en: "How do I use this screen step by step?",
        ur: "اس اسکرین کو استعمال کرنے کا مرحلہ وار طریقہ کیا ہے؟",
      };
    } else if (topicType === "features") {
      userLabel = {
        en: "What are the key features on this screen?",
        ur: "اس اسکرین کی اہم خصوصیات کیا ہیں؟",
      };
    } else if (topicType === "impacts") {
      userLabel = {
        en: "How does this screen affect stock & accounting?",
        ur: "اس اسکرین کا اسٹاک اور کھاتوں پر کیا اثر پڑتا ہے؟",
      };
    } else if (topicType === "shortcuts") {
      userLabel = {
        en: "What keyboard shortcuts work here?",
        ur: "یہاں کون سے کی بورڈ شارٹ کٹس کام کرتے ہیں؟",
      };
    } else if (topicType === "faq" && payload?.faq) {
      userLabel = {
        en: payload.faq.q.en,
        ur: payload.faq.q.ur,
      };
    }

    const userMsg = {
      id: `u-${Date.now()}`,
      role: "user",
      label: userLabel,
    };

    setThread((prev) => [...prev, userMsg]);
    setIsTyping(true);
    setVisualTalking(true);
    scrollToBottom();

    if (typingTimerRef.current) window.clearTimeout(typingTimerRef.current);
    typingTimerRef.current = window.setTimeout(() => {
      setIsTyping(false);
      const nextBotItem = {
        id: `b-${Date.now()}`,
        role: "bot",
        kind: topicType,
        guideKey: displayedGuide.key,
        faq: payload?.faq || null,
        chipId: topicId,
      };
      setThread((prev) => [...prev, nextBotItem]);
      scrollToBottom();
      window.setTimeout(() => setVisualTalking(false), 1800);
    }, 220);
  };

  /**
   * Handles user questions in Cube's Companion Drawer using the trained intelligence engine.
   */
  const handleAskSubmit = (e, presetQuery = null) => {
    if (e) e.preventDefault();
    const raw = (presetQuery ?? questionInput).trim();
    if (!raw) return;

    const routed = routeUserQuery({
      queryText: raw,
      currentSpeaker: "cube",
      currentGuide: displayedGuide,
      currentPathname: location.pathname,
      activeDemo,
    });

    const reply = routed.answer || answerUserQuestion(raw, displayedGuide);

    // Launch or pause interactive demo based on query intent
    if (routed.startDemoNow && routed.workflowId && DEMO_MANIFESTS[routed.workflowId]) {
      setActiveDemo({
        workflowId: routed.workflowId,
        stepIndex: 0,
        pausedForDetour: false,
      });
    } else if (activeDemo && !routed.startDemoNow) {
      setActiveDemo((prev) => (prev ? { ...prev, pausedForDetour: true } : null));
    }

    const userMsg = {
      id: `u-${Date.now()}`,
      role: "user",
      text: raw,
    };

    setThread((prev) => [...prev, userMsg]);
    if (!presetQuery) setQuestionInput("");
    setShowDirectory(false);
    setIsTyping(true);
    setVisualTalking(true);
    scrollToBottom();

    if (typingTimerRef.current) window.clearTimeout(typingTimerRef.current);
    typingTimerRef.current = window.setTimeout(() => {
      setIsTyping(false);
      const nextBotItem = {
        id: `b-${Date.now()}`,
        role: "bot",
        kind: "qa",
        questionText: raw,
        title: reply.title,
        en: reply.en,
        ur: reply.ur,
        links: reply.links || [],
        evidenceBadge: routed.evidenceBadge,
        workflowId: routed.workflowId,
      };
      setThread((prev) => [...prev, nextBotItem]);
      scrollToBottom();
      window.setTimeout(() => setVisualTalking(false), 1800);
    }, 220);
  };

  const handleNavigateToRoute = (path) => {
    if (!path || path.includes(":")) return;
    setPreviewKey(null);
    setShowDirectory(false);
    navigate(path);
  };

  // Directory search & filter state
  const [exploreCategory, setExploreCategory] = useState("all");
  const [exploreQuery, setExploreQuery] = useState("");

  const filteredModules = useMemo(() => {
    const q = exploreQuery.trim().toLowerCase();
    return Object.values(SCREEN_GUIDES).filter((item) => {
      if (!q && item.key.includes(":")) return false;
      if (exploreCategory !== "all" && item.category !== exploreCategory) return false;
      if (!q) return true;
      const haystack = `${item.title.en} ${item.title.ur} ${item.summary.en} ${item.summary.ur} ${item.section.en} ${item.section.ur} ${item.key}`.toLowerCase();
      return haystack.includes(q);
    });
  }, [exploreCategory, exploreQuery]);

  // Topic chips for progressive disclosure
  const allScreenChips = useMemo(() => {
    const chips = [];
    if (currentScreenWorkflowId && DEMO_MANIFESTS[currentScreenWorkflowId]) {
      chips.push({
        id: `walkthrough-${currentScreenWorkflowId}`,
        type: "walkthrough",
        workflowId: currentScreenWorkflowId,
        icon: "🎯",
        en: "Interactive Walkthrough (Live Demo)",
        ur: "مرحلہ وار عملی گائیڈ (لائیو ڈیمو)",
      });
    }
    if ((displayedGuide?.steps?.en?.length || 0) > 0) {
      chips.push({
        id: "steps",
        type: "steps",
        icon: "🪜",
        en: "How to use (Steps)",
        ur: "استعمال کا طریقہ",
      });
    }
    if ((displayedGuide?.features?.en?.length || 0) > 0) {
      chips.push({
        id: "features",
        type: "features",
        icon: "✨",
        en: "Key features",
        ur: "اہم خصوصیات",
      });
    }
    if (displayedGuide?.impacts?.en) {
      chips.push({
        id: "impacts",
        type: "impacts",
        icon: "📊",
        en: "Stock & ledger impact",
        ur: "کھاتے اور اسٹاک پر اثر",
      });
    }
    if ((displayedGuide?.shortcuts?.length || 0) > 0) {
      chips.push({
        id: "shortcuts",
        type: "shortcuts",
        icon: "⌨️",
        en: "Shortcuts",
        ur: "شارٹ کٹس",
      });
    }
    (displayedGuide?.faqs || []).forEach((faq, idx) => {
      chips.push({
        id: `faq-${idx}`,
        type: "faq",
        icon: "❓",
        en: faq.q.en,
        ur: faq.q.ur,
        faq,
      });
    });
    return chips;
  }, [displayedGuide, currentScreenWorkflowId]);

  const cubeAvailableChips = useMemo(
    () => allScreenChips.filter((c) => !usedTopics.includes(c.id)),
    [allScreenChips, usedTopics]
  );

  if (!isOpen && !isRendered) {
    return (
      <RoamingCubeCompanion
        onOpen={() => setSharedIsOpen(true)}
        screenTitle={sensedScreenGuide?.title?.en}
      />
    );
  }

  const relatedRoutes = displayedGuide?.relatedRoutes || [];

  /**
   * Renders a Bot Answer Card inside Cube Mode's conversational thread.
   */
  const renderCubeBotCard = (msg) => {
    const guide = SCREEN_GUIDES[msg.guideKey] || displayedGuide;
    const cardBaseClass = `p-3.5 rounded-2xl rounded-tl-xs border shadow-2xs space-y-2 ${
      isDark
        ? "bg-slate-900 border-slate-800 text-slate-100"
        : "bg-white border-slate-200/90 text-slate-800"
    }`;

    // 1. CONCISE 2-SENTENCE GREETING
    if (msg.kind === "greeting") {
      return (
        <div
          key={msg.id}
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={cardBaseClass}
        >
          <div className="flex items-center justify-between gap-2">
            <span className="inline-flex items-center gap-1.5 text-xs font-extrabold text-blue-600 dark:text-blue-400">
              <SparklesIcon className="w-3.5 h-3.5 shrink-0" />
              <span>
                {isUr
                  ? `السلام علیکم! یہ ${guide.title.ur} ہے`
                  : `Hi! You're on ${guide.title.en}`}
              </span>
            </span>
          </div>

          <p
            className={`text-[13.5px] leading-relaxed ${
              isUr ? "guide-bot-urdu-block" : "guide-bot-body-en"
            } text-slate-700 dark:text-slate-200`}
          >
            {isUr ? guide.summary.ur : guide.summary.en}
          </p>

          {isPreviewingOther && !guide.key.includes(":") && (
            <div className="pt-1">
              <button
                type="button"
                onClick={() => handleNavigateToRoute(guide.key)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white transition-all cursor-pointer shadow-xs"
              >
                <span>{isUr ? "یہ صفحہ کھولیں" : `Open ${guide.title.en}`}</span>
                <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
              </button>
            </div>
          )}
        </div>
      );
    }

    // 2. STEP-BY-STEP HOW TO USE
    if (msg.kind === "steps") {
      const steps = (isUr ? guide.steps?.ur : guide.steps?.en) || [];
      return (
        <div
          key={msg.id}
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={cardBaseClass}
        >
          <div className="flex items-center gap-1.5 text-xs font-extrabold text-amber-600 dark:text-amber-400">
            <BoltIcon className="w-4 h-4 shrink-0" />
            <span>{isUr ? "استعمال کا طریقہ (مرحلہ وار):" : "How to use this screen:"}</span>
          </div>
          <ol
            role="list"
            className={`space-y-2 text-[13px] ${
              isUr ? "guide-bot-urdu-block" : ""
            } text-slate-700 dark:text-slate-200`}
          >
            {steps.map((step, i) => (
              <li key={i} className="flex items-start gap-2">
                <span className="inline-flex items-center justify-center w-5 h-5 rounded-full text-[11px] font-bold shrink-0 mt-0.5 bg-amber-500/15 text-amber-700 dark:text-amber-300">
                  {i + 1}
                </span>
                <span className="leading-relaxed">{step}</span>
              </li>
            ))}
          </ol>
        </div>
      );
    }

    // 3. KEY FEATURES
    if (msg.kind === "features") {
      const features = (isUr ? guide.features?.ur : guide.features?.en) || [];
      return (
        <div
          key={msg.id}
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={cardBaseClass}
        >
          <div className="flex items-center gap-1.5 text-xs font-extrabold text-indigo-600 dark:text-indigo-400">
            <CheckCircleIcon className="w-4 h-4 shrink-0" />
            <span>{isUr ? "اہم خصوصیات:" : "Key features on this screen:"}</span>
          </div>
          <ul
            role="list"
            className={`space-y-2 text-[13px] ${
              isUr ? "guide-bot-urdu-block" : ""
            } text-slate-700 dark:text-slate-200`}
          >
            {features.map((feat, i) => (
              <li key={i} className="flex items-start gap-2">
                <span className="mt-2 w-1.5 h-1.5 rounded-full shrink-0 bg-indigo-500" />
                <span className="leading-relaxed">{feat}</span>
              </li>
            ))}
          </ul>
        </div>
      );
    }

    // 4. STOCK & ACCOUNTING IMPACT
    if (msg.kind === "impacts") {
      return (
        <div
          key={msg.id}
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={cardBaseClass}
        >
          <p className="text-xs font-extrabold text-purple-700 dark:text-purple-300">
            {isUr ? "📊 اسٹاک اور کھاتے پر اثر:" : "📊 Stock & Accounting Impact:"}
          </p>
          <p
            className={`text-[13px] leading-relaxed ${
              isUr ? "guide-bot-urdu-block" : ""
            } text-slate-700 dark:text-slate-200`}
          >
            {isUr ? guide.impacts?.ur : guide.impacts?.en}
          </p>
        </div>
      );
    }

    // 5. KEYBOARD SHORTCUTS
    if (msg.kind === "shortcuts") {
      const shortcuts = guide.shortcuts || [];
      return (
        <div
          key={msg.id}
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={cardBaseClass}
        >
          <div className="flex items-center gap-1.5 text-xs font-extrabold text-blue-600 dark:text-blue-400">
            <CommandLineIcon className="w-4 h-4 shrink-0" />
            <span>{isUr ? "کی بورڈ شارٹ کٹس:" : "Keyboard Shortcuts:"}</span>
          </div>
          <div className="space-y-1.5">
            {shortcuts.map((sc, i) => (
              <div
                key={i}
                className="flex items-center justify-between gap-2 text-xs py-1 border-b last:border-0 border-slate-100 dark:border-slate-800"
              >
                <span className={isUr ? "guide-bot-urdu-inline" : "font-medium"}>
                  {isUr ? sc.ur : sc.en}
                </span>
                <div className="flex items-center gap-1 shrink-0" dir="ltr">
                  {sc.keys.map((k) => (
                    <kbd
                      key={k}
                      className="px-1.5 py-0.5 text-[10px] font-mono font-bold rounded border bg-slate-100 dark:bg-slate-800 border-slate-300 dark:border-slate-700"
                    >
                      {k}
                    </kbd>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      );
    }

    // 6. SPECIFIC FAQ ANSWER
    if (msg.kind === "faq" && msg.faq) {
      return (
        <div
          key={msg.id}
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={cardBaseClass}
        >
          <div className="flex items-center gap-1.5 text-xs font-extrabold text-emerald-600 dark:text-emerald-400">
            <QuestionMarkCircleIcon className="w-4 h-4 shrink-0" />
            <span>{isUr ? msg.faq.q.ur : msg.faq.q.en}</span>
          </div>
          <p
            className={`text-[13px] leading-relaxed ${
              isUr ? "guide-bot-urdu-block" : ""
            } text-slate-700 dark:text-slate-200`}
          >
            {isUr ? msg.faq.a.ur : msg.faq.a.en}
          </p>
        </div>
      );
    }

    // 7. FREE-TEXT Q&A REPLY
    const hasWalkthrough = Boolean(msg.workflowId && DEMO_MANIFESTS[msg.workflowId]);
    return (
      <div
        key={msg.id}
        dir={isUr ? "rtl" : "ltr"}
        lang={isUr ? "ur" : "en"}
        className={cardBaseClass}
      >
        <div className="flex items-center justify-between gap-2 flex-wrap">
          {msg.title && (
            <p className="text-xs font-extrabold text-blue-600 dark:text-blue-400">
              {isUr ? msg.title.ur : msg.title.en}
            </p>
          )}
          {msg.evidenceBadge && (
            <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
              {isUr ? msg.evidenceBadge.ur : msg.evidenceBadge.en}
            </span>
          )}
        </div>

        <FormattedBotMessage
          text={isUr ? msg.ur : msg.en}
          isUrdu={isUr}
        />

        {(hasWalkthrough || (msg.links && msg.links.length > 0)) && (
          <div className="flex flex-wrap gap-1.5 pt-1">
            {hasWalkthrough && (
              <button
                type="button"
                onClick={() => startCubeDemo(msg.workflowId)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-xl text-xs font-extrabold bg-emerald-600 hover:bg-emerald-500 text-white shadow-2xs transition-all cursor-pointer"
              >
                <span aria-hidden="true">🎯</span>
                <span>
                  {isUr
                    ? "مرحلہ وار عملی گائیڈ شروع کریں (Interactive Demo)"
                    : "Start Interactive Walkthrough (Step-by-Step)"}
                </span>
              </button>
            )}
            {(msg.links || []).map((lnk) => (
              <button
                key={lnk.path}
                type="button"
                onClick={() => handleNavigateToRoute(lnk.path)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-xl text-xs font-bold bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white shadow-2xs transition-all cursor-pointer"
              >
                <span>{isUr ? lnk.ur : lnk.en}</span>
                <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5 shrink-0" />
              </button>
            ))}
          </div>
        )}
      </div>
    );
  };

  /**
   * Renders Cube's Grounded Interactive Step-by-Step Walkthrough Player (`DEMO_MANIFESTS`).
   * Distinguishes Observed live screen state vs. Conditional documented state and supports
   * live DOM target highlighting + mid-demo concept detours.
   */
  const renderCubeDemoPlayer = () => {
    if (!activeDemo) return null;
    const manifest = DEMO_MANIFESTS[activeDemo.workflowId];
    if (!manifest || !manifest.steps?.length) return null;

    const totalSteps = manifest.steps.length;
    const stepIndex = Math.min(Math.max(0, activeDemo.stepIndex), totalSteps - 1);
    const step = manifest.steps[stepIndex];
    const isOnTargetScreen = location.pathname === step.screen;

    // If paused for a mid-demo concept detour, show a compact "Resume Walkthrough" bar
    if (activeDemo.pausedForDetour) {
      return (
        <div
          dir={isUr ? "rtl" : "ltr"}
          lang={isUr ? "ur" : "en"}
          className={`p-3 rounded-2xl border flex items-center justify-between gap-2 ${
            isDark
              ? "bg-emerald-950/35 border-emerald-700/60 text-emerald-100"
              : "bg-emerald-50 border-emerald-300 text-emerald-950"
          }`}
        >
          <div className="min-w-0 flex-1">
            <p className="text-[11px] font-extrabold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">
              {isUr ? "عملی گائیڈ وقفے پر ہے" : "Walkthrough Paused for Question"}
            </p>
            <p className={`text-xs font-bold truncate ${isUr ? "guide-bot-urdu-inline" : ""}`}>
              {isUr ? manifest.title.ur : manifest.title.en} ({stepIndex + 1}/{totalSteps})
            </p>
          </div>
          <div className="flex items-center gap-1.5 shrink-0">
            <button
              type="button"
              onClick={() =>
                setActiveDemo((prev) => (prev ? { ...prev, pausedForDetour: false } : null))
              }
              className="px-2.5 py-1 rounded-xl text-xs font-extrabold bg-emerald-600 hover:bg-emerald-500 text-white cursor-pointer"
            >
              {isUr ? `مرحلہ ${stepIndex + 1} جاری رکھیں` : `Resume Step ${stepIndex + 1}`}
            </button>
            <button
              type="button"
              onClick={() => setActiveDemo(null)}
              className="p-1 rounded-lg text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 cursor-pointer"
              title={isUr ? "بند کریں" : "Exit walkthrough"}
            >
              <XMarkIcon className="w-4 h-4" />
            </button>
          </div>
        </div>
      );
    }

    return (
      <div
        dir={isUr ? "rtl" : "ltr"}
        lang={isUr ? "ur" : "en"}
        className={`p-3.5 rounded-2xl border shadow-xs space-y-2.5 ${
          isDark
            ? "bg-slate-900/95 border-emerald-600/60 text-slate-100"
            : "bg-white border-emerald-400/90 text-slate-900"
        }`}
      >
        {/* Top Bar: Title + Version + Exit */}
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-1.5 min-w-0">
            <span className="inline-flex items-center justify-center w-5 h-5 rounded-md bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 text-xs font-extrabold shrink-0">
              🎯
            </span>
            <span className="text-xs font-extrabold text-emerald-700 dark:text-emerald-300 truncate">
              {isUr ? manifest.title.ur : manifest.title.en}
            </span>
          </div>
          <div className="flex items-center gap-1.5 shrink-0">
            <span className="px-2 py-0.5 rounded-full text-[10px] font-extrabold bg-emerald-500/15 text-emerald-700 dark:text-emerald-300">
              {isUr
                ? `مرحلہ ${stepIndex + 1} از ${totalSteps}`
                : `Step ${stepIndex + 1} of ${totalSteps}`}
            </span>
            <button
              type="button"
              onClick={() => setActiveDemo(null)}
              title={isUr ? "ڈیمو بند کریں" : "Exit Walkthrough"}
              className="p-1 rounded-lg text-slate-400 hover:text-slate-700 dark:hover:text-white cursor-pointer"
            >
              <XMarkIcon className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* Progress Bar */}
        <div className="w-full h-1.5 rounded-full bg-slate-200 dark:bg-slate-800 overflow-hidden">
          <div
            className="h-full bg-gradient-to-r from-emerald-500 to-cyan-500 transition-all duration-300"
            style={{ width: `${Math.round(((stepIndex + 1) / totalSteps) * 100)}%` }}
          />
        </div>

        {/* Grounded Provenance Status: Observed on Current Screen vs. Conditional */}
        <div className="flex items-center justify-between gap-2 flex-wrap">
          <span
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10.5px] font-bold ${
              isOnTargetScreen
                ? "bg-emerald-500/15 text-emerald-700 dark:text-emerald-300"
                : "bg-amber-500/15 text-amber-700 dark:text-amber-300"
            }`}
          >
            <span>
              {isOnTargetScreen
                ? isUr
                  ? `🟢 موجودہ اسکرین پر مشاہدہ شدہ (${step.screen})`
                  : `🟢 Observed on current screen (${step.screen})`
                : isUr
                ? `📘 مشروط مرحلہ — اسکرین: ${step.screen} (v${PRODUCT_VERSION})`
                : `📘 Conditional — Requires ${step.screen} (v${PRODUCT_VERSION})`}
            </span>
          </span>

          {isOnTargetScreen ? (
            <button
              type="button"
              onClick={() => highlightElementOnScreen(step.target || step.targetSelector)}
              className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-extrabold bg-cyan-500/15 hover:bg-cyan-500/25 text-cyan-700 dark:text-cyan-300 border border-cyan-500/30 transition-colors cursor-pointer"
            >
              <span>🎯</span>
              <span>{isUr ? "اسکرین پر نمایاں کریں" : "Highlight on Screen"}</span>
            </button>
          ) : (
            <button
              type="button"
              onClick={() => handleNavigateToRoute(step.screen)}
              className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-extrabold bg-gradient-to-r from-cyan-500 to-blue-600 text-white shadow-2xs cursor-pointer"
            >
              <span>{isUr ? `صفحہ کھولیں (${step.screen})` : `Open ${step.screen}`}</span>
              <ArrowTopRightOnSquareIcon className="w-3 h-3" />
            </button>
          )}
        </div>

        {highlightStatus === "found" && (
          <p className="text-[11px] font-bold text-emerald-600 dark:text-emerald-400">
            {isUr
              ? "✓ متعلقہ بٹن/فیلڈ کو اسکرین پر ہائی لائٹ کر دیا گیا ہے!"
              : "✓ Highlighted target area on your active screen!"}
          </p>
        )}
        {highlightStatus === "not_found" && (
          <p className="text-[11px] font-bold text-amber-600 dark:text-amber-400">
            {isUr
              ? "ℹ️ یہ فیلڈ فارم کھولنے یا اگلے مرحلے میں نظر آئے گی۔"
              : "ℹ️ This control appears once the form/modal is open on screen."}
          </p>
        )}

        {/* Step Heading & Grounded Instruction */}
        <div className="space-y-1">
          <h4
            className={`text-xs font-extrabold text-slate-900 dark:text-white ${
              isUr ? "guide-bot-urdu-block" : ""
            }`}
          >
            {isUr ? step.title.ur : step.title.en}
          </h4>
          <p
            className={`text-[13px] leading-relaxed text-slate-700 dark:text-slate-200 ${
              isUr ? "guide-bot-urdu-block" : ""
            }`}
          >
            {isOnTargetScreen
              ? isUr
                ? (step.observedInstruction || step.observedText)?.ur
                : (step.observedInstruction || step.observedText)?.en
              : isUr
              ? (step.instruction || step.conditionalText)?.ur
              : (step.instruction || step.conditionalText)?.en}
          </p>
        </div>

        {/* Checkpoint Verification Box */}
        {step.checkpoint && (
          <div
            className={`p-2 rounded-xl border text-[11.5px] font-semibold flex items-start gap-1.5 ${
              isDark
                ? "bg-slate-800/80 border-slate-700 text-emerald-300"
                : "bg-slate-50 border-slate-200/90 text-emerald-800"
            }`}
          >
            <CheckCircleIcon className="w-4 h-4 shrink-0 text-emerald-500 mt-0.5" />
            <span className={isUr ? "guide-bot-urdu-block" : ""}>
              {isUr ? `چیک پوائنٹ: ${step.checkpoint.ur}` : `Checkpoint: ${step.checkpoint.en}`}
            </span>
          </div>
        )}

        {/* Step Navigation Controls */}
        <div className="flex items-center justify-between gap-2 pt-1">
          <button
            type="button"
            disabled={stepIndex === 0}
            onClick={() => {
              setHighlightStatus(null);
              setActiveDemo((prev) =>
                prev ? { ...prev, stepIndex: Math.max(0, prev.stepIndex - 1) } : null
              );
            }}
            className={`px-2.5 py-1 rounded-xl text-xs font-bold border transition-colors ${
              stepIndex === 0
                ? "opacity-40 cursor-not-allowed border-slate-200 dark:border-slate-800 text-slate-400"
                : "cursor-pointer border-slate-300 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800"
            }`}
          >
            {isUr ? "→ پچھلا مرحلہ" : "← Previous"}
          </button>

          {stepIndex < totalSteps - 1 ? (
            <button
              type="button"
              onClick={() => {
                setHighlightStatus(null);
                setActiveDemo((prev) =>
                  prev
                    ? { ...prev, stepIndex: Math.min(totalSteps - 1, prev.stepIndex + 1) }
                    : null
                );
                triggerBotTalking(1200);
              }}
              className="px-3 py-1 rounded-xl text-xs font-extrabold bg-emerald-600 hover:bg-emerald-500 text-white shadow-2xs transition-colors cursor-pointer"
            >
              {isUr ? "اگلا مرحلہ ←" : "Next Step →"}
            </button>
          ) : (
            <button
              type="button"
              onClick={() => setActiveDemo(null)}
              className="px-3 py-1 rounded-xl text-xs font-extrabold bg-gradient-to-r from-emerald-500 to-cyan-500 text-white shadow-2xs transition-colors cursor-pointer"
            >
              {isUr ? "✓ گائیڈ مکمل" : "✓ Finish Walkthrough"}
            </button>
          )}
        </div>
      </div>
    );
  };

  return (
    <dialog
      ref={dialogRef}
      {...{ closedby: "any" }}
      aria-labelledby={titleId}
      className={`guide-bot-drawer guide-bot-mode-cube border shadow-2xl ${
        isExpandedWidth ? "guide-bot-drawer-wide" : ""
      } p-0 ${
        isDark
          ? "bg-slate-900 border-slate-700/90 text-slate-100 shadow-black/60"
          : "bg-white border-slate-200/95 text-slate-900 shadow-slate-900/15"
      }`}
    >
      <div className="guide-bot-drawer-shell">
        {/* =================================================================== */}
        {/* 2-ROW HEADER:                                                       */}
        {/*   ROW 1: Cube Identity Pill + Lang + Utility + Close                */}
        {/*   ROW 2: Full-Width Mascot + Crisp Unclipped Heading & Subtitle     */}
        {/* =================================================================== */}
        <header
          className={`px-4 pt-3 pb-3 shrink-0 flex flex-col gap-3 ${
            isDark
              ? "bg-gradient-to-b from-slate-950 to-slate-900 border-b border-slate-800"
              : "bg-gradient-to-b from-slate-50 to-white border-b border-slate-200/80"
          }`}
        >
          {/* ROW 1: Top Utility Controls Bar */}
          <div className="flex items-center justify-between gap-2">
            {/* Cube Identity Pill */}
            <span
              className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-xl text-[11px] font-extrabold border ${
                isDark
                  ? "bg-slate-800 border-slate-700 text-slate-100"
                  : "bg-slate-100 border-slate-200 text-slate-900"
              }`}
            >
              <span aria-hidden="true">🤖</span>
              <span>{isUr ? "کاروبار اسسٹنٹ" : "Cube Assistant"}</span>
            </span>

            {/* Right Controls: Language (EN | اردو) + Cube Tools + Close */}
            <div className="flex items-center gap-1 shrink-0">
              <div
                role="group"
                aria-label="Language"
                className={`inline-flex items-center p-0.5 rounded-lg border ${
                  isDark
                    ? "bg-slate-800 border-slate-700"
                    : "bg-slate-100 border-slate-200"
                }`}
              >
                <button
                  type="button"
                  onClick={() => handleSetLang("en")}
                  aria-pressed={lang === "en"}
                  className={`px-2 py-0.5 rounded-md text-[11px] font-bold transition-colors cursor-pointer ${
                    lang === "en"
                      ? "bg-slate-900 dark:bg-blue-600 text-white shadow-2xs"
                      : "text-slate-600 dark:text-slate-300 hover:text-slate-900"
                  }`}
                >
                  EN
                </button>
                <button
                  type="button"
                  onClick={() => handleSetLang("ur")}
                  aria-pressed={lang === "ur"}
                  lang="ur"
                  dir="rtl"
                  className={`guide-bot-urdu-inline px-2 py-0.5 rounded-md text-[11px] font-bold transition-colors cursor-pointer ${
                    lang === "ur"
                      ? "bg-emerald-500 text-white shadow-2xs"
                      : "text-slate-600 dark:text-slate-300 hover:text-slate-900"
                  }`}
                >
                  اردو
                </button>
              </div>

              {/* Directory & Width Controls */}
              <button
                type="button"
                onClick={() => setShowDirectory((v) => !v)}
                title={isUr ? "تمام صفحات دیکھیں" : "Browse all screens"}
                aria-pressed={showDirectory}
                className={`p-1.5 rounded-lg transition-colors cursor-pointer ${
                  showDirectory
                    ? "bg-blue-600 text-white"
                    : "text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-100 hover:bg-slate-200/60 dark:hover:bg-slate-800"
                }`}
              >
                <Squares2X2Icon className="w-4 h-4" />
              </button>

              <button
                type="button"
                onClick={() => setIsExpandedWidth((v) => !v)}
                title={isExpandedWidth ? "Compact width" : "Wider view"}
                className="hidden sm:inline-flex p-1.5 rounded-lg text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-100 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
              >
                {isExpandedWidth ? (
                  <ArrowsPointingInIcon className="w-4 h-4" />
                ) : (
                  <ArrowsPointingOutIcon className="w-4 h-4" />
                )}
              </button>

              <button
                type="button"
                onClick={() => runSmoothClose(true)}
                aria-label="Close Assistant"
                className="p-1.5 rounded-lg text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-100 hover:bg-slate-200/60 dark:hover:bg-slate-800 transition-colors cursor-pointer"
              >
                <XMarkIcon className="w-4 h-4" />
              </button>
            </div>
          </div>

          {/* ROW 2: Mascot + Full-Width Heading Stage (Spacious & Never Truncated!) */}
          <div className="flex items-center gap-3.5">
            <button
              type="button"
              onClick={() => {
                const order = ["cube", "pixel", "bot"];
                const cur = getGuideBotMascotStyle();
                const next = order[(order.indexOf(cur) + 1) % order.length];
                setGuideBotMascotStyle(next);
                triggerBotTalking(1600);
              }}
              title="Click mascot to switch outfit (Cube / Pixel / Bot)"
              className="relative shrink-0 cursor-pointer transition-transform duration-200 hover:scale-105 active:scale-95 focus:outline-hidden"
            >
              <GuideBotAvatar
                className="w-16 h-20 drop-shadow-md"
                fullBody={true}
                speaking={visualTalking || isTyping}
                presenter={true}
              />
            </button>

            <div
              className="min-w-0 flex-1 space-y-0.5"
              dir={isUr ? "rtl" : "ltr"}
              lang={isUr ? "ur" : "en"}
            >
              {/* Status Badge Row */}
              <div className="flex items-center gap-1.5 flex-wrap">
                <span className="w-2 h-2 rounded-full shrink-0 animate-pulse bg-emerald-500" />
                <span
                  className={`text-[11px] font-extrabold uppercase tracking-wider text-blue-600 dark:text-blue-400 ${
                    isUr ? "guide-bot-urdu-inline" : ""
                  }`}
                >
                  {isPreviewingOther
                    ? isUr
                      ? "پری ویو موڈ"
                      : "Previewing Screen"
                    : isUr
                    ? "کاروبار اسسٹنٹ • لائیو گائیڈ"
                    : "Karobar Sidekick • Live Guide"}
                </span>

                {isPreviewingOther && (
                  <button
                    type="button"
                    onClick={() => setPreviewKey(null)}
                    className="px-1.5 py-0.5 rounded text-[10px] font-bold bg-blue-500/15 hover:bg-blue-500/25 text-blue-600 dark:text-blue-300 cursor-pointer"
                  >
                    {isUr ? "واپس" : "Reset"}
                  </button>
                )}
              </div>

              {/* Primary Heading Next to Mascot */}
              <h2
                id={titleId}
                className={`font-black tracking-tight break-words text-slate-900 dark:text-white ${
                  isUr
                    ? "guide-bot-urdu-block text-base py-0.5"
                    : "text-[16px] sm:text-[17px] leading-snug"
                }`}
              >
                {isUr ? displayedGuide.title.ur : displayedGuide.title.en}
              </h2>

              {/* Subtitle / Active Screen Context */}
              <p
                className={`text-xs font-semibold text-emerald-600 dark:text-emerald-400 ${
                  isUr ? "guide-bot-urdu-inline" : ""
                }`}
              >
                {isUr ? displayedGuide.section.ur : displayedGuide.section.en}
              </p>
            </div>
          </div>
        </header>

        {/* =================================================================== */}
        {/* BODY: MODULE DIRECTORY OR CONVERSATIONAL COMPANION THREAD           */}
        {/* =================================================================== */}
        {showDirectory ? (
          <div className="guide-bot-drawer-body p-4 space-y-3">
            <div className="flex items-center justify-between gap-2">
              <button
                type="button"
                onClick={() => setShowDirectory(false)}
                className="inline-flex items-center gap-1 text-xs font-bold text-blue-600 dark:text-blue-400 hover:underline cursor-pointer"
              >
                <ChevronLeftIcon className="w-3.5 h-3.5" />
                <span>{isUr ? "واپس جائیں" : "Back"}</span>
              </button>
              <span className="text-[11px] font-semibold text-slate-400">
                {filteredModules.length} {isUr ? "صفحات" : "screens"}
              </span>
            </div>

            <div className="relative">
              <MagnifyingGlassIcon className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                type="text"
                value={exploreQuery}
                onChange={(e) => setExploreQuery(e.target.value)}
                placeholder={
                  isUr ? "کوئی بھی صفحہ تلاش کریں..." : "Search any screen or feature..."
                }
                aria-label="Search screens"
                className={`w-full h-9 pl-9 pr-3 rounded-xl border text-xs font-medium focus:outline-hidden focus:ring-2 focus:ring-blue-500 ${
                  isDark
                    ? "bg-slate-800 border-slate-700 text-white placeholder-slate-400"
                    : "bg-slate-50 border-slate-200 text-slate-900 placeholder-slate-400"
                }`}
              />
            </div>

            <div className="flex items-center gap-1 overflow-x-auto pb-1">
              {MODULE_CATEGORIES.map((cat) => (
                <button
                  key={cat.key}
                  type="button"
                  onClick={() => setExploreCategory(cat.key)}
                  className={`px-2.5 py-1 rounded-lg text-[11px] font-bold whitespace-nowrap transition-colors cursor-pointer border ${
                    exploreCategory === cat.key
                      ? "bg-slate-900 dark:bg-blue-600 text-white border-slate-900 dark:border-blue-600"
                      : isDark
                      ? "bg-slate-800 border-slate-700 text-slate-300 hover:bg-slate-700"
                      : "bg-white border-slate-200 text-slate-700 hover:bg-slate-100"
                  }`}
                >
                  {isUr ? cat.ur : cat.en}
                </button>
              ))}
            </div>

            <div className="space-y-2">
              {filteredModules.map((mod) => {
                const isCurrent = mod.key === sensedScreenGuide.key;
                return (
                  <div
                    key={mod.key}
                    dir={isUr ? "rtl" : "ltr"}
                    lang={isUr ? "ur" : "en"}
                    className={`p-2.5 rounded-xl border flex items-center justify-between gap-2.5 transition-colors ${
                      isCurrent
                        ? isDark
                          ? "bg-emerald-950/30 border-emerald-600/60"
                          : "bg-emerald-50/70 border-emerald-300"
                        : isDark
                        ? "bg-slate-800/50 border-slate-700/80 hover:border-slate-600"
                        : "bg-white border-slate-200 hover:border-blue-300"
                    }`}
                  >
                    <div className="min-w-0 flex-1">
                      <p className="text-[10px] font-bold uppercase tracking-wider text-blue-600 dark:text-blue-400">
                        {isUr ? mod.section.ur : mod.section.en}
                      </p>
                      <h4
                        className={`text-xs font-extrabold text-slate-900 dark:text-white ${
                          isUr ? "guide-bot-urdu-block" : ""
                        }`}
                      >
                        {isUr ? mod.title.ur : mod.title.en}
                      </h4>
                    </div>

                    <div className="flex items-center gap-1.5 shrink-0">
                      <button
                        type="button"
                        onClick={() => {
                          setPreviewKey(mod.key);
                          setShowDirectory(false);
                        }}
                        className="px-2.5 py-1 rounded-lg text-[11px] font-bold bg-slate-200/80 hover:bg-slate-300 dark:bg-slate-700 dark:hover:bg-slate-600 text-slate-800 dark:text-slate-100 transition-colors cursor-pointer"
                      >
                        {isUr ? "پوچھیں" : "Explain"}
                      </button>
                      {!mod.key.includes(":") && (
                        <button
                          type="button"
                          onClick={() => handleNavigateToRoute(mod.key)}
                          className="px-2.5 py-1 rounded-lg text-[11px] font-bold bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white inline-flex items-center gap-1 transition-all cursor-pointer"
                        >
                          <span>{isUr ? "کھولیں" : "Open"}</span>
                          <ArrowTopRightOnSquareIcon className="w-3 h-3" />
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        ) : (
          <div
            className={`guide-bot-drawer-body p-4 space-y-3.5 ${
              isDark ? "bg-slate-950/40" : "bg-slate-50/50"
            }`}
          >
            {renderCubeDemoPlayer()}

            {thread.map((msg) => {
              if (msg.role === "user") {
                const userText = msg.label
                  ? isUr
                    ? msg.label.ur
                    : msg.label.en
                  : msg.text;
                return (
                  <div key={msg.id} className="flex justify-end">
                    <div
                      dir={isUr ? "rtl" : "ltr"}
                      lang={isUr ? "ur" : "en"}
                      className={`max-w-[85%] px-3.5 py-2 rounded-2xl rounded-br-xs bg-slate-900 dark:bg-blue-600 text-white text-xs sm:text-[13px] font-semibold shadow-2xs ${
                        isUr ? "guide-bot-urdu-block" : ""
                      }`}
                    >
                      {userText}
                    </div>
                  </div>
                );
              }
              return renderCubeBotCard(msg);
            })}

            {isTyping && (
              <div className="flex items-center gap-2">
                <div
                  className={`inline-flex items-center gap-1.5 px-3.5 py-2.5 rounded-2xl rounded-tl-xs border ${
                    isDark ? "bg-slate-900 border-slate-800" : "bg-white border-slate-200"
                  }`}
                >
                  <span className="w-1.5 h-1.5 rounded-full bg-blue-500 animate-bounce" />
                  <span
                    className="w-1.5 h-1.5 rounded-full bg-blue-500 animate-bounce"
                    style={{ animationDelay: "120ms" }}
                  />
                  <span
                    className="w-1.5 h-1.5 rounded-full bg-blue-500 animate-bounce"
                    style={{ animationDelay: "240ms" }}
                  />
                </div>
              </div>
            )}

            {!isTyping && (
              <div
                dir={isUr ? "rtl" : "ltr"}
                lang={isUr ? "ur" : "en"}
                className="pt-1 space-y-2.5"
              >
                {cubeAvailableChips.length > 0 && (
                  <div className="space-y-1.5">
                    <p className="text-[11px] font-bold text-slate-400 dark:text-slate-400 px-0.5">
                      {isUr
                        ? "مزید جاننے کے لیے کسی موضوع پر کلک کریں:"
                        : "Tap a topic to explore:"}
                    </p>
                    <div className="flex flex-wrap gap-1.5">
                      {cubeAvailableChips.map((chip) => (
                        <button
                          key={chip.id}
                          type="button"
                          onClick={() => handleTopicChipClick(chip.type, chip)}
                          className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold border transition-all cursor-pointer active:scale-95 ${
                            chip.type === "walkthrough"
                              ? "bg-emerald-500/15 hover:bg-emerald-500/25 border-emerald-500/40 text-emerald-700 dark:text-emerald-300"
                              : isDark
                              ? "bg-slate-900 hover:bg-slate-800 border-slate-700/90 text-slate-200 hover:border-blue-500/60"
                              : "bg-white hover:bg-blue-50/60 border-slate-200/90 text-slate-700 hover:border-blue-300 shadow-2xs"
                          } ${isUr ? "guide-bot-urdu-inline" : ""}`}
                        >
                          <span aria-hidden="true">{chip.icon}</span>
                          <span>{isUr ? chip.ur : chip.en}</span>
                        </button>
                      ))}
                    </div>
                  </div>
                )}

                {relatedRoutes.length > 0 && (
                  <div className="space-y-1.5 pt-0.5">
                    <p className="text-[11px] font-bold text-slate-400 dark:text-slate-400 px-0.5">
                      {isUr ? "متعلقہ صفحات کھولیں:" : "Quick open related screen:"}
                    </p>
                    <div className="flex flex-wrap gap-1.5">
                      {relatedRoutes.map((rel) => (
                        <button
                          key={rel.path}
                          type="button"
                          onClick={() => handleNavigateToRoute(rel.path)}
                          className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-bold bg-blue-500/10 hover:bg-blue-500/20 text-blue-600 dark:text-blue-300 border border-blue-500/20 transition-colors cursor-pointer ${
                            isUr ? "guide-bot-urdu-inline" : ""
                          }`}
                        >
                          <span>{isUr ? rel.ur : rel.en}</span>
                          <ArrowTopRightOnSquareIcon className="w-3 h-3 shrink-0" />
                        </button>
                      ))}
                    </div>
                  </div>
                )}

                {thread.length > 1 && (
                  <div className="pt-1">
                    <button
                      type="button"
                      onClick={() => resetToGreeting(displayedGuide)}
                      className="inline-flex items-center gap-1 text-[11px] font-bold text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 transition-colors cursor-pointer"
                    >
                      <ArrowPathIcon className="w-3 h-3" />
                      <span>{isUr ? "بات چیت صاف کریں" : "Clear chat"}</span>
                    </button>
                  </div>
                )}
              </div>
            )}

            <div ref={chatEndRef} />
          </div>
        )}

        {/* =================================================================== */}
        {/* BOTTOM ASK INPUT BAR                                                */}
        {/* =================================================================== */}
        <footer
          className={`p-3 shrink-0 ${
            isDark
              ? "bg-slate-900 border-t border-slate-800"
              : "bg-white border-t border-slate-200"
          }`}
        >
          <form onSubmit={handleAskSubmit} className="flex items-center gap-2">
            <label htmlFor="guide-bot-ask-input" className="sr-only">
              Ask a question
            </label>
            <input
              id="guide-bot-ask-input"
              ref={askInputRef}
              type="text"
              dir={isUr ? "rtl" : "ltr"}
              value={questionInput}
              onChange={(e) => setQuestionInput(e.target.value)}
              placeholder={
                isUr
                  ? "کچھ بھی پوچھیں (مثلاً: پرچیز آرڈر کیسے بنائیں؟)..."
                  : "Ask Cube anything (e.g. how to make purchase order)..."
              }
              className={`flex-1 h-10 px-3.5 text-xs sm:text-[13px] font-medium rounded-xl border focus:outline-hidden focus:ring-2 focus:ring-blue-500 ${
                isDark
                  ? "bg-slate-800 border-slate-700 text-white placeholder-slate-400"
                  : "bg-slate-50 border-slate-200 text-slate-900 placeholder-slate-400"
              }`}
            />
            <button
              type="submit"
              aria-label={isUr ? "سوال بھیجیں" : "Send question"}
              className="h-10 px-3.5 text-white text-xs font-bold inline-flex items-center justify-center gap-1.5 shadow-2xs transition-all cursor-pointer shrink-0 rounded-xl bg-slate-900 hover:bg-slate-800 dark:bg-blue-600 dark:hover:bg-blue-500"
            >
              <PaperAirplaneIcon className="w-4 h-4" />
            </button>
          </form>
        </footer>
      </div>
    </dialog>
  );
}
