import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  escapeHtml,
  sanitizeCssAlign,
  sanitizePrintHtml,
} from "../../resources/js/utils/securityHelpers.js";
import { generateBarcodeSvgString } from "../../resources/js/utils/barcode.js";

test("escapeHtml encodes &, <, >, \", ' and preserves 0 and false", () => {
  assert.equal(escapeHtml(null), "");
  assert.equal(escapeHtml(undefined), "");
  assert.equal(escapeHtml(0), "0");
  assert.equal(escapeHtml(false), "false");
  assert.equal(
    escapeHtml(`<script>alert("xss's & 1")</script>`),
    "&lt;script&gt;alert(&quot;xss&#39;s &amp; 1&quot;)&lt;/script&gt;"
  );
});

test("sanitizeCssAlign allowlists alignment values and rejects CSS/HTML breakouts", () => {
  assert.equal(sanitizeCssAlign("LEFT"), "left");
  assert.equal(sanitizeCssAlign("  right "), "right");
  assert.equal(sanitizeCssAlign("center"), "center");
  assert.equal(sanitizeCssAlign("Justify"), "justify");
  assert.equal(sanitizeCssAlign("</style><script>alert(1)</script>"), "center");
  assert.equal(sanitizeCssAlign("center; background:url(javascript:alert(1))", "left"), "left");
  assert.equal(sanitizeCssAlign(null, "right"), "right");
});

test("sanitizePrintHtml strips scripts, dangerous embeds, inline handlers, and dangerous URIs while preserving styles, SVGs, data:image, and plain text", () => {
  const dirty = `
    <!DOCTYPE html>
    <html>
      <head>
        <style>body { text-align: center; color: #111; }</style>
        <base href="https://evil.example/">
        <meta http-equiv="Refresh" content="0;url=https://evil.example/">
      </head>
      <body onload="alert(1)">
        <script>alert('paired')</script>
        <scr<script>ipt>alert('nested')</scr</script>ipt>
        <iframe src="https://evil.example"></iframe>
        <object data="x"></object>
        <embed src="x" />
        <applet code="Evil"></applet>
        <img src="data:image/png;base64,iVBORw0KGgo=" onerror="alert(2)" alt="Logo">
        <img/onerror=alert(3) src=x>
        <a href="java&#x09;script:alert(4)">Click</a>
        <a href="vbscript:msgbox(1)">VB</a>
        <a href="data:text/html,<script>alert(5)</script>">DataHTML</a>
        <svg viewBox="0 0 100 20"><rect width="10" height="20" onload="alert(6)"/></svg>
        <p style="background:url(javascript:alert(7)); color:#000">Note: onclick=safe literal text in receipt &amp; 100% safe</p>
        <script>alert('unclosed')
      </body>
    </html>
  `;

  const clean = sanitizePrintHtml(dirty);

  // Active execution payloads stripped
  assert.doesNotMatch(clean, /<script\b/i);
  assert.doesNotMatch(clean, /<\/script/i);
  assert.doesNotMatch(clean, /<iframe\b/i);
  assert.doesNotMatch(clean, /<object\b/i);
  assert.doesNotMatch(clean, /<embed\b/i);
  assert.doesNotMatch(clean, /<applet\b/i);
  assert.doesNotMatch(clean, /<base\b/i);
  assert.doesNotMatch(clean, /http-equiv\s*=\s*"?refresh/i);
  assert.doesNotMatch(clean, /\bonload\s*=/i);
  assert.doesNotMatch(clean, /\bonerror\s*=/i);
  assert.doesNotMatch(clean, /javascript:/i);
  assert.doesNotMatch(clean, /vbscript:/i);
  assert.doesNotMatch(clean, /data:text\/html/i);

  // Legitimate printable assets and plain text preserved
  assert.match(clean, /<style>body \{ text-align: center; color: #111; \}<\/style>/);
  assert.match(clean, /data:image\/png;base64,iVBORw0KGgo=/);
  assert.match(clean, /<svg viewBox="0 0 100 20"><rect width="10" height="20"\s*\/>/);
  assert.match(clean, /Note: onclick=safe literal text in receipt/);
});

test("sanitizePrintHtml blocks quote-adjacent on*, backtick unquoted values, out-of-range entities, entity meta refresh, SVG animations, dangerous URIs, unclosed EOF tags, and 10-deep nested scripts", () => {
  // 1. No whitespace after quoted attribute
  const q1 = sanitizePrintHtml('<img src="x"onerror="alert(1)">');
  assert.equal(q1, '<img src="x">');
  const q2 = sanitizePrintHtml("<img src='x'onerror=alert(1)>");
  assert.equal(q2, "<img src='x'>");
  const q3 = sanitizePrintHtml('<svg id="s"onload="alert(1)"></svg>');
  assert.equal(q3, '<svg id="s"></svg>');

  // 2. Backtick in unquoted attribute value
  const bt = sanitizePrintHtml("<img src=x onerror=`${alert(1)}`>");
  assert.doesNotMatch(bt, /onerror/i);
  assert.doesNotMatch(bt, /alert/i);
  assert.equal(bt, "<img src=x>");

  // 3. Out-of-range numeric HTML entities must not throw RangeError
  assert.doesNotThrow(() => {
    const outHex = sanitizePrintHtml('<a href="&#x110000;javascript:alert(1)">x</a>');
    const outDec = sanitizePrintHtml('<a href="&#99999999;javascript:alert(1)">x</a>');
    assert.match(outHex, /^<a href=/);
    assert.match(outDec, /^<a href=/);
  });
  const nullEntity = sanitizePrintHtml('<a href="java&#0;script:alert(1)">x</a>');
  assert.equal(nullEntity, '<a href="#">x</a>');

  // 4. Entity-encoded <meta http-equiv="&#114;efresh"> stripped while charset & print-config preserved
  const metaRefresh = sanitizePrintHtml(
    '<meta charset="utf-8"><meta name="karobar-print-config" content="{}"><meta http-equiv="&#114;efresh" content="0;url=javascript:alert(1)">'
  );
  assert.doesNotMatch(metaRefresh, /http-equiv/i);
  assert.doesNotMatch(metaRefresh, /javascript:/i);
  assert.match(metaRefresh, /<meta charset="utf-8">/);
  assert.match(metaRefresh, /<meta name="karobar-print-config" content="\{\}">/);

  // 5. Extended URI attributes (poster, background, cite, dynsrc, lowsrc) & srcdoc & dangerous data: MIME types
  const uriPayloads = sanitizePrintHtml(
    '<video poster="javascript:alert(1)"></video>' +
      '<div background="vbscript:msgbox(1)" srcdoc="<script>alert(1)</script>"></div>' +
      '<blockquote cite="javascript:alert(1)"></blockquote>' +
      '<img dynsrc="javascript:alert(1)" lowsrc="data:text/xml,<x/>">' +
      '<a href="data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+">svg</a>' +
      '<a href="data:application/xhtml+xml,<html/>">xhtml</a>'
  );
  assert.doesNotMatch(uriPayloads, /javascript:/i);
  assert.doesNotMatch(uriPayloads, /vbscript:/i);
  assert.doesNotMatch(uriPayloads, /srcdoc/i);
  assert.doesNotMatch(uriPayloads, /data:image\/svg\+xml/i);
  assert.doesNotMatch(uriPayloads, /data:application\/xhtml\+xml/i);
  assert.doesNotMatch(uriPayloads, /data:text\/xml/i);

  // 6. SVG declarative animation event/href injection stripped while safe SVG shapes preserved
  const svgAnim = sanitizePrintHtml(
    '<svg><rect width="10" height="10"/><set attributeName="onload" to="alert(1)" /><animate attributeName="href" to="javascript:alert(1)"></animate><animateTransform onbegin="alert(1)"/><animateMotion onend="alert(1)"/></svg>'
  );
  assert.doesNotMatch(svgAnim, /<set\b/i);
  assert.doesNotMatch(svgAnim, /<animate/i);
  assert.match(svgAnim, /<svg><rect width="10" height="10"\s*\/><\/svg>/);

  // 7. Unclosed HTML tags at EOF
  const eofImg = sanitizePrintHtml("<img src=x onerror=alert(1)");
  assert.doesNotMatch(eofImg, /onerror/i);
  assert.doesNotMatch(eofImg, /alert/i);
  const eofScript = sanitizePrintHtml("<p>ok</p><script src=https://evil.example/x.js");
  assert.equal(eofScript, "<p>ok</p>");

  // 8. 10-deep nested <script>
  const deepScript =
    "<scr".repeat(10) + "<script>alert(1)</script>" + "ipt>alert(1)</script>".repeat(10);
  const cleanDeep = sanitizePrintHtml(deepScript);
  assert.doesNotMatch(cleanDeep, /<script/i);
  assert.doesNotMatch(cleanDeep, /alert\(1\)/i);

  // 9. Positive preservation: <style>, title="onclick=1", raster data:image/*
  const preserved = sanitizePrintHtml(
    '<style>body{font-size:12px}</style><div title="onclick=1">ok</div><img src="data:image/png;base64,AAA"><img src="data:image/jpeg;base64,BBB">'
  );
  assert.match(preserved, /<style>body\{font-size:12px\}<\/style>/);
  assert.match(preserved, /<div title="onclick=1">ok<\/div>/);
  assert.match(preserved, /data:image\/png;base64,AAA/);
  assert.match(preserved, /data:image\/jpeg;base64,BBB/);
});

test("SessionExpiryModal tracks modeRef synchronously so warning timer skips ensureBackendGrace in reauth mode", () => {
  const modalSrc = fs.readFileSync(
    path.resolve("resources/js/components/SessionExpiryModal.jsx"),
    "utf8"
  );
  assert.match(modalSrc, /modeRef\s*=\s*useRef\(/);
  assert.match(modalSrc, /modeRef\.current\s*===\s*"reauth"\s*\?\s*"reauth"\s*:\s*nextMode/);
  assert.match(modalSrc, /if\s*\(resolvedMode\s*===\s*"warning"\)\s*\{\s*ensureBackendGrace\(\)/);
});

test("Cash Drawer & Barcode print templates use escapeHtml/sanitizeCssAlign and neutralize stored XSS payloads", () => {
  const cashDrawerSrc = fs.readFileSync(
    path.resolve("resources/js/components/CashDrawerModal.jsx"),
    "utf8"
  );
  const barcodeSrc = fs.readFileSync(
    path.resolve("resources/js/pages/Barcodes/Index.jsx"),
    "utf8"
  );
  const printHelperSrc = fs.readFileSync(
    path.resolve("resources/js/utils/printHelper.js"),
    "utf8"
  );

  // Verify printHelper sanitizes before printing
  assert.match(printHelperSrc, /sanitizePrintHtml/);

  // Verify CashDrawerModal escapes interpolated fields and Barcodes imports shared helpers
  assert.match(cashDrawerSrc, /from "@\/utils\/securityHelpers\.js"/);
  assert.match(cashDrawerSrc, /escapeHtml\(descText\)/);
  assert.match(cashDrawerSrc, /escapeHtml\(safeCashierName|safeCashierName = escapeHtml/);
  assert.match(barcodeSrc, /escapeHtml,\s*sanitizeCssAlign\s*\}\s*from "@\/utils\/securityHelpers\.js"/);
  assert.match(barcodeSrc, /safeCurrencySymbol = escapeHtml\(storeInfo\.currency_symbol/);
  assert.doesNotMatch(barcodeSrc, /String\(str \|\| ""\)/);

  // Simulate payload escaping as used in Cash Drawer & Barcode templates
  const xssPayload = `<img src=x onerror=alert(1)>`;
  const escapedNote = escapeHtml(xssPayload);
  assert.equal(escapedNote.includes("<img"), false);
  assert.equal(escapedNote, "&lt;img src=x onerror=alert(1)&gt;");

  const alignBreakout = sanitizeCssAlign(`</style><script>alert(1)</script>`, "center");
  assert.equal(alignBreakout, "center");
});

test("sanitizePrintHtml prevents HTML comment and RAWTEXT/RCDATA parser-differential mXSS while preserving benign CSS", () => {
  // 1. HTML comments containing unclosed tag quotes must not swallow subsequent elements
  const commentBreakout = sanitizePrintHtml('<!-- <a title=" --><img src=x onerror=alert(1)>');
  assert.equal(commentBreakout, '<!-- <a title=" --><img src=x>');
  const bangComment = sanitizePrintHtml('<!-- <a title=" --!><img src=x onerror=alert(1)>');
  assert.equal(bangComment, '<!-- <a title=" --!><img src=x>');
  const shortComment = sanitizePrintHtml("<!--><img src=x onerror=alert(1)>");
  assert.equal(shortComment, "<!--><img src=x>");

  // 2. <style> RAWTEXT: quotes and `<` inside CSS neutralize `<` to `\00003c` and must not swallow </style>
  assert.equal(
    sanitizePrintHtml('<style>/* <a title=" */ </style><img src=x onerror=alert(1)>'),
    '<style>/* \\00003ca title=" */ </style><img src=x>'
  );
  assert.equal(
    sanitizePrintHtml('<style>.x::before { content: "<a title=\'"; }</style><img src=x onerror=alert(1)>'),
    '<style>.x::before { content: "\\00003ca title=\'"; }</style><img src=x>'
  );
  assert.equal(
    sanitizePrintHtml('<style>.a > .b { color: red; content: "a<b"; }</style>'),
    '<style>.a > .b { color: red; content: "a\\00003cb"; }</style>'
  );
  assert.equal(
    sanitizePrintHtml('<style><img src="</style><img src=x onerror=alert(1)>"></style>'),
    '<style>\\00003cimg src="</style><img src=x>">'
  );
  assert.equal(
    sanitizePrintHtml('<style>body{background:url(javascript:alert(1));}</style>'),
    '<style>body{background:url(#blocked);}</style>'
  );
  assert.equal(
    sanitizePrintHtml('<style>body{background:url("data:image/png;base64,AAA");}</style>'),
    '<style>body{background:url("data:image/png;base64,AAA");}</style>'
  );

  // 3. RCDATA elements (<textarea>, <title>) neutralize `<`/`>` and legacy RAWTEXT (<xmp>, <noembed>, <noframes>) are dropped
  for (const tag of ["textarea", "title"]) {
    const payload = `<${tag}><a title="</${tag}><img src=x onerror=alert(1)>"></a></${tag}>`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, /onerror/i);
    assert.match(cleaned, /<img src=x>/);
    assert.ok(!cleaned.slice(`<${tag}>`.length, cleaned.indexOf(`</${tag}>`)).includes("<"));
  }
  for (const tag of ["xmp", "noembed", "noframes"]) {
    const payload = `<${tag}><a title="</${tag}><img src=x onerror=alert(1)>"></a></${tag}>`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, new RegExp(`<${tag}\\b`, "i"));
    assert.doesNotMatch(cleaned, /onerror/i);
  }

  // 4. Inside <svg>, disallowed tags (<style>, <div>, <img>, <a>) are strictly stripped
  const svgStyle = sanitizePrintHtml("<svg><style><img src=x onerror=alert(1)></style></svg>");
  assert.doesNotMatch(svgStyle, /onerror|<style|<img/i);
  assert.equal(svgStyle, "<svg></svg>");

  const svgBreakoutStyle = sanitizePrintHtml(
    '<svg><div><style><a title="</style><img src=x onerror=alert(1)>"></style></div></svg>'
  );
  assert.doesNotMatch(svgBreakoutStyle, /onerror|<div|<style|<img/i);
  assert.equal(svgBreakoutStyle, "<svg></svg>");
});

test("sanitizePrintHtml neutralizes HTML-entity-quoted and obfuscated CSS url(...) payloads", () => {
  const payloads = [
    '<div style="background:url(&quot;javascript:alert(1)&quot;)">x</div>',
    '<div style="background:url(&#34;javascript:alert(1)&#34;)">x</div>',
    '<div style="background:url(&#x22;javascript:alert(1)&#x22;)">x</div>',
    '<div style="background:url(&apos;javascript:alert(1)&apos;)">x</div>',
    '<div style="background:url(&#x27;javascript:alert(1)&#x27;)">x</div>',
    '<div style="background:url(&quot;data:image/svg+xml,<svg onload=alert(1)>&quot;)">x</div>',
    '<div style="background:url(java&#x0a;script:alert(1))">x</div>',
    '<div style="background:url&#40;javascript:alert(1)&#41;">x</div>',
    '<div style="background:url&#40;data:image/svg+xml,<svg onload=alert(1)>&#41;">x</div>',
    '<div style="background:u&#x72;l(javascript:alert(1))">x</div>',
    '<div style="background:url&lpar;javascript:alert(1)&rpar;">x</div>',
  ];

  for (const payload of payloads) {
    const cleaned = sanitizePrintHtml(payload);
    assert.equal(cleaned, '<div style="background:url(#blocked)">x</div>');
  }

  // CSS-escaped url(...) and backslash-newline string line continuations
  assert.equal(
    sanitizePrintHtml("<style>body{background:\\75rl(javascript:alert(1))}</style>"),
    "<style>body{background:url(#blocked)}</style>"
  );
  assert.equal(
    sanitizePrintHtml('<style>body{background:url("java\\\nscript:alert(1)")}</style>'),
    "<style>body{background:url(#blocked)}</style>"
  );
  assert.equal(
    sanitizePrintHtml('<style>body{background:url("java\\\r\nscript:alert(1)")}</style>'),
    "<style>body{background:url(#blocked)}</style>"
  );

  // Benign entity-quoted style attributes remain intact
  assert.equal(
    sanitizePrintHtml(
      '<div style="font-family:&quot;Inter&quot;;background:url(&quot;data:image/png;base64,AAA&quot;)">x</div>'
    ),
    '<div style="font-family:&quot;Inter&quot;;background:url(&quot;data:image/png;base64,AAA&quot;)">x</div>'
  );
});

test("sanitizePrintHtml unconditionally strips <math>, MathML elements, and SVG HTML integration points", () => {
  // 1. <math> and MathML tags are stripped; <style>/<textarea>/<title> neutralize `<`, and <xmp>/<noembed>/<noframes> are dropped
  for (const tag of ["style", "textarea", "title"]) {
    const payload = `<math><${tag}><img src=x onerror=alert(1)></${tag}></math>`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, /<(?:math|img)\b/i);
    assert.ok(!cleaned.slice(`<${tag}>`.length, cleaned.indexOf(`</${tag}>`)).includes("<"));
  }
  for (const tag of ["xmp", "noembed", "noframes"]) {
    const payload = `<math><${tag}><img src=x onerror=alert(1)></${tag}></math>`;
    const cleaned = sanitizePrintHtml(payload);
    assert.equal(cleaned, "");
  }

  // 2. MathML text integration points (<mi>, <mo>, <mn>, <ms>, <mtext>) are stripped
  for (const ip of ["mi", "mo", "mn", "ms", "mtext"]) {
    const payload = `<math><${ip}><style>/* <a title=" */</style><img src=x onerror=alert(1)></${ip}></math>`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, new RegExp(`<(?:math|${ip})\\b`, "i"));
    assert.doesNotMatch(cleaned, /onerror/i);
    assert.match(cleaned, /<img src=x>/);
  }

  // 3. <annotation-xml> is stripped regardless of encoding
  for (const enc of ["text/html", "TEXT/HTML", "application/xhtml+xml", "application/mathml-presentation+xml"]) {
    const payload = `<math><annotation-xml encoding="${enc}"><style>/* <a title=" */</style><img src=x onerror=alert(1)></annotation-xml></math>`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, /<(?:math|annotation-xml)\b/i);
    assert.doesNotMatch(cleaned, /onerror/i);
  }

  // 4. SVG HTML integration points (<desc>, <foreignObject>, <title>) are stripped inside <svg>
  const descMismatch = sanitizePrintHtml(
    '<svg><desc></title><style><img src="</style><img src=x onerror=alert(1)>"></style></desc></svg>'
  );
  assert.doesNotMatch(descMismatch, /onerror|<desc|<style|<img/i);
  assert.equal(descMismatch, "<svg></svg>");

  const foreignObjMismatch = sanitizePrintHtml(
    '<svg><foreignObject></desc><style><img src="</style><img src=x onerror=alert(1)>"></style></foreignObject></svg>'
  );
  assert.doesNotMatch(foreignObjMismatch, /onerror|<foreignObject|<style|<img/i);
  assert.equal(foreignObjMismatch, "<svg></svg>");

  const svgCloseInDesc = sanitizePrintHtml(
    '<svg><desc></svg></desc><style><a title="</style><img src=x onerror=alert(1)>"></style></svg>'
  );
  assert.doesNotMatch(svgCloseInDesc, /onerror|<desc/i);
  assert.match(svgCloseInDesc, /<img src=x>/);
});

test("sanitizePrintHtml handles HTML5 bogus comments, DOCTYPE, self-closing RAWTEXT/RCDATA tags, and leading '=' in closing tags", () => {
  // 1. HTML5 bogus comments & <!DOCTYPE close at first '>' without quote scanning
  const bogusVectors = [
    '<? <a title="><img src=x onerror=alert(1)>">',
    '<! <a title="><img src=x onerror=alert(1)>">',
    '</1 <a title="><img src=x onerror=alert(1)>">',
    '</ <a title="><img src=x onerror=alert(1)>">',
    '<!DOCTYPE <a title="><img src=x onerror=alert(1)>">',
    '</div title="><a title="><img src=x onerror=alert(1)>">',
  ];
  for (const vector of bogusVectors) {
    const cleaned = sanitizePrintHtml(vector);
    assert.doesNotMatch(cleaned, /onerror/i);
    assert.match(cleaned, /<img src=x>/);
  }
  assert.equal(sanitizePrintHtml("<!DOCTYPE html><p>ok</p>"), "<!DOCTYPE html><p>ok</p>");

  // 2. Self-closing HTML RAWTEXT/RCDATA tags (<style>, <textarea>, <title>) emit '>'; legacy <xmp>/<noembed>/<noframes> are dropped
  for (const tag of ["style", "textarea", "title"]) {
    const payload = `<${tag}/><a title="</${tag}><img src=x onerror=alert(1)>">`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, /onerror/i);
    assert.match(cleaned, /<img src=x>/);
    assert.ok(cleaned.startsWith(`<${tag}>`), `Expected ${tag} to emit '>' instead of '/>'`);
  }
  for (const tag of ["xmp", "noembed", "noframes"]) {
    const payload = `<${tag}/><a title="</${tag}><img src=x onerror=alert(1)>">`;
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, new RegExp(`<${tag}\\b`, "i"));
    assert.doesNotMatch(cleaned, /onerror/i);
  }

  // Foreign <svg><style/> and <svg><img> are strictly stripped inside <svg>
  assert.equal(
    sanitizePrintHtml("<svg><style/><img src=x onerror=alert(1)></svg>"),
    "<svg></svg>"
  );

  // 3. Disallowed tags inside <svg> and <math> never emit active handlers or raw `<` in <style>
  for (const breakout of ["<div/>", "<br/>", '<font color="red"/>']) {
    const svgOut = sanitizePrintHtml(
      `<svg>${breakout}<style><img src="</style><img src=x onerror=alert(1)>"></style>`
    );
    assert.doesNotMatch(svgOut, /onerror|<style|<img/i);
    assert.equal(svgOut, "<svg></svg>");

    const mathOut = sanitizePrintHtml(
      `<math>${breakout}<style><img src="</style><img src=x onerror=alert(1)>"></style>`
    );
    assert.doesNotMatch(mathOut, /<math/i);
    assert.doesNotMatch(mathOut, /onerror/i);
    assert.match(mathOut, /<img src=x>/);
  }

  // 4. Leading '=' before attribute name in RAWTEXT/RCDATA end tags (HTML5 §13.2.5.32)
  assert.equal(
    sanitizePrintHtml('<style>x</style ="><img src=x onerror=alert(1)>'),
    '<style>x</style ="><img src=x>'
  );
  assert.equal(
    sanitizePrintHtml('<textarea>x</textarea ="><img src=x onerror=alert(1)>'),
    '<textarea>x</textarea ="><img src=x>'
  );
});

test("sanitizePrintHtml strips dangerous tags with unquoted attribute quotes, enforces strict HTML5 ASCII whitespace, neutralizes SVG CDATA & integration tags, and handles CSS escape/comment/string boundaries", () => {
  // 1. Dangerous tags with unquoted quotes (x=a'b) stripped directly inside HTML5 tokenizer
  const unquotedQuotePayloads = [
    `<script x=a'b>alert(1)</script x=">`,
    `<script src=https://evil.example/x.js x=a'b></script x=">`,
    `<object data="data:text/html,<img src=x onerror=alert(1)>" x=a'b></object>`,
    `<embed src=https://evil.example/x.swf x=a'b>`,
    `<applet code=Evil.class x=a'b></applet>`,
    `<noscript x=a'b><img src=x onerror=alert(1)></noscript x=a"b>`,
    `<base href=https://evil.example/ x=a'b>`,
    `<iframe src=https://evil.example x=a'b></iframe>`,
    `<svg><set attributeName=onload to=alert(1) x=a'b></svg>`,
    `<svg><animate attributeName=href to=javascript:alert(1) x=a'b></svg>`,
    `<svg><animateTransform onbegin=alert(1) x=a'b/></svg>`,
    `<svg><animateMotion onend=alert(1) x=a'b></animateMotion></svg>`,
  ];
  for (const payload of unquotedQuotePayloads) {
    const cleaned = sanitizePrintHtml(payload);
    assert.doesNotMatch(cleaned, /<(?:script|object|embed|applet|noscript|base|iframe|set|animate|animateTransform|animateMotion)\b/i);
    assert.doesNotMatch(cleaned, /onerror|onload|onbegin|onend|javascript:/i);
  }

  // 2. Non-HTML5 whitespace (\u000b, \u00a0, \u2028, \ufeff) must NOT be skipped before attribute values
  for (const ws of ["\u000b", "\u00a0", "\u2028", "\ufeff"]) {
    const styleVec = sanitizePrintHtml(`<style>x</style title=${ws}"><img src=x onerror=alert(1)>`);
    assert.doesNotMatch(styleVec, /onerror/i);
    assert.match(styleVec, /<img src=x>/);

    const textareaVec = sanitizePrintHtml(
      `<textarea>x</textarea title=${ws}"><img src=x onerror=alert(1)>`
    );
    assert.doesNotMatch(textareaVec, /onerror/i);
    assert.match(textareaVec, /<img src=x>/);

    const attrVec = sanitizePrintHtml(`<a title=${ws}"><img src=x onerror=alert(1)>">`);
    assert.doesNotMatch(attrVec, /onerror/i);
    assert.match(attrVec, /<img src=x>/);
  }

  // 3. SVG CDATA is escaped as text and integration/MathML tags never appear in output
  const integrationVectors = [
    `<svg><desc><![CDATA[a><a title="]]><img src=x onerror=alert(1)>]]></desc></svg>`,
    `<svg><foreignObject><![CDATA[a><a title="]]><img src=x onerror=alert(1)>]]></foreignObject></svg>`,
    `<math><mtext><![CDATA[a><a title="]]><img src=x onerror=alert(1)>]]></mtext></math>`,
    `<svg><desc><div><![CDATA[a><a title="]]>"><img src=x onerror=alert(1)>`,
    `<svg><desc><desc></desc><style><a title="</style><img src=x onerror=alert(1)>"></style>`,
    `<svg><foreignObject><foreignObject></foreignObject><style><a title="</style><img src=x onerror=alert(1)>"></style>`,
    `<math><mtext><mtext></mtext><style><a title="</style><img src=x onerror=alert(1)>"></style>`,
    `<math><mi><mi></mi><style><a title="</style><img src=x onerror=alert(1)>"></style>`,
    `<svg><desc><p><math></svg><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><div><svg></math><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><div></desc><style><a title="</style><img src=x onerror=alert(1)>"></style>`,
    `<svg><desc><p><math><mi><svg></p><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><p><mglyph><style><img src="</style><img src=x onerror=alert(1)>">`,
    `<math><mtext><div><malignmark><style><img src="</style><img src=x onerror=alert(1)>">`,
    `<math><mtext><table><mglyph><style><img src="</style><img src=x onerror=alert(1)>">`,
  ];
  for (const vec of integrationVectors) {
    const cleaned = sanitizePrintHtml(vec);
    assert.doesNotMatch(cleaned, /<(?:math|mtext|mi|mglyph|malignmark|desc|foreignObject)\b/i, `Forbidden tag survived: ${vec}`);
    assert.doesNotMatch(cleaned, /<!\[CDATA\[/i, `Raw CDATA survived: ${vec}`);
    assert.doesNotMatch(cleaned, /<img\b[^>]*onerror/i, `Active onerror survived: ${vec}`);
    const styleMatch = /<style>([\s\S]*?)<\/style>/i.exec(cleaned);
    if (styleMatch) {
      assert.ok(!styleMatch[1].includes("<"), `Raw '<' survived inside <style> for: ${vec}`);
    }
  }

  // 4. CSS decodeCssEscapes hex+line-continuation ordering and comment/string url( scanning
  assert.equal(
    sanitizePrintHtml('<style>body{background:url("java\\73\\\ncript:alert(1)")}</style>'),
    "<style>body{background:url(#blocked)}</style>"
  );
  assert.equal(
    sanitizePrintHtml('<style>/* url(" */ body{background:url(javascript:alert(1))} /* ") */</style>'),
    '<style>/* url(" */ body{background:url(#blocked)} /* ") */</style>'
  );
  assert.equal(
    sanitizePrintHtml(
      `<style>.a::before{content:"url('"} body{background:url(javascript:alert(1));content:"')"}</style>`
    ),
    `<style>.a::before{content:"url('"} body{background:url(#blocked);content:"')"}</style>`
  );
  assert.equal(
    sanitizePrintHtml(
      `<div style="content:'url(&quot;';background:url(javascript:alert(1));content:'&quot;)'">x</div>`
    ),
    `<div style="content:'url(&quot;';background:url(#blocked);content:'&quot;)'">x</div>`
  );
});

test("sanitizePrintHtml blocks all ADV-* / I6-* / non-foreign insertion-mode vectors, enforces strict URI allowlist, and preserves real print templates", () => {
  // 1. All 8 Reviewer r6 probes (ADV-1..4, ADV-8..11) + all 20 Challenger r6 probes (I6-1..I6-8c, I6-23*)
  const r6Vectors = [
    `<svg><desc><span></svg></span></desc><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><span></math></span></mtext><style><img src=x onerror=alert(1)></style>`,
    `<svg><foreignObject><b></svg></b></foreignObject><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><table><div></table></div></mtext><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><select><div/></select><mglyph><xmp><img src=x onerror=alert(1)></xmp>`,
    `<math><mtext><table><tr><td></table></mtext><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><select><table></select><mglyph><xmp><img src=x onerror=alert(1)></xmp>`,
    `<math><mtext><table><caption><div></table></mtext><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><p><math><annotation-xml></p><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><div><p></div></desc><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><div><h1></div></desc><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><section><p></section></desc><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><button><p></button></desc><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><ul><li><p></ul></desc><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><select><div></select><mglyph><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><select><p></select><mglyph><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><table><td><div></table><mglyph><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><table><caption><div></table><mglyph><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><table><tr><td><div></table><mglyph><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><table><th><div></table><mglyph><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><p><hr></mtext><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><p><xmp></xmp></mtext><style><img src=x onerror=alert(1)></style>`,
    `<svg><desc><math></br><style><a title="</style><img src=x onerror=alert(1)>">`,
    `<svg><desc><math></p><style><a title="</style><img src=x onerror=alert(1)>">`,
    `<math><mtext><svg></br><style><a title="</style><img src=x onerror=alert(1)>">`,
    `<math><mtext><svg></p><style><a title="</style><img src=x onerror=alert(1)>">`,
    `<math><mtext><form><form></form></mtext><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><form><div></form></div></mtext><style><img src=x onerror=alert(1)></style>`,
    `<math><mtext><form><p><form></p></mtext><style><img src=x onerror=alert(1)></style>`,
    `<math><annotation-xml encoding="application/xhtml&plus;xml"><style><a title="</style><img src=x onerror=alert(1)>">`,
    `<svg><desc><p><applet></p></desc><style><a title="</style><img src=x onerror=alert(1)>">`,
    `<svg><desc><p><object></p></desc><style><a title="</style><img src=x onerror=alert(1)>">`,
  ];

  // 2. Non-foreign HTML5 insertion-mode probes (<select>, <colgroup>, <frameset>, <plaintext>)
  const nonForeignVectors = [
    `<select><style></select><img src=x onerror=alert(1)></style>`,
    `<select><xmp></select><img src=x onerror=alert(1)></xmp>`,
    `<select><title></select><img src=x onerror=alert(1)></title>`,
    `<table><colgroup><style><img src=x onerror=alert(1)></style>`,
    `<frameset><style><img src=x onerror=alert(1)></style>`,
    `<plaintext><img src=x onerror=alert(1)>`,
  ];

  for (const vec of [...r6Vectors, ...nonForeignVectors]) {
    const cleaned = sanitizePrintHtml(vec);
    assert.doesNotMatch(
      cleaned,
      /<(?:math|mtext|mi|mo|mn|ms|mtext|mglyph|malignmark|annotation-xml|desc|foreignObject|select|xmp|noembed|noframes|colgroup|frameset|frame|plaintext|form|applet|object)\b/i,
      `Forbidden tag survived in: ${vec}`
    );
    assert.doesNotMatch(cleaned, /<img\b[^>]*onerror/i, `Executable onerror survived in: ${vec}`);
    const styleMatch = /<style>([\s\S]*?)<\/style>/i.exec(cleaned);
    if (styleMatch) {
      assert.ok(!styleMatch[1].includes("<"), `Raw '<' survived inside <style> for: ${vec}`);
    }
    const titleMatch = /<title>([\s\S]*?)<\/title>/i.exec(cleaned);
    if (titleMatch) {
      assert.ok(!titleMatch[1].includes("<"), `Raw '<' survived inside <title> for: ${vec}`);
    }
  }

  // 3. Strict URI scheme allowlist + &plus; entity decoding
  assert.equal(sanitizePrintHtml('<a href="data:image/svg&plus;xml,<svg onload=alert(1)>">x</a>'), '<a href="#">x</a>');
  assert.equal(
    sanitizePrintHtml('<div style="background:url(data:image/svg&plus;xml,<svg onload=alert(1)>)">x</div>'),
    '<div style="background:url(#blocked)">x</div>'
  );
  assert.equal(sanitizePrintHtml('<a href="file:///etc/passwd">x</a>'), '<a href="#">x</a>');
  assert.equal(sanitizePrintHtml('<img src="data:image/png+xml;base64,AAA">'), '<img src="#">');
  assert.equal(sanitizePrintHtml('<img src="/storage/logo.png">'), '<img src="/storage/logo.png">');
  assert.equal(sanitizePrintHtml('<a href="mailto:info@example.com">mail</a>'), '<a href="mailto:info@example.com">mail</a>');
  assert.equal(sanitizePrintHtml('<a href="tel:+923001234567">call</a>'), '<a href="tel:+923001234567">call</a>');
  assert.equal(sanitizePrintHtml('<img src="blob:https://app.local/123">'), '<img src="blob:https://app.local/123">');

  // 4. &#0; -> U+FFFD & CSS bad-string / legacy-entity / deep smuggling checks
  assert.equal(sanitizePrintHtml('<a href="java&#0;script:alert(1)">x</a>'), '<a href="#">x</a>');
  assert.equal(
    sanitizePrintHtml('<div style="background:url(java&#0;script:alert(1))">x</div>'),
    '<div style="background:url(#blocked)">x</div>'
  );

  for (const depth of [63, 64, 200]) {
    const nestedScript =
      "<s" + "<o".repeat(depth) + "<object>" + "bject>".repeat(depth) + "cript>alert(1)</script>";
    assert.equal(sanitizePrintHtml(nestedScript), "");

    const nestedImg =
      "<i" + "<o".repeat(depth) + "<object>" + "bject>".repeat(depth) + "mg src=x onerror=alert(1)>";
    assert.equal(sanitizePrintHtml(nestedImg), "<img src=x>");

    const nestedSvgScript =
      "<svg><s" +
      "<o".repeat(depth) +
      "<object>" +
      "bject>".repeat(depth) +
      "cript>alert(1)</script></svg>";
    assert.equal(sanitizePrintHtml(nestedSvgScript), "<svg></svg>");
  }

  for (const nl of ["\n", "\r", "\f"]) {
    const cssBadString = `<style>.a{background:url("foo${nl})} body{background:url(javascript:alert(1))} .b{content:")"}</style>`;
    const cleanedCss = sanitizePrintHtml(cssBadString);
    assert.doesNotMatch(cleanedCss, /javascript:/i);
    assert.match(cleanedCss, /url\(#blocked\)/);
  }
  const attrCssBadString = sanitizePrintHtml(
    '<div style="background:url(&#x22;foo&#10;);background:url(javascript:alert(1));content:&#x22;)&#x22;">x</div>'
  );
  assert.doesNotMatch(attrCssBadString, /javascript:/i);
  assert.match(attrCssBadString, /url\(#blocked\)/);

  const fontQuoteAdj = sanitizePrintHtml(
    '<svg><script><font title="x"color=red></script><img src=x onerror=alert(1)>'
  );
  assert.doesNotMatch(fontQuoteAdj, /onerror/i);
  assert.equal(fontQuoteAdj, "<svg></svg>");

  const fontQuotedSafe = sanitizePrintHtml(
    '<svg><script><font title="color=red">alert(1)</script><img src=x onerror=alert(1)>'
  );
  assert.doesNotMatch(fontQuotedSafe, /alert\(1\)/i);
  assert.equal(fontQuotedSafe, "<svg></svg>");

  assert.equal(
    sanitizePrintHtml(
      '<div style="background:url(&quot data:image/svg+xml,<svg onload=alert(1)>&quot)">x</div>'
    ),
    '<div style="background:url(#blocked)">x</div>'
  );
  assert.equal(
    sanitizePrintHtml('<a href="https://example.com?a=1&amp=2&ampfoo=3">x</a>'),
    '<a href="https://example.com?a=1&amp=2&ampfoo=3">x</a>'
  );

  // 5. Byte-for-byte preservation of real print templates (Cash Drawer, Barcode SVG, PrinterSetting, PurchaseOrder, FBR SVG, ZXing SVG)
  const cashDrawerTemplate = `<!DOCTYPE html><html><head><title>Cash Drawer Shift Report #12</title><style>@media print { body { margin: 0; } }</style></head><body><div class="header"><h1 class="title">Cash Register Shift Report</h1></div><table class="summary-table"><tr><td style="text-align: right; color: #10b981;">+ Cash Sales:</td></tr></table></body></html>`;
  assert.equal(sanitizePrintHtml(cashDrawerTemplate), cashDrawerTemplate);

  const barcodeSvg = generateBarcodeSvgString("1234567890", { showText: true });
  assert.equal(sanitizePrintHtml(barcodeSvg), barcodeSvg);

  const calibrationTemplate = `<!DOCTYPE html><html><head><meta charset="UTF-8" /><meta name="karobar-print-config" content="{&quot;paperWidthMm&quot;:80}" /><title>Calibration</title><style>@page { size: 80mm auto; margin: 0; }</style></head><body><div class="ruler">80mm<br/>OK</div></body></html>`;
  assert.equal(sanitizePrintHtml(calibrationTemplate), calibrationTemplate);

  const purchaseOrderTemplate = `<!DOCTYPE html><html><head><meta charset="utf-8"/><title>PO #101</title><style>@page { size: A4; }</style></head><body><h1>Purchase Order</h1><table><thead><tr><th>Item</th></tr></thead><tbody><tr><td style="width:85%">Rice 5kg</td></tr></tbody></table></body></html>`;
  assert.equal(sanitizePrintHtml(purchaseOrderTemplate), purchaseOrderTemplate);

  const fbrSvgBadge = `<svg width="112" height="76" viewBox="0 0 140 95" xmlns="http://www.w3.org/2000/svg" style="display:block;"><rect x="2" y="2" width="136" height="91" rx="8" fill="#fff" stroke="#111" stroke-width="2"/><circle cx="24" cy="24" r="10" fill="#16a34a"/><path d="M18 24 L22 28 L30 20" stroke="#fff" fill="none"/><polygon points="100,10 110,30 90,30" fill="#111"/><line x1="10" y1="60" x2="130" y2="60" stroke="#ccc" stroke-dasharray="4 2"/><text x="70" y="80" text-anchor="middle" font-size="11" font-weight="700">FBR POS</text></svg>`;
  assert.equal(sanitizePrintHtml(fbrSvgBadge), fbrSvgBadge);

  const zxingQrSvg = `<?xml version="1.0" standalone="no"?><!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd"><svg xmlns="http://www.w3.org/2000/svg" version="1.2" baseProfile="tiny" shape-rendering="crispEdges" viewBox="0 0 33 33" width="120" height="120" style="background-color:#fff"><rect x="0" y="0" width="7" height="7" fill="rgb(0,0,0)"/><text x="16" y="31" text-anchor="middle">12345</text></svg>`;
  assert.equal(sanitizePrintHtml(zxingQrSvg), zxingQrSvg);
});
