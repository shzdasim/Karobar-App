import React from "react";
import { createPortal } from "react-dom";
import MascotCharacter from "./MascotCharacter.jsx";

const STARS = [
  { left: "8%", bottom: "32px", size: 6, delay: "0s" },
  { left: "19%", bottom: "64px", size: 4, delay: "0.2s" },
  { left: "31%", bottom: "26px", size: 7, delay: "0.4s" },
  { left: "46%", bottom: "54px", size: 5, delay: "0.1s" },
  { left: "61%", bottom: "30px", size: 6, delay: "0.35s" },
  { left: "74%", bottom: "68px", size: 4, delay: "0.15s" },
  { left: "88%", bottom: "36px", size: 7, delay: "0.3s" },
];

/**
 * Full-viewport stage for the Mascot & Curtain Cord theme transition.
 *
 * Choreography:
 * - 'to-dark' (Light -> Dark):
 *   1. Mascot appears near the topbar toggle and jumps UP to the top where the curtain cord hangs.
 *   2. Mascot grabs the cord and pushes/pulls the dark curtain DOWN across the screen while waving at us.
 *   3. At the bottom, the mascot goes down with the curtain, slides into the bottom edge, and disappears.
 *
 * - 'to-light' (Dark -> Light, Reverse):
 *   1. Mascot slides UP from the bottom of the screen and grabs the lowered curtain cord.
 *   2. Mascot pushes/pulls the curtain UP from bottom to top (revealing Light Mode underneath) while waving at us.
 *   3. Upon reaching the top where the cord hangs, the mascot hops off at the top and disappears.
 */
export default function ThemeCurtainStage({ transition }) {
  if (!transition?.active || typeof document === "undefined") {
    return null;
  }

  const { direction = "to-dark", cordX = window.innerWidth - 260, fallback = false, id } = transition;
  const isToDark = direction === "to-dark";

  return createPortal(
    <div
      key={id}
      className={`mascot-curtain-stage mascot-stage-${direction} ${fallback ? "mascot-stage-fallback" : ""}`}
      style={{ "--cord-x": `${cordX}px` }}
      aria-hidden="true"
    >
      {/* Top ornamental curtain rod & pulley bracket where the cord hangs */}
      <div className="mascot-top-rod">
        <div className="mascot-pulley-bracket">
          <div className="mascot-pulley-wheel" />
        </div>
      </div>

      {/* Curtain Seam & Pleated Fabric Drape moving from top->bottom (to-dark) or bottom->top (to-light) */}
      <div className={`mascot-curtain-seam-track mascot-seam-${direction}`}>
        {/* Pleated theatrical curtain valance above the hem bar */}
        <div className="mascot-curtain-drape">
          <div className="mascot-curtain-pleats" />
          <div className="mascot-curtain-stars">
            {STARS.map((s, idx) => (
              <span
                key={idx}
                className="mascot-curtain-star"
                style={{
                  left: s.left,
                  bottom: s.bottom,
                  width: `${s.size}px`,
                  height: `${s.size}px`,
                  animationDelay: s.delay,
                }}
              />
            ))}
          </div>
        </div>

        {/* Weighted Golden/Indigo Curtain Bottom Hem Bar & Scalloped Fringe */}
        <div className="mascot-curtain-hem">
          <div className="mascot-curtain-hem-rod" />
          <div className="mascot-curtain-fringe" />
          <div className="mascot-curtain-glow" />
        </div>
      </div>

      {/* Hanging Curtain Cord & Golden Tassel Track */}
      <div className={`mascot-cord-track mascot-cord-${direction}`}>
        {/* Braided cord extending upward from the tassel into the top pulley */}
        <div className="mascot-cord-line" />
        {/* Decorative golden bell tassel that the mascot grabs */}
        <div className="mascot-cord-tassel">
          <div className="mascot-tassel-cap" />
          <div className="mascot-tassel-skirt" />
        </div>
      </div>

      {/* Mascot Actor Track (jumps to cord, pulls down/up while waving, slides out) */}
      <div className={`mascot-actor-track mascot-actor-${direction}`}>
        <div className="mascot-sparkle-burst" />
        <MascotCharacter direction={direction} />
      </div>
    </div>,
    document.body
  );
}
