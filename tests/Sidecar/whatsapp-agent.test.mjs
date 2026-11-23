import test from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

import {
  createAgentServer,
  hasJsonContentType,
  isAuthorized,
  isCrossOriginRequest,
  timingSafeMatch,
} from "../../scripts/whatsapp-agent.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const ROOT_DIR = path.resolve(__dirname, "..", "..");

function createMockReq({
  method = "GET",
  url = "/status",
  headers = {},
  body = null,
  remoteAddress = "127.0.0.1",
} = {}) {
  const req = new EventEmitter();
  req.method = method;
  req.url = url;
  req.headers = Object.fromEntries(
    Object.entries(headers).map(([k, v]) => [k.toLowerCase(), v])
  );
  req.socket = { remoteAddress };
  req.destroy = () => {};

  if (body !== null && req.headers["content-length"] === undefined) {
    const rawBuf = Buffer.isBuffer(body) ? body : Buffer.from(String(body), "utf8");
    req.headers["content-length"] = String(rawBuf.length);
  }

  queueMicrotask(() => {
    if (body !== null) {
      const buf = Buffer.isBuffer(body) ? body : Buffer.from(String(body), "utf8");
      req.emit("data", buf);
    }
    req.emit("end");
  });

  return req;
}

function createMockRes() {
  let statusCode = 0;
  let headers = {};
  const chunks = [];
  let resolveDone;
  const done = new Promise((resolve) => {
    resolveDone = resolve;
  });

  const res = {
    writableEnded: false,
    writeHead(code, hdrs = {}) {
      statusCode = code;
      headers = { ...hdrs };
    },
    end(chunk) {
      if (chunk !== undefined && chunk !== null) {
        chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(String(chunk), "utf8"));
      }
      res.writableEnded = true;
      resolveDone();
    },
  };

  return {
    res,
    done,
    get statusCode() {
      return statusCode;
    },
    get headers() {
      return headers;
    },
    json() {
      return JSON.parse(Buffer.concat(chunks).toString("utf8"));
    },
  };
}

async function dispatchMock(agent, reqOpts) {
  const req = createMockReq(reqOpts);
  const mockRes = createMockRes();
  await agent.requestHandler(req, mockRes.res);
  await mockRes.done;
  return mockRes;
}

function makeTempStorageDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "karobar-wa-sidecar-test-"));
}

test("ISS-05: timingSafeMatch rejects empty/mismatched keys and matches equal keys", () => {
  assert.equal(timingSafeMatch("", "secret"), false);
  assert.equal(timingSafeMatch("secret", ""), false);
  assert.equal(timingSafeMatch("short", "much-longer-secret-key"), false);
  assert.equal(timingSafeMatch("karobar_wa_abc123", "karobar_wa_abc123"), true);
});

test("ISS-05: fails closed (401) when WHATSAPP_AGENT_API_KEY is empty even on loopback", async () => {
  const tmpDir = makeTempStorageDir();
  const agent = createAgentServer({ apiKey: "", storageDir: tmpDir });
  try {
    const res = await dispatchMock(agent, {
      method: "GET",
      url: "/status",
      remoteAddress: "127.0.0.1",
    });
    assert.equal(res.statusCode, 401);
    assert.equal(res.json().ok, false);
  } finally {
    agent.dispose();
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
});

test("ISS-05: rejects wrong keys, non-loopback IPs, and accepts valid X-Api-Key or Bearer", async () => {
  const tmpDir = makeTempStorageDir();
  const apiKey = "karobar_wa_super_secret_key_12345";
  const agent = createAgentServer({ apiKey, storageDir: tmpDir, initialStatus: "qr_ready" });
  try {
    const badKey = await dispatchMock(agent, {
      url: "/status",
      headers: { "x-api-key": "karobar_wa_super" },
    });
    assert.equal(badKey.statusCode, 401);

    const nonLoopback = await dispatchMock(agent, {
      url: "/status",
      remoteAddress: "192.168.1.50",
      headers: { "x-api-key": apiKey },
    });
    assert.equal(nonLoopback.statusCode, 401);

    const validHeader = await dispatchMock(agent, {
      url: "/status",
      headers: { "x-api-key": apiKey },
    });
    assert.equal(validHeader.statusCode, 200);
    assert.equal(validHeader.json().ok, true);
    assert.equal(validHeader.json().status, "qr_ready");

    const validBearer = await dispatchMock(agent, {
      url: "/status",
      headers: { authorization: `Bearer ${apiKey}` },
    });
    assert.equal(validBearer.statusCode, 200);
    assert.equal(validBearer.json().ok, true);
  } finally {
    agent.dispose();
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
});

test("ISS-05: rejects Origin / Sec-Fetch-Site: cross-site with 403 and OPTIONS with 405", async () => {
  const tmpDir = makeTempStorageDir();
  const apiKey = "karobar_wa_test_key";
  const agent = createAgentServer({ apiKey, storageDir: tmpDir });
  try {
    const withOrigin = await dispatchMock(agent, {
      method: "GET",
      url: "/status",
      headers: { "x-api-key": apiKey, origin: "https://evil.example" },
    });
    assert.equal(withOrigin.statusCode, 403);
    assert.equal(withOrigin.headers["Access-Control-Allow-Origin"], undefined);

    const crossSite = await dispatchMock(agent, {
      method: "POST",
      url: "/restart",
      headers: { "x-api-key": apiKey, "sec-fetch-site": "cross-site" },
    });
    assert.equal(crossSite.statusCode, 403);

    const optionsRes = await dispatchMock(agent, {
      method: "OPTIONS",
      url: "/status",
      headers: { "x-api-key": apiKey },
    });
    assert.equal(optionsRes.statusCode, 405);
    assert.equal(optionsRes.headers["Access-Control-Allow-Origin"], undefined);

    assert.equal(isCrossOriginRequest({ headers: { origin: "http://localhost" } }), true);
    assert.equal(isCrossOriginRequest({ headers: { "sec-fetch-site": "same-origin" } }), false);
  } finally {
    agent.dispose();
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
});

test("ISS-05: enforces Content-Type: application/json on JSON routes while keeping bodyless action POSTs compatible", async () => {
  const tmpDir = makeTempStorageDir();
  const apiKey = "karobar_wa_test_key";
  let sockInitialized = false;
  const agent = createAgentServer({
    apiKey,
    storageDir: tmpDir,
    initialStatus: "connected",
    initialSock: {
      logout: async () => {},
      onWhatsApp: async () => [{ exists: true, jid: "923001234567@s.whatsapp.net" }],
      sendMessage: async () => ({ key: { id: "msg-1" } }),
      ev: { removeAllListeners() {} },
      end() {},
    },
    useMultiFileAuthStateImpl: async () => ({ state: {}, saveCreds: async () => {} }),
    fetchLatestWaWebVersionImpl: async () => ({ version: [2, 3000, 1], isLatest: false }),
    makeWASocketImpl: () => {
      sockInitialized = true;
      return {
        ev: { on() {}, removeAllListeners() {} },
        end() {},
      };
    },
  });

  try {
    // Reject text/plain on /pair-code, /send-document, /send-text with 415
    for (const route of ["/pair-code", "/send-document", "/send-text"]) {
      const badCt = await dispatchMock(agent, {
        method: "POST",
        url: route,
        headers: { "x-api-key": apiKey, "content-type": "text/plain" },
        body: "{}",
      });
      assert.equal(badCt.statusCode, 415, `${route} must reject text/plain`);
    }

    // Valid JSON on /send-text succeeds
    const okText = await dispatchMock(agent, {
      method: "POST",
      url: "/send-text",
      headers: { "x-api-key": apiKey, "content-type": "application/json; charset=utf-8" },
      body: JSON.stringify({ phone: "923001234567", text: "Hello" }),
    });
    assert.equal(okText.statusCode, 200);
    assert.equal(okText.json().sent, true);

    // Bodyless POST /restart (matching LocalWhatsAppSidecarManager.cs) succeeds, while text/plain fails with 415
    const badRestart = await dispatchMock(agent, {
      method: "POST",
      url: "/restart",
      headers: { "x-api-key": apiKey, "content-type": "application/x-www-form-urlencoded" },
      body: "foo=bar",
    });
    assert.equal(badRestart.statusCode, 415);

    const okRestart = await dispatchMock(agent, {
      method: "POST",
      url: "/restart",
      headers: { "x-api-key": apiKey },
    });
    assert.equal(okRestart.statusCode, 200);
    assert.equal(okRestart.json().ok, true);
    assert.equal(hasJsonContentType({ headers: { "content-type": "application/json; charset=utf-8" } }), true);
    assert.equal(sockInitialized, true);
  } finally {
    agent.dispose();
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
});

test("ISS-06: malformed req.url returns 400 cleanly instead of throwing ERR_INVALID_URL", async () => {
  const tmpDir = makeTempStorageDir();
  const apiKey = "karobar_wa_test_key";
  const agent = createAgentServer({ apiKey, storageDir: tmpDir });
  try {
    const res = await dispatchMock(agent, {
      method: "GET",
      url: "http://[::1",
      headers: { "x-api-key": apiKey },
    });
    assert.equal(res.statusCode, 400);
    assert.equal(res.json().ok, false);
    assert.match(res.json().message, /Malformed URL/i);

    const badJson = await dispatchMock(agent, {
      method: "POST",
      url: "/send-text",
      headers: { "x-api-key": apiKey, "content-type": "application/json" },
      body: "{not-json",
    });
    assert.equal(badJson.statusCode, 400);
    assert.match(badJson.json().message, /Invalid JSON/i);
  } finally {
    agent.dispose();
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
});

test("ISS-05: whatsapp-agent-spawn.cjs fails closed with exit code 1 when WHATSAPP_AGENT_API_KEY is empty", () => {
  const tmpDir = makeTempStorageDir();
  const logPath = path.join(tmpDir, "spawn.log");
  try {
    const proc = spawnSync(
      process.execPath,
      [path.join(ROOT_DIR, "scripts", "whatsapp-agent-spawn.cjs"), path.join(ROOT_DIR, "scripts", "whatsapp-agent.mjs"), logPath],
      {
        env: { ...process.env, WHATSAPP_AGENT_API_KEY: "" },
        encoding: "utf8",
      }
    );
    assert.equal(proc.status, 1);
    assert.match(proc.stderr, /WHATSAPP_AGENT_API_KEY/);
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
});
