#!/usr/bin/env node
/**
 * Build-time exporter that generates a PHP-readable knowledge index:
 *   resources/assistant/cube-knowledge.json
 *
 * Source of truth:
 *   - resources/js/data/guideBotKnowledge.js
 *   - resources/js/data/guideBotIntelligence.js
 *   - resources/js/data/guideBotScreenBlueprints.js
 *
 * Security & boundary guarantees:
 *   - Never includes runtime DOM values, customer data, or input values.
 *   - Explicitly strips CSS/DOM selectors (`target`, `targetSelector`) from workflow metadata.
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import {
  SCREEN_GUIDES,
  MODULE_CATEGORIES,
  MODAL_CONTEXT_GUIDES,
  PRODUCT_VERSION,
  TRAINED_ERP_INTENTS,
  DEMO_MANIFESTS,
  resolveWorkflowIdForIntent,
} from "../resources/js/data/guideBotKnowledge.js";
import { UNIVERSAL_CONTROL_DICTIONARY } from "../resources/js/data/guideBotScreenBlueprints.js";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const projectRoot = path.resolve(__dirname, "..");
const outputDir = path.join(projectRoot, "resources", "assistant");
const outputFile = path.join(outputDir, "cube-knowledge.json");

function sanitizeScreenGuide(guide) {
  const route = guide.key || "";
  const faqs = (guide.faqs || []).map((faq, idx) => ({
    id: `faq:${route}#${idx}`,
    route,
    screenTitle: {
      en: guide.title?.en || route,
      ur: guide.title?.ur || route,
    },
    q: {
      en: faq.q?.en || "",
      ur: faq.q?.ur || "",
    },
    a: {
      en: faq.a?.en || "",
      ur: faq.a?.ur || "",
    },
  }));

  const fields = (guide.fields || []).map((field) => ({
    term: {
      en: field.term?.en || "",
      ur: field.term?.ur || "",
    },
    type: field.type || "Field",
    en: field.en || "",
    ur: field.ur || "",
  }));

  return {
    id: `screen:${route}`,
    route,
    category: guide.category || "overview",
    title: {
      en: guide.title?.en || "",
      ur: guide.title?.ur || "",
    },
    section: {
      en: guide.section?.en || "",
      ur: guide.section?.ur || "",
    },
    summary: {
      en: guide.summary?.en || "",
      ur: guide.summary?.ur || "",
    },
    features: {
      en: guide.features?.en || [],
      ur: guide.features?.ur || [],
    },
    steps: {
      en: guide.steps?.en || [],
      ur: guide.steps?.ur || [],
    },
    impacts: {
      en: guide.impacts?.en || "",
      ur: guide.impacts?.ur || "",
    },
    shortcuts: (guide.shortcuts || []).map((sc) => ({
      keys: sc.keys || [],
      en: sc.en || "",
      ur: sc.ur || "",
    })),
    faqs,
    fields,
    example: guide.example
      ? {
          title: {
            en: guide.example.title?.en || "",
            ur: guide.example.title?.ur || "",
          },
          en: guide.example.en || "",
          ur: guide.example.ur || "",
        }
      : null,
    impactMatrix: guide.impactMatrix
      ? {
          stock: guide.impactMatrix.stock || { en: "", ur: "" },
          ledger: guide.impactMatrix.ledger || { en: "", ur: "" },
          treasury: guide.impactMatrix.treasury || { en: "", ur: "" },
          gl: guide.impactMatrix.gl || { en: "", ur: "" },
        }
      : null,
    commonMistake: guide.commonMistake
      ? {
          en: guide.commonMistake.en || "",
          ur: guide.commonMistake.ur || "",
        }
      : null,
    relatedRoutes: (guide.relatedRoutes || []).map((rel) => ({
      path: rel.path || "",
      en: rel.en || "",
      ur: rel.ur || "",
    })),
    workflowId: resolveWorkflowIdForIntent("", "", route),
  };
}

function sanitizeIntent(intent) {
  const primaryPath = intent.links?.[0]?.path || "";
  return {
    id: intent.id,
    sourceId: `intent:${intent.id}`,
    onlyWhenShort: Boolean(intent.onlyWhenShort),
    riskLevel: intent.riskLevel || "low",
    workflowId:
      intent.handoffWorkflowId ||
      resolveWorkflowIdForIntent(intent.id, "", primaryPath),
    phrases: intent.phrases || [],
    requiredGroups: intent.requiredGroups || [],
    keywords: intent.keywords || [],
    negativePhrases: intent.negativePhrases || [],
    title: {
      en: intent.title?.en || "",
      ur: intent.title?.ur || "",
    },
    en: intent.en || "",
    ur: intent.ur || "",
    links: (intent.links || []).map((link) => ({
      path: link.path || "",
      en: link.en || "",
      ur: link.ur || "",
    })),
  };
}

function sanitizeWorkflow(manifest) {
  // Explicitly strip CSS selectors (`target`, `targetSelector`) from exported workflow steps
  return {
    id: manifest.id,
    sourceId: `workflow:${manifest.id}`,
    title: {
      en: manifest.title?.en || "",
      ur: manifest.title?.ur || "",
    },
    primaryScreen: manifest.primaryScreen || "/dashboard",
    steps: (manifest.steps || []).map((step) => ({
      step: step.step,
      screen: step.screen || manifest.primaryScreen || "/dashboard",
      screenLabel: {
        en: step.screenLabel?.en || "",
        ur: step.screenLabel?.ur || "",
      },
      title: {
        en: step.title?.en || "",
        ur: step.title?.ur || "",
      },
      instruction: {
        en: step.instruction?.en || "",
        ur: step.instruction?.ur || "",
      },
      checkpoint: {
        en: step.checkpoint?.en || "",
        ur: step.checkpoint?.ur || "",
      },
    })),
  };
}

const screens = Object.values(SCREEN_GUIDES).map(sanitizeScreenGuide);
const intents = TRAINED_ERP_INTENTS.map(sanitizeIntent);
const workflows = Object.values(DEMO_MANIFESTS).map(sanitizeWorkflow);
const controls = UNIVERSAL_CONTROL_DICTIONARY.map((entry, idx) => ({
  id: `control:${idx}`,
  keywords: entry.keywords || [],
  title: {
    en: entry.title?.en || "",
    ur: entry.title?.ur || "",
  },
  en: entry.en || "",
  ur: entry.ur || "",
}));
const modals = Object.values(MODAL_CONTEXT_GUIDES).map((modal) => ({
  id: modal.id,
  badge: modal.badge || { en: "", ur: "" },
  title: modal.title || { en: "", ur: "" },
  summary: modal.summary || { en: "", ur: "" },
}));

const knowledgeIndex = {
  product_version: PRODUCT_VERSION,
  schema_version: "1.0.0",
  categories: MODULE_CATEGORIES,
  screens,
  intents,
  workflows,
  controls,
  modals,
};

fs.mkdirSync(outputDir, { recursive: true });
fs.writeFileSync(outputFile, JSON.stringify(knowledgeIndex, null, 2) + "\n", "utf8");

console.log(
  `[Cube Knowledge Exporter] Generated ${ path.relative(projectRoot, outputFile) } ` +
  `(v${PRODUCT_VERSION}: ${screens.length} screens, ${intents.length} intents, ${workflows.length} workflows, ${controls.length} controls).`
);

