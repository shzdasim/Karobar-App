// resources/js/utils/posScanner.js
import { useEffect, useRef } from "react";

/**
 * High-performance Web Audio API Sound Synthesizer for POS Scanning
 * Zero latency, 100% offline, mathematically generated waveforms.
 */
class PosSoundEngine {
  constructor() {
    this.ctx = null;
    this.enabled = true;
    this.volume = 0.35;
  }

  init() {
    if (!this.ctx && typeof window !== "undefined") {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (AudioCtx) {
        this.ctx = new AudioCtx();
      }
    }
    if (this.ctx && this.ctx.state === "suspended") {
      this.ctx.resume().catch(() => {});
    }
  }

  setVolume(vol) {
    this.volume = Math.max(0, Math.min(1, vol));
  }

  toggleSound(enable = !this.enabled) {
    this.enabled = enable;
    return this.enabled;
  }

  /**
   * Crisp, high-pitched success chime (880 Hz / A5 note, sine wave)
   */
  playSuccess() {
    if (!this.enabled) return;
    try {
      this.init();
      if (!this.ctx) return;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = "sine";
      osc.frequency.setValueAtTime(880, this.ctx.currentTime);

      gain.gain.setValueAtTime(this.volume, this.ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + 0.08);

      osc.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start();
      osc.stop(this.ctx.currentTime + 0.08);
    } catch (_) {
      // Audio playback fails silently if browser blocks autoplay
    }
  }

  /**
   * Double-chirp when incrementing an existing cart item (1174Hz -> 1480Hz)
   */
  playIncrement() {
    if (!this.enabled) return;
    try {
      this.init();
      if (!this.ctx) return;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = "sine";
      osc.frequency.setValueAtTime(1174, this.ctx.currentTime); // D6
      osc.frequency.setValueAtTime(1480, this.ctx.currentTime + 0.04); // F#6

      gain.gain.setValueAtTime(this.volume, this.ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + 0.1);

      osc.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start();
      osc.stop(this.ctx.currentTime + 0.1);
    } catch (_) {}
  }

  /**
   * Low warning tone for not-found, out-of-stock, or expired batch (180 Hz sawtooth wave)
   */
  playError() {
    if (!this.enabled) return;
    try {
      this.init();
      if (!this.ctx) return;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = "sawtooth";
      osc.frequency.setValueAtTime(180, this.ctx.currentTime);

      gain.gain.setValueAtTime(this.volume * 1.2, this.ctx.currentTime);
      gain.gain.linearRampToValueAtTime(0.001, this.ctx.currentTime + 0.22);

      osc.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start();
      osc.stop(this.ctx.currentTime + 0.22);
    } catch (_) {}
  }
}

export const posSound = new PosSoundEngine();

/**
 * React hook for global hardware barcode scanner detection
 * Detects ultra-fast keyboard wedge scanner bursts (< 45ms per keystroke)
 * and dispatches onScan(barcode).
 */
export function useBarcodeScanner({
  onScan,
  enabled = true,
  maxIntervalMs = 50,
  minBarcodeLength = 3,
}) {
  const bufferRef = useRef("");
  const lastKeyTimeRef = useRef(0);
  const onScanRef = useRef(onScan);

  useEffect(() => {
    onScanRef.current = onScan;
  }, [onScan]);

  useEffect(() => {
    if (!enabled) return;

    const handleKeyDown = (e) => {
      const now = performance.now();
      const timeDiff = now - lastKeyTimeRef.current;
      lastKeyTimeRef.current = now;

      // Determine if cashier is currently typing inside a non-scanner text input
      const target = e.target;
      const isInput =
        target &&
        (target.tagName === "INPUT" ||
          target.tagName === "TEXTAREA" ||
          target.isContentEditable);

      // If user is focused on an input element that is NOT marked as scanner-target
      // and typing slowly like a human, do not capture
      const isScannerInput = target && target.classList.contains("scanner-target");

      if (e.key === "Enter") {
        if (bufferRef.current.length >= minBarcodeLength) {
          // Scanner burst detected with Enter terminator
          e.preventDefault();
          e.stopPropagation();
          const code = bufferRef.current.trim();
          bufferRef.current = "";
          if (onScanRef.current) {
            onScanRef.current(code);
          }
          return;
        }
        bufferRef.current = "";
        return;
      }

      // Single printable character
      if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
        if (timeDiff > maxIntervalMs && !isScannerInput) {
          // If slow typing outside scanner input, reset buffer
          if (isInput) {
            bufferRef.current = "";
            return;
          }
          bufferRef.current = e.key;
        } else {
          // Fast keystrokes or focused in scanner input
          bufferRef.current += e.key;
        }
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => {
      window.removeEventListener("keydown", handleKeyDown, true);
    };
  }, [enabled, maxIntervalMs, minBarcodeLength]);
}

