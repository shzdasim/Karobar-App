/**
 * Pure-ESM zero-dependency security and auth resilience helpers.
 * Intentionally free of React, JSX, DOM globals, or side effects at module top-level
 * so Node `node:test` suites can import and verify every helper directly.
 */

const ALLOWED_CSS_ALIGN = new Set(["left", "right", "center", "justify"]);
const ALLOWED_EXPLICIT_URI_SCHEMES = new Set(["http:", "https:", "mailto:", "tel:", "blob:"]);
const ALLOWED_RASTER_DATA_URI_RE =
  /^data:image\/(?:png|jpe?g|gif|webp|bmp|x-icon)(?:[;,]|$)/i;
const URI_ATTR_NAMES = new Set([
  "href",
  "src",
  "action",
  "formaction",
  "xlink:href",
  "poster",
  "background",
  "cite",
  "dynsrc",
  "lowsrc",
]);

// Strict HTML element allowlist when outside <svg> (zero unused insertion modes or foreign <math>)
const HTML_ALLOWED_TAGS = new Set([
  "html",
  "head",
  "body",
  "meta",
  "title",
  "style",
  "div",
  "span",
  "p",
  "br",
  "hr",
  "h1",
  "h2",
  "h3",
  "h4",
  "h5",
  "h6",
  "blockquote",
  "pre",
  "code",
  "small",
  "strong",
  "b",
  "em",
  "i",
  "u",
  "s",
  "sub",
  "sup",
  "mark",
  "center",
  "font",
  "header",
  "footer",
  "main",
  "section",
  "article",
  "aside",
  "nav",
  "summary",
  "details",
  "figure",
  "figcaption",
  "ul",
  "ol",
  "li",
  "dl",
  "dt",
  "dd",
  "table",
  "thead",
  "tbody",
  "tfoot",
  "tr",
  "th",
  "td",
  "caption",
  "img",
  "a",
  "video",
  "button",
  "label",
  "textarea",
  "svg",
]);

// Strict SVG element allowlist when inside <svg> (zero HTML integration points, zero <style>/<script>/<use>/<a>)
const SVG_ALLOWED_TAGS = new Set([
  "svg",
  "g",
  "defs",
  "rect",
  "circle",
  "ellipse",
  "line",
  "polyline",
  "polygon",
  "path",
  "text",
  "tspan",
]);

const HTML_VOID_TAGS = new Set(["br", "hr", "img", "meta"]);

// Outside <svg>, these RAWTEXT elements are dropped along with their body up to their closing tag (or EOF).
const RAWTEXT_DROPPED_HTML_TAGS = new Set([
  "script",
  "iframe",
  "noscript",
  "noembed",
  "noframes",
  "xmp",
]);

// Strict WHATWG HTML5 ASCII whitespace (U+0009 TAB, U+000A LF, U+000C FF, U+000D CR, U+0020 SPACE).
function isHtmlWs(ch) {
  return ch === " " || ch === "\t" || ch === "\n" || ch === "\f" || ch === "\r";
}

/**
 * Escapes HTML special characters for safe interpolation into HTML templates.
 * Preserves `0` and `false` as `"0"` and `"false"`, while returning `""` for null/undefined.
 */
export function escapeHtml(value) {
  if (value === null || value === undefined) return "";
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/**
 * Allowlists CSS text-align values to prevent style/tag injection in HTML print builders.
 */
export function sanitizeCssAlign(value, fallback = "center") {
  const normalizedFallback = ALLOWED_CSS_ALIGN.has(String(fallback || "").trim().toLowerCase())
    ? String(fallback).trim().toLowerCase()
    : "center";
  if (value === null || value === undefined) return normalizedFallback;
  const candidate = String(value).trim().toLowerCase();
  return ALLOWED_CSS_ALIGN.has(candidate) ? candidate : normalizedFallback;
}

function safeFromCodePoint(code, nullCharReplacement = "") {
  if (code === 0) return nullCharReplacement;
  if (!Number.isFinite(code) || code < 0 || code > 0x10ffff) {
    return "\uFFFD";
  }
  return String.fromCodePoint(code);
}

const NAMED_HTML_ENTITIES = {
  quot: '"',
  apos: "'",
  colon: ":",
  semi: ";",
  tab: "\t",
  newline: "\n",
  lpar: "(",
  rpar: ")",
  sol: "/",
  bsol: "\\",
  amp: "&",
  plus: "+",
  lt: "<",
  gt: ">",
  comma: ",",
  num: "#",
  percnt: "%",
  period: ".",
  hyphen: "-",
  lowbar: "_",
  ast: "*",
  equals: "=",
  quest: "?",
};

const HTML_ENTITY_RE =
  /&(?:#x([0-9a-f]+);?|#([0-9]+);?|(quot|apos|colon|semi|tab|newline|lpar|rpar|sol|bsol|amp|plus|lt|gt|comma|num|percnt|period|hyphen|lowbar|ast|equals|quest);|(quot|amp|lt|gt)(?![a-zA-Z0-9=]))/gi;

function decodeHtmlEntityMatch(hex, dec, namedSemi, namedLegacy, nullCharReplacement = "") {
  if (hex !== undefined) {
    return safeFromCodePoint(Number.parseInt(hex, 16), nullCharReplacement);
  }
  if (dec !== undefined) {
    return safeFromCodePoint(Number.parseInt(dec, 10), nullCharReplacement);
  }
  const named = namedSemi ?? namedLegacy;
  return NAMED_HTML_ENTITIES[named.toLowerCase()] ?? "";
}

function decodeBasicHtmlEntities(str, { nullCharReplacement = "" } = {}) {
  return String(str).replace(HTML_ENTITY_RE, (_, hex, dec, namedSemi, namedLegacy) =>
    decodeHtmlEntityMatch(hex, dec, namedSemi, namedLegacy, nullCharReplacement)
  );
}

function normalizeHtmlEntitiesWithMap(str, { nullCharReplacement = "" } = {}) {
  let norm = "";
  const origStart = [];
  const origEnd = [];
  let lastIndex = 0;

  HTML_ENTITY_RE.lastIndex = 0;
  let match;
  while ((match = HTML_ENTITY_RE.exec(str)) !== null) {
    const start = match.index;
    const end = start + match[0].length;
    for (let i = lastIndex; i < start; i += 1) {
      norm += str[i];
      origStart.push(i);
      origEnd.push(i + 1);
    }
    const decoded = decodeHtmlEntityMatch(
      match[1],
      match[2],
      match[3],
      match[4],
      nullCharReplacement
    );
    for (let j = 0; j < decoded.length; j += 1) {
      norm += decoded[j];
      origStart.push(start);
      origEnd.push(end);
    }
    lastIndex = end;
  }

  for (let i = lastIndex; i < str.length; i += 1) {
    norm += str[i];
    origStart.push(i);
    origEnd.push(i + 1);
  }

  return { norm, origStart, origEnd };
}

function decodeCssEscapes(str) {
  return String(str).replace(
    /\\(?:([0-9a-fA-F]{1,6})(?:\r\n|[\t\n\f\r ])?|(\r\n|[\n\r\f])|([^\r\n\f0-9a-fA-F]))/g,
    (_, hex, newline, escapedChar) => {
      if (hex !== undefined) {
        return safeFromCodePoint(Number.parseInt(hex, 16));
      }
      if (newline !== undefined) {
        return "";
      }
      return escapedChar;
    }
  );
}

/**
 * Strict URI scheme allowlist: permits only relative URLs (no scheme before `/`, `?`, `#`),
 * `http:`, `https:`, `mailto:`, `tel:`, `blob:`, and raster `data:image/(png|jpe?g|gif|webp|bmp|x-icon)`.
 */
function isSafeUriValue(rawValue) {
  if (rawValue === null || rawValue === undefined) return true;
  const decoded = decodeBasicHtmlEntities(String(rawValue))
    // Strip ASCII control characters and whitespace that browsers ignore inside URL schemes
    .replace(/[\u0000-\u0020\u007f]+/g, "");
  if (!decoded) return true;
  const authorityOrPathEnd = decoded.search(/[/?#]/);
  const prefix = authorityOrPathEnd === -1 ? decoded : decoded.slice(0, authorityOrPathEnd);
  const colonIdx = prefix.indexOf(":");
  if (colonIdx === -1) {
    return true;
  }
  const scheme = prefix.slice(0, colonIdx + 1).toLowerCase();
  if (scheme === "data:") {
    return ALLOWED_RASTER_DATA_URI_RE.test(decoded);
  }
  return ALLOWED_EXPLICIT_URI_SCHEMES.has(scheme);
}

function isSafeCssUrlTarget(rawTarget) {
  if (!rawTarget) return true;
  let decoded = decodeBasicHtmlEntities(rawTarget).trim();
  if (
    decoded.length >= 2 &&
    ((decoded.startsWith('"') && decoded.endsWith('"')) ||
      (decoded.startsWith("'") && decoded.endsWith("'")))
  ) {
    decoded = decoded.slice(1, -1);
  } else {
    decoded = decoded.replace(/^["']+/, "").replace(/["']+$/, "");
  }
  decoded = decodeCssEscapes(decoded);
  return isSafeUriValue(decoded);
}

function matchCssEntityQuote(str, index) {
  const slice = str.slice(index);
  const m = /^(?:&quot;|&apos;|&#0*(?:34|39);?|&#x0*(?:22|27);?)/i.exec(slice);
  if (!m) return null;
  const raw = m[0].toLowerCase();
  const kind = raw.includes("quot") || raw.includes("34") || raw.includes("22") ? '"' : "'";
  return { length: m[0].length, kind };
}

function skipCssWsAndComments(str, idx) {
  const len = str.length;
  let p = idx;
  while (p < len) {
    if (isHtmlWs(str[p])) {
      p += 1;
      continue;
    }
    if (str[p] === "/" && str[p + 1] === "*") {
      const endComment = str.indexOf("*/", p + 2);
      p = endComment === -1 ? len : endComment + 2;
      continue;
    }
    break;
  }
  return p;
}

const CSS_U = "(?:u|\\\\u|\\\\0*(?:75|55)(?:\\r\\n|[\\t\\n\\f\\r ])?)";
const CSS_R = "(?:r|\\\\r|\\\\0*(?:72|52)(?:\\r\\n|[\\t\\n\\f\\r ])?)";
const CSS_L = "(?:l|\\\\l|\\\\0*(?:6c|4c)(?:\\r\\n|[\\t\\n\\f\\r ])?)";
const CSS_URL_OPEN_STICKY_RE = new RegExp(`${CSS_U}${CSS_R}${CSS_L}[ \\t\\n\\f\\r]*\\(`, "yi");

function sanitizeStyleValue(rawStyle, { decodeHtmlEntities = false } = {}) {
  const rawStr = String(rawStyle);
  const mapped = decodeHtmlEntities ? normalizeHtmlEntitiesWithMap(rawStr) : null;
  const str = mapped ? mapped.norm : rawStr;
  const origStart = mapped ? mapped.origStart : null;
  const origEnd = mapped ? mapped.origEnd : null;
  const toOrigStart = (idx) => (origStart ? (idx < str.length ? origStart[idx] : rawStr.length) : idx);
  const toOrigEnd = (endIdx) =>
    origEnd ? (endIdx > 0 ? origEnd[endIdx - 1] : 0) : endIdx;

  const len = str.length;
  let out = "";
  let cursor = 0;
  let origCursor = 0;

  while (cursor < len) {
    const ch = str[cursor];

    // Skip CSS /* ... */ comments before scanning for url(...)
    if (ch === "/" && str[cursor + 1] === "*") {
      const endComment = str.indexOf("*/", cursor + 2);
      const nextCursor = endComment === -1 ? len : endComment + 2;
      const nextOrig = toOrigEnd(nextCursor);
      out += rawStr.slice(origCursor, nextOrig);
      cursor = nextCursor;
      origCursor = nextOrig;
      continue;
    }

    // Skip CSS "..." / '...' quoted strings before scanning for url(...)
    if (ch === '"' || ch === "'") {
      let p = cursor + 1;
      while (p < len) {
        if (str[p] === "\\") {
          if (str[p + 1] === "\r" && str[p + 2] === "\n") {
            p += 3;
          } else {
            p = Math.min(p + 2, len);
          }
          continue;
        }
        if (str[p] === "\n" || str[p] === "\r" || str[p] === "\f") {
          break;
        }
        if (str[p] === ch) {
          p += 1;
          break;
        }
        p += 1;
      }
      const nextOrig = toOrigEnd(p);
      out += rawStr.slice(origCursor, nextOrig);
      cursor = p;
      origCursor = nextOrig;
      continue;
    }

    // Match CSS url(...) at a valid identifier boundary
    if (
      (ch === "u" || ch === "U" || ch === "\\") &&
      (cursor === 0 || !/[a-zA-Z0-9_-]/.test(str[cursor - 1]))
    ) {
      CSS_URL_OPEN_STICKY_RE.lastIndex = cursor;
      const urlMatch = CSS_URL_OPEN_STICKY_RE.exec(str);
      if (urlMatch) {
        const matchStart = cursor;
        const origMatchStart = toOrigStart(matchStart);
        let p = skipCssWsAndComments(str, matchStart + urlMatch[0].length);

        const targetStart = p;
        let targetEnd = p;
        let fullEnd = -1;

        if (p < len && (str[p] === '"' || str[p] === "'")) {
          const q = str[p];
          let isBadString = false;
          p += 1;
          while (p < len) {
            if (str[p] === "\\") {
              if (str[p + 1] === "\r" && str[p + 2] === "\n") {
                p += 3;
              } else {
                p = Math.min(p + 2, len);
              }
              continue;
            }
            if (str[p] === "\n" || str[p] === "\r" || str[p] === "\f") {
              isBadString = true;
              break;
            }
            if (str[p] === q) {
              p += 1;
              break;
            }
            p += 1;
          }
          targetEnd = p;
          if (!isBadString) {
            p = skipCssWsAndComments(str, p);
            if (p < len && str[p] === ")") {
              fullEnd = p + 1;
            }
          }
        } else {
          const openEntity = p < len && str[p] === "&" ? matchCssEntityQuote(str, p) : null;
          if (openEntity) {
            p += openEntity.length;
            while (p < len) {
              if (str[p] === "&") {
                const closeEntity = matchCssEntityQuote(str, p);
                if (closeEntity && closeEntity.kind === openEntity.kind) {
                  p += closeEntity.length;
                  break;
                }
              }
              p += 1;
            }
            targetEnd = p;
            p = skipCssWsAndComments(str, p);
            if (p < len && str[p] === ")") {
              fullEnd = p + 1;
            }
          } else {
            let parenDepth = 0;
            while (p < len) {
              const c = str[p];
              if (c === "(") {
                parenDepth += 1;
              } else if (c === ")") {
                if (parenDepth === 0) {
                  targetEnd = p;
                  fullEnd = p + 1;
                  break;
                }
                parenDepth -= 1;
              } else if (parenDepth === 0 && (c === "}" || c === '"' || c === "'")) {
                targetEnd = p;
                break;
              } else if (parenDepth === 0 && c === ";") {
                const prefix = str.slice(targetStart, p);
                const isHtmlEntityEnd = /&(?:#\d+|#x[0-9a-f]+|[a-z]+)$/i.test(prefix);
                const isDataUri = /^data:/i.test(decodeBasicHtmlEntities(prefix).trim());
                if (!isHtmlEntityEnd && !isDataUri) {
                  targetEnd = p;
                  break;
                }
              }
              p += 1;
            }
            if (fullEnd === -1 && p >= len) {
              targetEnd = len;
            }
          }
        }

        if (fullEnd === -1) {
          // Unclosed or malformed url(...): still check up to targetEnd/EOF for disallowed schemes
          const candidateEnd = Math.max(targetEnd, p);
          const rawCandidate = str.slice(targetStart, candidateEnd);
          if (!isSafeCssUrlTarget(rawCandidate)) {
            out += "url(#blocked)";
            cursor = candidateEnd;
            origCursor = toOrigEnd(candidateEnd);
          } else {
            const matchEnd = matchStart + urlMatch[0].length;
            const origMatchEnd = toOrigEnd(matchEnd);
            out += rawStr.slice(origMatchStart, origMatchEnd);
            cursor = matchEnd;
            origCursor = origMatchEnd;
          }
          continue;
        }

        const rawTarget = str.slice(targetStart, targetEnd);
        const origFullEnd = toOrigEnd(fullEnd);
        if (!isSafeCssUrlTarget(rawTarget)) {
          out += "url(#blocked)";
        } else {
          out += rawStr.slice(origMatchStart, origFullEnd);
        }
        cursor = fullEnd;
        origCursor = origFullEnd;
        continue;
      }
    }

    // Consume CSS backslash escape outside strings/comments so \" cannot open a string
    if (ch === "\\") {
      const nextCursor =
        str[cursor + 1] === "\r" && str[cursor + 2] === "\n"
          ? cursor + 3
          : Math.min(cursor + 2, len);
      const nextOrig = toOrigEnd(nextCursor);
      out += rawStr.slice(origCursor, nextOrig);
      cursor = nextCursor;
      origCursor = nextOrig;
      continue;
    }

    let next = cursor + 1;
    while (next < len) {
      const c = str[next];
      if (c === "/" || c === '"' || c === "'" || c === "\\" || c === "u" || c === "U") {
        break;
      }
      next += 1;
    }
    const nextOrig = toOrigStart(next);
    out += rawStr.slice(origCursor, nextOrig);
    cursor = next;
    origCursor = nextOrig;
  }

  return out;
}

/**
 * Finds the closing `>` of an HTML tag starting immediately after its tag name,
 * honoring quoted attribute values and strict HTML5 ASCII whitespace `[ \t\n\f\r]`.
 * Returns index after `>`, or `-1` if unclosed at EOF.
 */
function findTagCloseEnd(html, startPos) {
  const len = html.length;
  let pos = startPos;
  while (pos < len) {
    while (pos < len && (isHtmlWs(html[pos]) || html[pos] === "/")) {
      if (html[pos] === "/" && html[pos + 1] === ">") {
        pos += 1;
        break;
      }
      pos += 1;
    }
    if (pos >= len) return -1;
    if (html[pos] === ">") return pos + 1;

    // HTML5 §13.2.5.32: unexpected '=' before attribute name starts an attribute named '='
    if (html[pos] === "=") {
      pos += 1;
    }
    while (
      pos < len &&
      !isHtmlWs(html[pos]) &&
      html[pos] !== "=" &&
      html[pos] !== "/" &&
      html[pos] !== ">"
    ) {
      pos += 1;
    }
    while (pos < len && isHtmlWs(html[pos])) {
      pos += 1;
    }
    if (pos < len && html[pos] === "=") {
      pos += 1;
      while (pos < len && isHtmlWs(html[pos])) {
        pos += 1;
      }
      if (pos < len && (html[pos] === '"' || html[pos] === "'")) {
        const q = html[pos];
        pos += 1;
        while (pos < len && html[pos] !== q) {
          pos += 1;
        }
        if (pos < len && html[pos] === q) {
          pos += 1;
        }
      } else {
        while (pos < len && !isHtmlWs(html[pos]) && html[pos] !== ">") {
          pos += 1;
        }
      }
    }
  }
  return -1;
}

/**
 * Locates the matching HTML5 RAWTEXT/RCDATA end tag (`</tagName ...>`) starting at `bodyStart`.
 * Returns `{ bodyEnd, closeEnd }` or `null` if unclosed before EOF.
 */
function findRawtextCloseTag(html, bodyStart, lowerTagName) {
  const closeRe = new RegExp(`</${lowerTagName}(?=[ \\t\\n\\f\\r/>])`, "gi");
  closeRe.lastIndex = bodyStart;
  const match = closeRe.exec(html);
  if (!match) return null;
  const afterName = match.index + 2 + lowerTagName.length;
  const closeEnd = findTagCloseEnd(html, afterName);
  if (closeEnd === -1) return null;
  return {
    bodyEnd: match.index,
    closeEnd,
  };
}

/**
 * Locates the closing `</script ...>` boundary of a `<script>` inside `<svg>` content.
 * Per HTML5 §13.2.6.5, `<svg><script>` enters HTML Script data state where only `</script`
 * (or EOF) exits the script element. Returns `{ bodyEnd, closeEnd }` or `null` at EOF.
 */
function findForeignScriptCloseTag(html, bodyStart) {
  return findRawtextCloseTag(html, bodyStart, "script");
}

/**
 * Strict allowlist HTML/SVG tag tokenizer & attribute sanitizer.
 * - Enforces `HTML_ALLOWED_TAGS` outside `<svg>` and `SVG_ALLOWED_TAGS` inside `<svg>`.
 * - Uses a flat `svgTagStack` (zero `<math>`, zero SVG HTML-integration points, zero non-body insertion modes).
 * - Neutralizes `<` inside HTML `<style>` (`\00003c`) and `<`/`>` inside `<title>`/`<textarea>` (`&lt;`/`&gt;`).
 * - Strips `on*` handlers, `srcdoc`, `<meta http-equiv=...>`, and non-allowlisted URI schemes / CSS `url(...)`.
 */
function sanitizeHtmlTags(html, { allowUnclosedRawtextToEof = true } = {}) {
  const len = html.length;
  let out = "";
  let cursor = 0;
  const svgTagStack = [];

  while (cursor < len) {
    const start = html.indexOf("<", cursor);
    if (start === -1) {
      out += html.slice(cursor);
      break;
    }

    out += html.slice(cursor, start);

    // HTML comments (`<!-- ... -->`, including HTML5 short-circuits `<!-->`, `<!--->`, and `--!>`)
    if (html.startsWith("<!--", start)) {
      if (html.startsWith("<!-->", start)) {
        out += "<!-->";
        cursor = start + 5;
        continue;
      }
      if (html.startsWith("<!--->", start)) {
        out += "<!--->";
        cursor = start + 6;
        continue;
      }
      const endNormal = html.indexOf("-->", start + 4);
      const endBang = html.indexOf("--!>", start + 4);
      let commentEnd = -1;
      if (endNormal !== -1 && (endBang === -1 || endNormal <= endBang)) {
        commentEnd = endNormal + 3;
      } else if (endBang !== -1) {
        commentEnd = endBang + 4;
      }
      if (commentEnd === -1) {
        out += `${html.slice(start)}-->`;
        cursor = len;
      } else {
        out += html.slice(start, commentEnd);
        cursor = commentEnd;
      }
      continue;
    }

    // Foreign CDATA (`<![CDATA[...]]>`) inside <svg> is escaped as plain text; outside <svg>, preserve <!DOCTYPE ...> and drop bogus <!...> comments
    if (html.startsWith("<!", start)) {
      if (svgTagStack.length > 0 && html.startsWith("<![CDATA[", start)) {
        const cdataEnd = html.indexOf("]]>", start + 9);
        if (cdataEnd === -1) {
          out += escapeHtml(html.slice(start + 9));
          cursor = len;
        } else {
          out += escapeHtml(html.slice(start + 9, cdataEnd));
          cursor = cdataEnd + 3;
        }
        continue;
      }
      const gt = html.indexOf(">", start + 2);
      const isDoctype = /^<!doctype(?=[ \t\n\f\r>])/i.test(html.slice(start, start + 10));
      if (gt === -1) {
        if (isDoctype) {
          out += `${html.slice(start)}>`;
        }
        cursor = len;
      } else {
        if (isDoctype) {
          out += html.slice(start, gt + 1);
        }
        cursor = gt + 1;
      }
      continue;
    }

    // HTML5 bogus comment (`<?...`, including ZXing `<?xml ...?>`) closes at first `>`
    if (html.startsWith("<?", start)) {
      const gt = html.indexOf(">", start + 2);
      if (gt === -1) {
        out += `${html.slice(start)}>`;
        cursor = len;
      } else {
        out += html.slice(start, gt + 1);
        cursor = gt + 1;
      }
      continue;
    }

    // End tags (`</tag ...>`) and HTML5 bogus-comment end tags (`</1...`, `</ ...`)
    if (start + 1 < len && html[start + 1] === "/") {
      if (start + 2 >= len) {
        cursor = len;
        continue;
      }
      if (html[start + 2] === ">") {
        cursor = start + 3;
        continue;
      }
      if (!/[a-zA-Z]/.test(html[start + 2])) {
        const gt = html.indexOf(">", start + 2);
        if (gt === -1) {
          out += `${html.slice(start)}>`;
          cursor = len;
        } else {
          out += html.slice(start, gt + 1);
          cursor = gt + 1;
        }
        continue;
      }

      let endPos = start + 2;
      while (
        endPos < len &&
        !isHtmlWs(html[endPos]) &&
        html[endPos] !== "/" &&
        html[endPos] !== ">"
      ) {
        endPos += 1;
      }
      const endTagLower = html.slice(start + 2, endPos).toLowerCase();
      const closeEnd = findTagCloseEnd(html, endPos);
      if (closeEnd === -1) {
        cursor = len;
        continue;
      }

      if (svgTagStack.length > 0) {
        if (!SVG_ALLOWED_TAGS.has(endTagLower)) {
          cursor = closeEnd;
          continue;
        }
        let matchIdx = -1;
        for (let i = svgTagStack.length - 1; i >= 0; i -= 1) {
          if (svgTagStack[i] === endTagLower) {
            matchIdx = i;
            break;
          }
        }
        if (matchIdx !== -1) {
          svgTagStack.length = matchIdx;
          out += `</${endTagLower}>`;
        }
        cursor = closeEnd;
        continue;
      }

      // Outside <svg>: drop disallowed end tags and stray </svg>, </style>, </title>, </textarea>
      if (
        !HTML_ALLOWED_TAGS.has(endTagLower) ||
        endTagLower === "svg" ||
        endTagLower === "style" ||
        endTagLower === "title" ||
        endTagLower === "textarea"
      ) {
        cursor = closeEnd;
        continue;
      }

      out += html.slice(start, closeEnd);
      cursor = closeEnd;
      continue;
    }

    if (start + 1 >= len || !/[a-zA-Z]/.test(html[start + 1])) {
      out += "<";
      cursor = start + 1;
      continue;
    }

    let pos = start + 1;
    while (pos < len && !isHtmlWs(html[pos]) && html[pos] !== "/" && html[pos] !== ">") {
      pos += 1;
    }
    const tagName = html.slice(start + 1, pos);
    const lowerTagName = tagName.toLowerCase();

    // Prevent nested-tag smuggling such as `<scr<script>ipt>` or `<ma<math>th>` when the inner tag will be dropped
    const activeAllowlist = svgTagStack.length > 0 ? SVG_ALLOWED_TAGS : HTML_ALLOWED_TAGS;
    if (lowerTagName.includes("<")) {
      const lastLt = lowerTagName.lastIndexOf("<");
      const innerTag = lowerTagName.slice(lastLt + 1);
      if (!activeAllowlist.has(innerTag)) {
        const splitAt = start + 1 + lastLt;
        out += html.slice(start, splitAt);
        cursor = splitAt;
        continue;
      }
    }

    const keptAttrs = [];
    let hasHttpEquiv = false;
    let hadAnyAttr = false;
    let selfClosing = false;
    let spaceBeforeSelfClose = false;
    let closed = false;

    while (pos < len) {
      // Skip strict HTML5 ASCII whitespace and '/' separators (recording self-closing '/>')
      let sawWsBeforeSlash = false;
      while (pos < len && (isHtmlWs(html[pos]) || html[pos] === "/")) {
        if (isHtmlWs(html[pos])) {
          sawWsBeforeSlash = true;
        }
        if (html[pos] === "/" && html[pos + 1] === ">") {
          selfClosing = true;
          spaceBeforeSelfClose = sawWsBeforeSlash;
          pos += 1;
          break;
        }
        pos += 1;
      }

      if (pos >= len) break;

      if (html[pos] === ">") {
        closed = true;
        pos += 1;
        break;
      }

      const nameStart = pos;
      // HTML5 §13.2.5.32: unexpected '=' before attribute name starts an attribute named '='
      if (html[pos] === "=") {
        pos += 1;
      }
      while (
        pos < len &&
        !isHtmlWs(html[pos]) &&
        html[pos] !== "=" &&
        html[pos] !== "/" &&
        html[pos] !== ">"
      ) {
        pos += 1;
      }
      const attrName = html.slice(nameStart, pos);
      if (!attrName) {
        pos += 1;
        continue;
      }
      hadAnyAttr = true;
      const lowerAttrName = attrName.toLowerCase();

      while (pos < len && isHtmlWs(html[pos])) {
        pos += 1;
      }

      let hasValue = false;
      let quote = "";
      let attrValue = "";

      if (pos < len && html[pos] === "=") {
        hasValue = true;
        pos += 1;
        while (pos < len && isHtmlWs(html[pos])) {
          pos += 1;
        }

        if (pos < len && (html[pos] === '"' || html[pos] === "'")) {
          quote = html[pos];
          pos += 1;
          const valStart = pos;
          while (pos < len && html[pos] !== quote) {
            pos += 1;
          }
          attrValue = html.slice(valStart, pos);
          if (pos < len && html[pos] === quote) {
            pos += 1;
          }
        } else {
          // HTML5 §13.2.5.38: quotes inside unquoted attribute values are literal until ASCII whitespace or '>'
          const valStart = pos;
          while (pos < len && !isHtmlWs(html[pos]) && html[pos] !== ">") {
            pos += 1;
          }
          attrValue = html.slice(valStart, pos);
        }
      }

      if (lowerTagName === "meta" && lowerAttrName === "http-equiv") {
        hasHttpEquiv = true;
      }

      // Drop any event handler attribute (including quote-prefixed malformed names) and srcdoc
      const normalizedHandlerName = lowerAttrName.replace(/^["']+/, "");
      if (/^on[a-z0-9_-]*$/.test(normalizedHandlerName) || lowerAttrName === "srcdoc") {
        continue;
      }

      if (hasValue && URI_ATTR_NAMES.has(lowerAttrName)) {
        if (!isSafeUriValue(attrValue)) {
          keptAttrs.push(`${attrName}="#"`);
          continue;
        }
      }

      if (hasValue && lowerAttrName === "style") {
        const safeStyle = sanitizeStyleValue(attrValue, { decodeHtmlEntities: true });
        keptAttrs.push(quote ? `${attrName}=${quote}${safeStyle}${quote}` : `${attrName}=${safeStyle}`);
        continue;
      }

      if (!hasValue) {
        keptAttrs.push(attrName);
      } else if (quote) {
        keptAttrs.push(`${attrName}=${quote}${attrValue}${quote}`);
      } else {
        keptAttrs.push(`${attrName}=${attrValue}`);
      }
    }

    cursor = pos;

    // Unclosed tag at EOF is ignored per HTML5 §13.2.5.34
    if (!closed) {
      continue;
    }

    const closeSuffix = selfClosing
      ? spaceBeforeSelfClose && (keptAttrs.length > 0 || !hadAnyAttr)
        ? " />"
        : "/>"
      : ">";

    // Inside <svg> sandbox: drop <script> body + any non-SVG_ALLOWED_TAGS start tag
    if (svgTagStack.length > 0) {
      if (lowerTagName === "script") {
        if (!selfClosing) {
          const match = findForeignScriptCloseTag(html, cursor);
          if (match) {
            cursor = match.closeEnd;
          } else if (allowUnclosedRawtextToEof) {
            cursor = len;
          } else {
            const nextLt = html.indexOf("<", cursor);
            cursor = nextLt === -1 ? len : nextLt;
          }
        }
        continue;
      }
      if (!SVG_ALLOWED_TAGS.has(lowerTagName)) {
        continue;
      }
      if (!selfClosing) {
        svgTagStack.push(lowerTagName);
      }
      const attrsPart = keptAttrs.length > 0 ? ` ${keptAttrs.join(" ")}` : "";
      out += `<${tagName}${attrsPart}${closeSuffix}`;
      continue;
    }

    // Outside <svg>
    if (lowerTagName === "meta" && hasHttpEquiv) {
      continue;
    }

    if (RAWTEXT_DROPPED_HTML_TAGS.has(lowerTagName)) {
      const match = findRawtextCloseTag(html, cursor, lowerTagName);
      if (match) {
        cursor = match.closeEnd;
      } else if (allowUnclosedRawtextToEof) {
        cursor = len;
      } else {
        const nextLt = html.indexOf("<", cursor);
        cursor = nextLt === -1 ? len : nextLt;
      }
      continue;
    }

    if (!HTML_ALLOWED_TAGS.has(lowerTagName)) {
      continue;
    }

    const attrsPart = keptAttrs.length > 0 ? ` ${keptAttrs.join(" ")}` : "";

    if (lowerTagName === "svg") {
      if (!selfClosing) {
        svgTagStack.push("svg");
      }
      out += `<${tagName}${attrsPart}${closeSuffix}`;
      continue;
    }

    // HTML5 RAWTEXT/RCDATA elements (<style>, <title>, <textarea>) neutralize `<` so breakout is impossible
    if (lowerTagName === "style" || lowerTagName === "title" || lowerTagName === "textarea") {
      out += `<${tagName}${attrsPart}>`;
      const match = findRawtextCloseTag(html, cursor, lowerTagName);
      if (!match) {
        const rawRest = html.slice(cursor);
        if (lowerTagName === "style") {
          out += `${sanitizeStyleValue(rawRest).replace(/</g, "\\00003c")}</style>`;
        } else {
          out += `${rawRest.replace(/</g, "&lt;").replace(/>/g, "&gt;")}</${lowerTagName}>`;
        }
        cursor = len;
      } else {
        const rawBody = html.slice(cursor, match.bodyEnd);
        const closeTag = html.slice(match.bodyEnd, match.closeEnd);
        if (lowerTagName === "style") {
          out += sanitizeStyleValue(rawBody).replace(/</g, "\\00003c") + closeTag;
        } else {
          out += rawBody.replace(/</g, "&lt;").replace(/>/g, "&gt;") + closeTag;
        }
        cursor = match.closeEnd;
      }
      continue;
    }

    out += `<${tagName}${attrsPart}${closeSuffix}`;
  }

  // Auto-close any unclosed <svg> frames at EOF
  while (svgTagStack.length > 0) {
    out += `</${svgTagStack.pop()}>`;
  }

  return out;
}

function sanitizePrintHtmlPure(html) {
  if (html === null || html === undefined) return "";
  let out = String(html);
  let prev;
  let passes = 0;
  const maxPasses = Math.max(64, out.length + 2);

  do {
    prev = out;
    out = sanitizeHtmlTags(out, { allowUnclosedRawtextToEof: passes === 0 });
    passes += 1;
  } while (out !== prev && passes < maxPasses);

  if (out !== prev) {
    return escapeHtml(out);
  }

  return out;
}

function escapeDomAttrValue(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function serializeDomAttributes(el) {
  const parts = [];
  for (let i = 0; i < el.attributes.length; i += 1) {
    const attr = el.attributes[i];
    const attrName = attr.name;
    if (!/^[a-zA-Z_:][a-zA-Z0-9_.:-]*$/.test(attrName)) {
      continue;
    }
    const lowerAttr = attrName.toLowerCase();
    if (/^on[a-z0-9_-]*$/.test(lowerAttr) || lowerAttr === "srcdoc") {
      continue;
    }
    if (URI_ATTR_NAMES.has(lowerAttr)) {
      if (!isSafeUriValue(attr.value)) {
        parts.push(`${attrName}="#"`);
      } else {
        parts.push(`${attrName}="${escapeDomAttrValue(attr.value)}"`);
      }
      continue;
    }
    if (lowerAttr === "style") {
      const safeStyle = sanitizeStyleValue(attr.value, { decodeHtmlEntities: false });
      parts.push(`${attrName}="${escapeDomAttrValue(safeStyle)}"`);
      continue;
    }
    parts.push(`${attrName}="${escapeDomAttrValue(attr.value)}"`);
  }
  return parts.length > 0 ? ` ${parts.join(" ")}` : "";
}

function serializeSanitizedDomNode(node) {
  if (!node) return "";
  if (node.nodeType === 3) {
    return escapeHtml(node.nodeValue || "");
  }
  if (node.nodeType === 8) {
    const safeComment = String(node.nodeValue || "").replace(/--!?>/g, "");
    return `<!--${safeComment}-->`;
  }
  if (node.nodeType !== 1) {
    return "";
  }

  const ns = node.namespaceURI;
  const localName = String(node.localName || node.nodeName || "").toLowerCase();

  if (ns === "http://www.w3.org/2000/svg") {
    if (!SVG_ALLOWED_TAGS.has(localName)) {
      return "";
    }
    const attrsStr = serializeDomAttributes(node);
    const childrenHtml = serializeSanitizedDomChildren(node);
    if (
      !childrenHtml &&
      localName !== "svg" &&
      localName !== "g" &&
      localName !== "defs" &&
      localName !== "text"
    ) {
      return `<${localName}${attrsStr}/>`;
    }
    return `<${localName}${attrsStr}>${childrenHtml}</${localName}>`;
  }

  if (ns !== "http://www.w3.org/1999/xhtml" || !HTML_ALLOWED_TAGS.has(localName)) {
    return "";
  }

  if (localName === "meta" && node.hasAttribute("http-equiv")) {
    return "";
  }

  const attrsStr = serializeDomAttributes(node);

  if (localName === "style") {
    const safeCss = sanitizeStyleValue(node.textContent || "").replace(/</g, "\\00003c");
    return `<style${attrsStr}>${safeCss}</style>`;
  }

  if (localName === "title" || localName === "textarea") {
    return `<${localName}${attrsStr}>${escapeHtml(node.textContent || "")}</${localName}>`;
  }

  if (HTML_VOID_TAGS.has(localName)) {
    return `<${localName}${attrsStr}>`;
  }

  return `<${localName}${attrsStr}>${serializeSanitizedDomChildren(node)}</${localName}>`;
}

function serializeSanitizedDomChildren(parent) {
  if (!parent || !parent.childNodes) return "";
  let out = "";
  for (let i = 0; i < parent.childNodes.length; i += 1) {
    out += serializeSanitizedDomNode(parent.childNodes[i]);
  }
  return out;
}

/**
 * Browser/WebView2 defense-in-depth DOMParser pass using Blink's real HTML5 parser.
 * Walks the parsed DOM tree with the exact same allowlists and `<`-neutralization rules.
 */
function sanitizeWithDomParser(html) {
  if (typeof DOMParser !== "function" || !html) return html;
  try {
    const isFullDocument = /^\s*(?:<!doctype\s+html\b|<html\b)/i.test(html);
    const hasHtmlDoctype = /^\s*<!doctype\s+html\b/i.test(html);
    const doc = new DOMParser().parseFromString(html, "text/html");
    if (!doc || !doc.documentElement) return html;

    if (isFullDocument) {
      const prefix = hasHtmlDoctype || doc.doctype?.name === "html" ? "<!DOCTYPE html>" : "";
      const htmlAttrs = serializeDomAttributes(doc.documentElement);
      const headHtml = doc.head ? serializeSanitizedDomNode(doc.head) : "";
      const bodyHtml = doc.body ? serializeSanitizedDomNode(doc.body) : "";
      return `${prefix}<html${htmlAttrs}>${headHtml}${bodyHtml}</html>`;
    }

    const headChildren = doc.head ? serializeSanitizedDomChildren(doc.head) : "";
    const bodyChildren = doc.body ? serializeSanitizedDomChildren(doc.body) : "";
    return `${headChildren}${bodyChildren}`;
  } catch {
    return html;
  }
}

/**
 * Non-oscillating defense-in-depth sanitizer for HTML passed to printSlipHtml / printHtmlViaIframe.
 * Enforces strict HTML and SVG tag allowlists (no <math>, no SVG HTML integration points, no
 * <select>/<colgroup>/<template>/<frameset> insertion modes), HTML RAWTEXT/RCDATA `<`-neutralization,
 * strict URI scheme allowlisting, and dual-layer Browser DOMParser validation when available.
 */
export function sanitizePrintHtml(html) {
  if (html === null || html === undefined) return "";
  let out = sanitizePrintHtmlPure(html);
  if (typeof DOMParser === "function") {
    out = sanitizeWithDomParser(out);
    out = sanitizePrintHtmlPure(out);
  }
  return out;
}

/**
 * Returns true if an HTTP status indicates invalid/expired/revoked authentication.
 */
export function isAuthInvalidStatus(status) {
  return status === 401 || status === 403;
}

/**
 * Classifies how the axios 401 response interceptor should handle an unauthorized response.
 * Never triggers `/api/session/extend` on 401 (which would fail with an expired bearer token).
 *
 * @returns {'ignore' | 'force_logout' | 'prompt_reauth'}
 */
export function classifyAuth401Action({ status, url, token, pathname } = {}) {
  if (status !== 401) return "ignore";
  const normalizedUrl = String(url || "");
  if (normalizedUrl.includes("/login") || normalizedUrl.includes("/session/extend")) {
    return "ignore";
  }
  const hasToken = Boolean(token && String(token).trim());
  const cleanPath = String(pathname || "/").split("?")[0].split("#")[0] || "/";
  if (!hasToken || cleanPath === "/") {
    return "force_logout";
  }
  return "prompt_reauth";
}

/**
 * Determines whether SessionExpiryModal should display proactive `'warning'` (Wait 10 Minutes)
 * or `'reauth'` (inline password re-authentication).
 *
 * @returns {'warning' | 'reauth'}
 */
export function resolveSessionModalMode({ reason, nowMs, expiryMs, extendStatus } = {}) {
  if (reason === "unauthorized" || reason === "reauth" || isAuthInvalidStatus(extendStatus)) {
    return "reauth";
  }
  if (Number.isFinite(expiryMs) && expiryMs > 0 && Number.isFinite(nowMs) && nowMs >= expiryMs) {
    return "reauth";
  }
  return "warning";
}

/**
 * Determines whether AuthContext's `/api/user` hydration failure should clear stored credentials.
 * Clears auth ONLY on HTTP 401 or non-terminal HTTP 403. Preserves session on transient
 * 5xx, 429, 408, network errors, timeouts, cancellations, and 403 TERMINAL_UNAUTHORIZED.
 */
export function shouldClearAuthOnUserFetchError(err) {
  if (!err) return false;
  if (
    err.code === "ERR_CANCELED" ||
    err.name === "CanceledError" ||
    err.name === "AbortError"
  ) {
    return false;
  }
  const status = err?.response?.status;
  if (status === 401) return true;
  if (status === 403) {
    const code = err?.response?.data?.code;
    return code !== "TERMINAL_UNAUTHORIZED";
  }
  return false;
}

/**
 * Synchronizes desktop terminal metadata into storage without persisting `karobar_pairing_token`.
 * Under desktop mode, any stale `karobar_pairing_token` in storage is proactively removed.
 */
export function syncDesktopNetworkStorage({ isDesktop, cfg, storage } = {}) {
  if (!storage) return;
  if (isDesktop) {
    try {
      storage.removeItem?.("karobar_pairing_token");
    } catch {
      // ignore storage errors
    }
  }
  if (!cfg || typeof cfg !== "object") return;
  try {
    if (cfg.terminalId) {
      storage.setItem?.("karobar_terminal_uid", cfg.terminalId);
    }
    if (cfg.terminalName) {
      storage.setItem?.("karobar_terminal_name", cfg.terminalName);
    }
    if (cfg.mode) {
      storage.setItem?.("karobar_terminal_mode", cfg.mode);
    }
    if (cfg.hostname) {
      storage.setItem?.("karobar_terminal_hostname", cfg.hostname);
    }
  } catch {
    // ignore storage errors
  }
}

/**
 * Returns the `X-Network-Token` header value for browser LAN mode, or `""` in desktop mode.
 * Under desktop mode, proactively scrubs any stale `karobar_pairing_token` from storage
 * because the native desktop host injects `X-Network-Token` authoritatively.
 */
export function getBrowserPairingTokenHeader({ isDesktop, storage } = {}) {
  if (isDesktop) {
    try {
      storage?.removeItem?.("karobar_pairing_token");
    } catch {
      // ignore storage errors
    }
    return "";
  }
  try {
    return String(storage?.getItem?.("karobar_pairing_token") || "").trim();
  } catch {
    return "";
  }
}
