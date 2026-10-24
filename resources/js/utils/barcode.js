// resources/js/utils/barcode.js
/**
 * Zero-Dependency Pure Vector Barcode Generator (Code 128 Auto & EAN-13)
 * Outputs crisp vector SVGs for pixel-perfect thermal & laser label printing.
 */

// Code 128 widths (107 patterns: indices 0-105 have 6 widths = 11 modules, index 106 has 7 widths = 13 modules)
const C128_PATTERNS = [
  "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
  "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
  "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
  "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
  "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
  "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
  "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
  "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
  "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
  "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
  "114131","311141","411131","211412","211214","211232","2331112"
];

const CODE_START_A = 103;
const CODE_START_B = 104;
const CODE_START_C = 105;
const CODE_STOP = 106;
const CODE_TO_B = 100;
const CODE_TO_C = 99;

/**
 * Encode an alphanumeric string into Code 128 module values
 */
export function encodeCode128(text) {
  if (!text || typeof text !== "string") {
    text = "000000";
  }

  // Sanitize text: keep printable ASCII (32-126)
  const clean = text.replace(/[^\x20-\x7E]/g, "").trim() || "000000";

  let i = 0;
  const values = [];
  let currentMode = "";

  // Check if starts with at least 4 digits -> Start C, else Start B
  const startsWith4Digits = /^\d{4}/.test(clean);
  if (startsWith4Digits) {
    currentMode = "C";
    values.push(CODE_START_C);
  } else {
    currentMode = "B";
    values.push(CODE_START_B);
  }

  while (i < clean.length) {
    if (currentMode === "C") {
      // Check if we still have at least 2 digits
      if (/^\d{2}/.test(clean.substring(i))) {
        const pair = parseInt(clean.substring(i, i + 2), 10);
        values.push(pair);
        i += 2;
      } else {
        // Switch to Mode B
        currentMode = "B";
        values.push(CODE_TO_B);
      }
    } else {
      // In Mode B: check if remaining has 4+ consecutive digits -> switch to C
      if (/^\d{4}/.test(clean.substring(i))) {
        currentMode = "C";
        values.push(CODE_TO_C);
      } else {
        const code = clean.charCodeAt(i) - 32;
        values.push(code >= 0 && code <= 95 ? code : 0);
        i++;
      }
    }
  }

  // Calculate Checksum: (StartCode + Sum(val * position)) % 103
  let checksum = values[0];
  for (let pos = 1; pos < values.length; pos++) {
    checksum += values[pos] * pos;
  }
  checksum = checksum % 103;
  values.push(checksum);
  values.push(CODE_STOP);

  return values;
}

/**
 * Generate binary string of bars (1) and spaces (0) from Code 128 values
 */
export function code128ToBinary(values) {
  let binary = "";
  for (const val of values) {
    const pattern = C128_PATTERNS[val];
    if (!pattern) continue;
    let isBar = true;
    for (const char of pattern) {
      const width = parseInt(char, 10);
      binary += (isBar ? "1" : "0").repeat(width);
      isBar = !isBar;
    }
  }
  return binary;
}

/**
 * Generates an SVG string or configuration for Code 128
 * @param {string} text - The value to encode
 * @param {object} options - Options: height, barWidth, quietZone, color, bgColor
 */
export function generateBarcodeSvgString(text, options = {}) {
  const {
    height = 40,
    barWidth = 1.4,
    quietZone = 10,
    color = "#000000",
    bgColor = "transparent",
    showText = false,
    fontSize = 11,
  } = options;

  const values = encodeCode128(text);
  const binary = code128ToBinary(values);

  // Group continuous 1s into rectangles
  const bars = [];
  let currentPos = quietZone;
  let inBar = false;
  let barStart = 0;

  for (let i = 0; i < binary.length; i++) {
    const bit = binary[i];
    if (bit === "1") {
      if (!inBar) {
        inBar = true;
        barStart = i;
      }
    } else {
      if (inBar) {
        inBar = false;
        bars.push({
          x: (quietZone + barStart) * barWidth,
          w: (i - barStart) * barWidth,
        });
      }
    }
  }
  if (inBar) {
    bars.push({
      x: (quietZone + barStart) * barWidth,
      w: (binary.length - barStart) * barWidth,
    });
  }

  const totalModules = binary.length + quietZone * 2;
  const totalWidth = totalModules * barWidth;
  const totalHeight = showText ? height + fontSize + 4 : height;

  const rectsSvg = bars
    .map(
      (b) =>
        `<rect x="${b.x.toFixed(2)}" y="0" width="${b.w.toFixed(2)}" height="${height}" fill="${color}" />`
    )
    .join("");

  const textSvg = showText
    ? `<text x="${(totalWidth / 2).toFixed(2)}" y="${(height + fontSize).toFixed(2)}" text-anchor="middle" font-family="monospace, monospace" font-size="${fontSize}" font-weight="600" fill="${color}">${escapeXml(text)}</text>`
    : "";

  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${totalWidth.toFixed(2)} ${totalHeight.toFixed(2)}" width="${totalWidth.toFixed(2)}" height="${totalHeight.toFixed(2)}" style="background:${bgColor};display:block;max-width:100%;">${rectsSvg}${textSvg}</svg>`;
}

function escapeXml(str) {
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

/**
 * React Component for rendering Vector Barcode
 */
import React from "react";

export function BarcodeSvg({
  value,
  height = 36,
  barWidth = 1.3,
  quietZone = 6,
  color = "#000000",
  showText = false,
  fontSize = 11,
  className = "",
  style = {},
}) {
  const values = React.useMemo(() => encodeCode128(String(value || "000000")), [value]);
  const binary = React.useMemo(() => code128ToBinary(values), [values]);

  const bars = React.useMemo(() => {
    const list = [];
    let inBar = false;
    let barStart = 0;

    for (let i = 0; i < binary.length; i++) {
      if (binary[i] === "1") {
        if (!inBar) {
          inBar = true;
          barStart = i;
        }
      } else if (inBar) {
        inBar = false;
        list.push({
          x: (quietZone + barStart) * barWidth,
          w: (i - barStart) * barWidth,
        });
      }
    }
    if (inBar) {
      list.push({
        x: (quietZone + barStart) * barWidth,
        w: (binary.length - barStart) * barWidth,
      });
    }
    return list;
  }, [binary, barWidth, quietZone]);

  const totalModules = binary.length + quietZone * 2;
  const totalWidth = totalModules * barWidth;
  const totalHeight = showText ? height + fontSize + 4 : height;

  const rectElements = bars.map((b, idx) =>
    React.createElement("rect", {
      key: idx,
      x: b.x,
      y: 0,
      width: b.w,
      height: height,
      fill: color,
      shapeRendering: "crispEdges",
    })
  );

  const textElement = showText
    ? React.createElement(
        "text",
        {
          key: "txt",
          x: totalWidth / 2,
          y: height + fontSize,
          textAnchor: "middle",
          fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace",
          fontSize: fontSize,
          fontWeight: "600",
          fill: color,
          letterSpacing: "0.05em",
        },
        String(value || "")
      )
    : null;

  return React.createElement(
    "svg",
    {
      xmlns: "http://www.w3.org/2000/svg",
      viewBox: `0 0 ${totalWidth.toFixed(2)} ${totalHeight.toFixed(2)}`,
      width: totalWidth,
      height: totalHeight,
      className: className,
      style: {
        display: "block",
        maxWidth: "100%",
        height: "auto",
        ...style,
      },
    },
    ...rectElements,
    textElement
  );
}

export default BarcodeSvg;
