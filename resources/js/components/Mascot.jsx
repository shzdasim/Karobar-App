import { useEffect, useRef, useState, useCallback } from 'react';

export default function Mascot() {
  const mascotRef = useRef(null);
  const [mousePos, setMousePos] = useState({ x: 0, y: 0 });
  const [eyeOffset, setEyeOffset] = useState({ x: 0, y: 0 });
  const [isBlinking, setIsBlinking] = useState(false);
  const [bouncePhase, setBouncePhase] = useState(0);
  const [squish, setSquish] = useState(0);
  const [isSmiling, setIsSmiling] = useState(false);

  // Track mouse
  useEffect(() => {
    const handleMouseMove = (e) => {
      setMousePos({ x: e.clientX, y: e.clientY });
    };
    const handleTouchMove = (e) => {
      if (e.touches.length > 0) {
        setMousePos({ x: e.touches[0].clientX, y: e.touches[0].clientY });
      }
    };
    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('touchmove', handleTouchMove);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('touchmove', handleTouchMove);
    };
  }, []);

  // Eye follows cursor subtly
  useEffect(() => {
    if (!mascotRef.current) return;
    const rect = mascotRef.current.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2 - 5;

    const dx = mousePos.x - centerX;
    const dy = mousePos.y - centerY;
    const dist = Math.sqrt(dx * dx + dy * dy);
    const maxShift = 4;

    if (dist > 0) {
      setEyeOffset({
        x: Math.min(Math.max((dx / dist) * maxShift, -maxShift), maxShift),
        y: Math.min(Math.max((dy / dist) * maxShift, -maxShift), maxShift),
      });
    } else {
      setEyeOffset({ x: 0, y: 0 });
    }
  }, [mousePos]);

  // Periodic blink - REAL blink every 3-5 seconds
  useEffect(() => {
    let blinkTimer;
    let nextBlinkTimer;

    const doBlink = () => {
      setIsBlinking(true);
      blinkTimer = setTimeout(() => {
        setIsBlinking(false);
        nextBlinkTimer = setTimeout(doBlink, 3000 + Math.random() * 2000);
      }, 120);
    };

    const firstBlink = setTimeout(doBlink, 1500);

    return () => {
      clearTimeout(firstBlink);
      clearTimeout(blinkTimer);
      clearTimeout(nextBlinkTimer);
    };
  }, []);

  // Occasional smile every 8-12 seconds
  useEffect(() => {
    let smileTimer;
    let nextSmileTimer;

    const doSmile = () => {
      setIsSmiling(true);
      smileTimer = setTimeout(() => {
        setIsSmiling(false);
        nextSmileTimer = setTimeout(doSmile, 8000 + Math.random() * 4000);
      }, 2000);
    };

    const firstSmile = setTimeout(doSmile, 5000);

    return () => {
      clearTimeout(firstSmile);
      clearTimeout(smileTimer);
      clearTimeout(nextSmileTimer);
    };
  }, []);

  // Gentle bounce
  useEffect(() => {
    const timer = setInterval(() => {
      setBouncePhase((p) => (p + 1) % 2);
    }, 1500);
    return () => clearInterval(timer);
  }, []);

  // Hover squish
  useEffect(() => {
    if (squish > 0) {
      const t = setTimeout(() => setSquish(0), 300);
      return () => clearTimeout(t);
    }
  }, [squish]);

  const onEnter = useCallback(() => setSquish(1), []);
  const onLeave = useCallback(() => setSquish(0), []);
  const nearMouse = mousePos.x > 0 && mousePos.y > 0;

  return (
    <div style={{
      position: 'fixed',
      bottom: '24px',
      left: '50%',
      transform: 'translateX(-50%)',
      zIndex: 9999,
      pointerEvents: 'auto',
      userSelect: 'none',
    }}>
      <div
        ref={mascotRef}
        className={`box ${squish > 0 ? 'squish' : ''}`}
        style={{
          position: 'relative',
          width: '90px',
          height: '80px',
          background: '#0a0a0a',
          borderRadius: '8px',
          border: '2px solid rgba(255,255,255,0.15)',
          boxShadow: `
            0 4px 20px rgba(0,0,0,0.4),
            inset 0 1px 0 rgba(255,255,255,0.1),
            0 0 40px rgba(255,255,255,${nearMouse ? 0.08 : 0.03})
          `,
          transform: `translateY(${bouncePhase === 0 ? 0 : -3}px)${squish > 0 ? ' scaleY(0.9)' : ''}`,
          transition: 'transform 0.15s ease',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 14,
        }}
        onMouseEnter={onEnter}
        onMouseLeave={onLeave}
      >
        {/* Left eye */}
        <Eye
          offsetX={eyeOffset.x}
          offsetY={eyeOffset.y}
          blinking={isBlinking}
        />

        {/* Right eye */}
        <Eye
          offsetX={eyeOffset.x}
          offsetY={eyeOffset.y}
          blinking={isBlinking}
        />

        {/* Mouth */}
        <div className={`mouth${isSmiling ? ' smiling' : ''}`}>
          <svg viewBox="0 0 24 16">
            {/* Smile line */}
            <path d="M3 10 Q12 18 21 10" fill="none" stroke="rgba(255,255,255,0.9)" strokeWidth="2" strokeLinecap="round" />
            {/* Upper teeth */}
            <path d="M8 11 L8 13.5 Q10 14 12 13.5 L12 11" fill="rgba(255,255,255,0.85)" />
            {/* Lower teeth (2 small ones) */}
            <path d="M6.5 12.5 L7.5 11" stroke="rgba(255,255,255,0.7)" strokeWidth="1.5" strokeLinecap="round" />
            <path d="M14.5 12.5 L13.5 11" stroke="rgba(255,255,255,0.7)" strokeWidth="1.5" strokeLinecap="round" />
          </svg>
        </div>

        {/* Antenna */}
        <div className="antenna">
          <div className="antenna-line" />
          <div className="antenna-dot" />
        </div>
      </div>

      <style>{`
        .box {
          position: relative;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 14px;
          transition: transform 0.15s ease;
        }
        .box.squish {
          transform: translateY(-3px) scaleY(0.9);
        }

        .eye {
          position: relative;
          width: 24px;
          height: 24px;
          transform: translate(var(--ox, 0px), var(--oy, 0px));
          transition: transform 0.1s ease-out;
          will-change: transform;
        }

        .sclera {
          width: 24px;
          height: 24px;
          background: radial-gradient(circle at 35% 30%, #fffef5, #f5f5ff 60%, #e8e8f0);
          border-radius: 50%;
          box-shadow:
            inset 0 -3px 4px rgba(0,0,0,0.08),
            inset 0 3px 5px rgba(255,255,255,0.9),
            0 2px 8px rgba(0,0,0,0.15),
            0 0 12px rgba(255,255,255,0.15);
          display: flex;
          align-items: center;
          justify-content: center;
          position: relative;
          overflow: hidden;
        }

        .iris {
          width: 12px;
          height: 12px;
          background: radial-gradient(circle at 40% 35%, #1a1a1a, #000 70%);
          border-radius: 50%;
          display: flex;
          align-items: center;
          justify-content: center;
          position: relative;
          box-shadow:
            inset 0 1px 2px rgba(255,255,255,0.15),
            0 0 6px rgba(0,0,0,0.4);
        }

        .pupil {
          width: 6px;
          height: 6px;
          background: #000;
          border-radius: 50%;
          position: absolute;
          top: 50%;
          left: 50%;
          transform: translate(-50%, -50%);
          box-shadow: inset 0 1px 1px rgba(255,255,255,0.1);
        }

        /* Big main twinkle highlight */
        .iris::before {
          content: '';
          position: absolute;
          top: 2px;
          left: 3px;
          width: 5px;
          height: 5px;
          background: white;
          border-radius: 50%;
          opacity: 0.95;
          animation: twinkle-main 1.8s ease-in-out infinite;
          box-shadow: 0 0 3px rgba(255,255,255,0.8);
        }

        /* Secondary twinkle */
        .iris::after {
          content: '';
          position: absolute;
          bottom: 2px;
          right: 2px;
          width: 2.5px;
          height: 2.5px;
          background: white;
          border-radius: 50%;
          opacity: 0.7;
          animation: twinkle-secondary 2.2s ease-in-out infinite;
          animation-delay: 0.3s;
        }

        /* Tiny extra sparkle */
        .sparkle-tiny {
          position: absolute;
          top: 1px;
          right: 3px;
          width: 2px;
          height: 2px;
          background: white;
          border-radius: 50%;
          opacity: 0.8;
          animation: twinkle-tiny 1.5s ease-in-out infinite;
          animation-delay: 0.7s;
          box-shadow: 0 0 2px rgba(255,255,255,0.9);
        }

        /* Floating sparkles around eye */
        .sparkle-1 {
          position: absolute;
          top: -4px;
          right: -4px;
          width: 3px;
          height: 3px;
          background: white;
          border-radius: 50%;
          opacity: 0;
          animation: float-sparkle 2.5s ease-in-out infinite;
          box-shadow: 0 0 4px rgba(255,255,255,0.9);
        }
        .sparkle-2 {
          position: absolute;
          bottom: -5px;
          left: -3px;
          width: 2px;
          height: 2px;
          background: white;
          border-radius: 50%;
          opacity: 0;
          animation: float-sparkle 3s ease-in-out infinite;
          animation-delay: 0.8s;
          box-shadow: 0 0 3px rgba(255,255,255,0.8);
        }
        .sparkle-3 {
          position: absolute;
          top: 6px;
          left: -6px;
          width: 2.5px;
          height: 2.5px;
          background: white;
          border-radius: 50%;
          opacity: 0;
          animation: float-sparkle 2.8s ease-in-out infinite;
          animation-delay: 1.2s;
          box-shadow: 0 0 3px rgba(255,255,255,0.8);
        }

        /* Soft glow around eye */
        .glow {
          position: absolute;
          top: 50%;
          left: 50%;
          width: 32px;
          height: 32px;
          transform: translate(-50%, -50%);
          background: radial-gradient(circle, rgba(255,255,255,0.25), transparent 70%);
          border-radius: 50%;
          pointer-events: none;
          animation: eye-glow 3s ease-in-out infinite;
        }

        .eye.blinking .sclera {
          animation: eye-blink 120ms ease-in-out;
        }

        @keyframes eye-blink {
          0% { transform: scaleY(1); }
          50% { transform: scaleY(0.05); }
          100% { transform: scaleY(1); }
        }

        @keyframes twinkle-main {
          0%, 100% { opacity: 0.9; transform: scale(1) translate(0, 0); }
          30% { opacity: 1; transform: scale(1.2) translate(-1px, -1px); }
          60% { opacity: 0.85; transform: scale(0.9) translate(0, 0); }
        }

        @keyframes twinkle-secondary {
          0%, 100% { opacity: 0.5; transform: scale(0.8); }
          50% { opacity: 0.9; transform: scale(1.2); }
        }

        @keyframes twinkle-tiny {
          0%, 100% { opacity: 0.4; transform: scale(0.7); }
          50% { opacity: 1; transform: scale(1.3); }
        }

        @keyframes float-sparkle {
          0%, 100% { opacity: 0; transform: translateY(0) scale(0.5); }
          20% { opacity: 1; transform: translateY(-3px) scale(1); }
          40% { opacity: 0.6; transform: translateY(-6px) scale(0.8); }
          60% { opacity: 0; transform: translateY(-9px) scale(0.5); }
        }

        @keyframes eye-glow {
          0%, 100% { opacity: 0.6; transform: translate(-50%, -50%) scale(1); }
          50% { opacity: 1; transform: translate(-50%, -50%) scale(1.15); }
        }

        .mouth {
          position: absolute;
          bottom: 10px;
          left: 50%;
          transform: translateX(-50%);
          width: 24px;
          height: 16px;
          transition: transform 0.3s ease;
        }
        .mouth svg {
          width: 100%;
          height: 100%;
        }
        .mouth.smiling {
          transform: translateX(-50%) scale(1.1) translateY(-2px);
        }
        .mouth.smiling svg path:first-child {
          animation: smile-glow 2s ease-in-out infinite;
        }
        @keyframes smile-glow {
          0%, 100% { stroke: rgba(255,255,255,0.9); }
          50% { stroke: rgba(255,255,255,1); filter: drop-shadow(0 0 3px rgba(255,255,255,0.5)); }
        }

        .antenna {
          position: absolute;
          top: -10px;
          left: 50%;
          transform: translateX(-50%);
          display: flex;
          flex-direction: column;
          align-items: center;
          z-index: 10;
        }
        .antenna-line {
          width: 2px;
          height: 10px;
          background: linear-gradient(to bottom, #444, #222);
          border-radius: 1px;
          margin-bottom: -2px;
        }
        .antenna-dot {
          width: 5px;
          height: 5px;
          background: #555;
          border-radius: 50%;
          box-shadow: 0 0 6px rgba(255,255,255,0.4);
          animation: dot-pulse 2s ease-in-out infinite;
        }
        @keyframes dot-pulse {
          0%, 100% { transform: scale(1); opacity: 0.8; box-shadow: 0 0 4px rgba(255,255,255,0.3); }
          50% { transform: scale(1.4); opacity: 1; box-shadow: 0 0 8px rgba(255,255,255,0.6); }
        }

        @media (prefers-reduced-motion: reduce) {
          .box, .antenna-dot, .eye.blinking .sclera, .iris::before, .iris::after, .sparkle-tiny, .sparkle-1, .sparkle-2, .sparkle-3, .glow {
            animation: none !important;
          }
        }
      `}</style>
    </div>
  );
}

// Separate Eye component for cleaner blink animation
function Eye({ offsetX, offsetY, blinking }) {
  return (
    <div
      className={`eye${blinking ? ' blinking' : ''}`}
      style={{
        '--ox': `${offsetX}px`,
        '--oy': `${offsetY}px`,
      }}
    >
      <div className="glow" />
      <div className="sclera">
        <div className="iris">
          <div className="pupil" />
          <div className="sparkle-tiny" />
        </div>
        <div className="sparkle-1" />
        <div className="sparkle-2" />
        <div className="sparkle-3" />
      </div>
    </div>
  );
}
