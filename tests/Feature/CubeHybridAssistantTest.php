<?php

namespace Tests\Feature;

use App\Models\CubeProviderUsage;
use App\Models\User;
use App\Services\Cube\CubeKnowledgeRetriever;
use App\Services\LicenseService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\ConnectionException;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Hash;
use Illuminate\Support\Facades\Http;
use Laravel\Sanctum\Sanctum;
use Spatie\Permission\Models\Permission;
use Tests\TestCase;

class CubeHybridAssistantTest extends TestCase
{
    use RefreshDatabase;

    protected User $user;

    protected function setUp(): void
    {
        parent::setUp();

        $this->user = User::create([
            'name' => 'Cube Test Admin',
            'email' => 'cube-admin@karobar.test',
            'password' => Hash::make('password'),
            'status' => 'active',
            'remember_token_expires_at' => now()->addHours(8),
        ]);

        // Default valid license for authenticated tests unless overridden
        $this->mock(LicenseService::class, function ($mock) {
            $mock->shouldReceive('currentStatus')->andReturn(['valid' => true])->byDefault();
        });

        config([
            'cube.ai_enabled' => true,
            'cube.free_only' => true,
            'cube.primary_provider' => 'groq',
            'cube.backup_provider' => 'gemini',
            'cube.providers.groq.enabled' => true,
            'cube.providers.groq.api_key' => 'gsk_test_free_groq_key_123456',
            'cube.providers.groq.model' => 'llama-3.3-70b-versatile',
            'cube.providers.groq.daily_limit' => 50,
            'cube.providers.gemini.enabled' => true,
            'cube.providers.gemini.api_key' => 'AIza_test_free_gemini_key_123456',
            'cube.providers.gemini.model' => 'gemini-3.8-flash',
            'cube.providers.gemini.daily_limit' => 50,
        ]);
    }

    /**
     * 1. Authenticated access to `/api/cube/ask` (and input validation).
     */
    public function test_authenticated_access_to_cube_ask_and_rejects_forbidden_frontend_fields(): void
    {
        Http::fake([
            'api.groq.com/*' => Http::response([
                'choices' => [
                    [
                        'message' => [
                            'content' => json_encode([
                                'title_en' => 'Purchase Order vs Purchase Invoice',
                                'title_ur' => 'پرچیز آرڈر بمقابلہ پرچیز انوائس',
                                'answer_en' => '1. A Purchase Order is a demand list sent to a supplier.\n2. A Purchase Invoice adds physical stock and updates payable ledger.',
                                'answer_ur' => '1. پرچیز آرڈر سپلائر کے لیے مال کی فہرست ہے۔\n2. پرچیز انوائس سے اسٹاک اور کھاتہ اپڈیٹ ہوتا ہے۔',
                                'source_ids' => ['intent:po_vs_purchase_invoice'],
                            ], JSON_UNESCAPED_UNICODE),
                        ],
                    ],
                ],
            ], 200),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'Can you compare Purchase Order and Purchase Invoice in detail?',
            'language' => 'en',
            'current_route' => '/purchase-orders',
        ]);

        $response->assertOk()
            ->assertJsonPath('kind', 'qa')
            ->assertJsonPath('provider', 'groq')
            ->assertJsonPath('provider_status', 'groq_primary');

        // Reject forbidden fields from frontend (provider, css_selector, navigation_url, action_id)
        $forbiddenResponse = $this->postJson('/api/cube/ask', [
            'question' => 'How to make sale?',
            'provider' => 'groq',
            'css_selector' => '#hack',
            'navigation_url' => 'https://evil.example.com',
        ]);

        $forbiddenResponse->assertStatus(422)
            ->assertJsonValidationErrors(['provider', 'css_selector', 'navigation_url']);
    }

    /**
     * 2. Unauthenticated rejection.
     */
    public function test_unauthenticated_requests_are_rejected(): void
    {
        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How to create a purchase order?',
        ]);

        $response->assertStatus(401);
    }

    /**
     * 3. License middleware enforcement.
     */
    public function test_license_middleware_enforces_valid_license(): void
    {
        $this->mock(LicenseService::class, function ($mock) {
            $mock->shouldReceive('currentStatus')->andReturn([
                'valid' => false,
                'reason' => 'License expired or missing',
            ]);
        });

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How to add new stock?',
        ]);

        $response->assertStatus(402)
            ->assertJsonPath('code', 'license_required');
    }

    /**
     * 4. Token expiry enforcement.
     */
    public function test_token_expiry_middleware_rejects_expired_session(): void
    {
        $expiredUser = User::create([
            'name' => 'Expired Cube User',
            'email' => 'expired@karobar.test',
            'password' => Hash::make('password'),
            'status' => 'active',
            'remember_token_expires_at' => now()->subMinutes(10),
        ]);

        Sanctum::actingAs($expiredUser);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How to add new stock?',
        ]);

        $response->assertStatus(401)
            ->assertJsonPath('expired', true);
    }

    /**
     * 5, 6, 7, 22, 23. Strong local intent answers without external AI calls, FAQ matching,
     * Screen-guide matching, Interactive walkthrough preservation, and Evidence label accuracy.
     */
    public function test_frontend_local_intelligence_layers_faq_screen_guide_walkthrough_and_evidence_labels(): void
    {
        $nodeBinary = is_executable('/Users/malikasimshahzad/.nvm/versions/node/v24.20.0/bin/node')
            ? '/Users/malikasimshahzad/.nvm/versions/node/v24.20.0/bin/node'
            : 'node';

        $script = <<<'JS'
import {
  routeUserQuery,
  SCREEN_GUIDES,
  DEMO_MANIFESTS,
  MATCH_CATEGORIES,
  EVIDENCE_TYPES,
  buildEvidenceBadge,
} from "./resources/js/data/guideBotKnowledge.js";

// Test 5: Strong local intent match (should NOT call AI)
const strongIntent = routeUserQuery("how to create purchase order", "cube", SCREEN_GUIDES["/dashboard"]);

// Test 6: FAQ match (should NOT call AI)
const faqMatch = routeUserQuery(
  "Why do dashboard numbers change when I pick different dates?",
  "cube",
  SCREEN_GUIDES["/dashboard"]
);

// Test 7: Screen-guide match (should NOT call AI)
const screenMatch = routeUserQuery(
  "explain this screen",
  "cube",
  SCREEN_GUIDES["/purchase-orders"]
);

// Test 18 (local): Roman Urdu strong match
const romanUrdu = routeUserQuery(
  "khuli goliyan kaise bechein",
  "cube",
  SCREEN_GUIDES["/sale-invoices/pos"]
);

// Test 22: Interactive walkthrough preservation with deterministic selectors
const demoReq = routeUserQuery(
  "show me how to create a purchase order",
  "cube",
  SCREEN_GUIDES["/purchase-orders"]
);
const manifest = DEMO_MANIFESTS[demoReq.workflowId];

// Test 23: Evidence label accuracy (never labels static docs as live UI observation)
const docBadge = strongIntent.evidenceBadge;
const screenBadge = screenMatch.evidenceBadge;
const domBadge = buildEvidenceBadge(EVIDENCE_TYPES.LIVE_DOM_INSPECTION, SCREEN_GUIDES["/dashboard"]);
const aiBadge = buildEvidenceBadge(EVIDENCE_TYPES.AI_GROUNDED_DOCS, SCREEN_GUIDES["/dashboard"], "Groq AI");

console.log(JSON.stringify({
  strongIntent: {
    matchedIntentId: strongIntent.matchedIntentId,
    intentScore: strongIntent.intentScore,
    secondBestScore: strongIntent.secondBestScore,
    matchMargin: strongIntent.matchMargin,
    matchCategory: strongIntent.matchCategory,
    evidenceType: strongIntent.evidenceType,
    confidence: strongIntent.confidence,
    shouldCallAi: strongIntent.shouldCallAi,
  },
  faqMatch: {
    matchedIntentId: faqMatch.matchedIntentId,
    matchCategory: faqMatch.matchCategory,
    shouldCallAi: faqMatch.shouldCallAi,
  },
  screenMatch: {
    matchedIntentId: screenMatch.matchedIntentId,
    matchCategory: screenMatch.matchCategory,
    evidenceType: screenMatch.evidenceType,
    shouldCallAi: screenMatch.shouldCallAi,
  },
  romanUrdu: {
    matchedIntentId: romanUrdu.matchedIntentId,
    matchCategory: romanUrdu.matchCategory,
    shouldCallAi: romanUrdu.shouldCallAi,
  },
  demoReq: {
    startDemoNow: demoReq.startDemoNow,
    workflowId: demoReq.workflowId,
    shouldCallAi: demoReq.shouldCallAi,
    hasManifestSteps: Boolean(manifest && manifest.steps && manifest.steps.length > 0 && manifest.steps[0].target),
  },
  badges: {
    docEn: docBadge.en,
    screenEn: screenBadge.en,
    domEn: domBadge.en,
    aiEn: aiBadge.en,
  },
}));
JS;

        $cmd = escapeshellarg($nodeBinary).' --input-type=module -e '.escapeshellarg($script);
        $output = shell_exec($cmd);
        $this->assertNotNull($output, 'Node evaluation should return JSON output');

        $result = json_decode(trim($output), true);
        $this->assertIsArray($result);

        // 5. Strong local intent match without external AI calls
        $this->assertSame('create_purchase_order', $result['strongIntent']['matchedIntentId']);
        $this->assertSame('strong_intent_match', $result['strongIntent']['matchCategory']);
        $this->assertGreaterThan(65, $result['strongIntent']['intentScore']);
        $this->assertGreaterThan(24, $result['strongIntent']['matchMargin']);
        $this->assertFalse($result['strongIntent']['shouldCallAi']);

        // 6. FAQ matching
        $this->assertSame('faq:/dashboard#0', $result['faqMatch']['matchedIntentId']);
        $this->assertSame('faq_match', $result['faqMatch']['matchCategory']);
        $this->assertFalse($result['faqMatch']['shouldCallAi']);

        // 7. Screen-guide matching
        $this->assertSame('current_screen_overview', $result['screenMatch']['matchedIntentId']);
        $this->assertSame('screen_guide_match', $result['screenMatch']['matchCategory']);
        $this->assertSame('current_screen_context', $result['screenMatch']['evidenceType']);
        $this->assertFalse($result['screenMatch']['shouldCallAi']);

        // 18. Roman Urdu query handling locally
        $this->assertSame('sell_loose_tablets_pack_vs_unit', $result['romanUrdu']['matchedIntentId']);
        $this->assertFalse($result['romanUrdu']['shouldCallAi']);

        // 22. Interactive walkthrough preservation
        $this->assertTrue($result['demoReq']['startDemoNow']);
        $this->assertSame('create_purchase_order', $result['demoReq']['workflowId']);
        $this->assertFalse($result['demoReq']['shouldCallAi']);
        $this->assertTrue($result['demoReq']['hasManifestSteps']);

        // 23. Evidence label accuracy: static docs and screen context are never labeled as "Observed on current screen"
        $this->assertStringContainsString('Documented in Karobar Knowledge Base', $result['badges']['docEn']);
        $this->assertStringNotContainsString('Observed on current screen', $result['badges']['docEn']);
        $this->assertStringContainsString('Current Screen Context', $result['badges']['screenEn']);
        $this->assertStringNotContainsString('Observed on current screen', $result['badges']['screenEn']);
        $this->assertStringContainsString('Live DOM Inspection', $result['badges']['domEn']);
        $this->assertStringContainsString('AI-Assisted Explanation', $result['badges']['aiEn']);
    }

    /**
     * 8, 16, 17. Groq primary success with bilingual English and Urdu structured output.
     */
    public function test_groq_primary_success_returns_structured_english_and_urdu_output(): void
    {
        Http::fake([
            'api.groq.com/*' => Http::response([
                'choices' => [
                    [
                        'message' => [
                            'content' => json_encode([
                                'title_en' => 'How to Record Supplier Expiry Intimation',
                                'title_ur' => 'سپلائر ایکسپائری انٹیمیشن درج کرنے کا طریقہ',
                                'answer_en' => "1. Open **Batch Expiry Report** (`/reports/batch-expiry`).\n2. Filter by your supplier's return policy window and mark the batch as **Intimated**.",
                                'answer_ur' => "1. **Batch Expiry Report** (`/reports/batch-expiry`) کھولیں۔\n2. سپلائر کی پالیسی کے مطابق بیچ کو **Intimated** مارک کریں۔",
                                'source_ids' => ['screen:/suppliers'],
                            ], JSON_UNESCAPED_UNICODE),
                        ],
                    ],
                ],
            ], 200),
            'generativelanguage.googleapis.com/*' => Http::response([], 500),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'Please explain in detail how supplier expiry intimation policy works',
            'language' => 'ur',
            'current_route' => '/suppliers',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'groq')
            ->assertJsonPath('provider_status', 'groq_primary')
            ->assertJsonPath('fallback_used', false)
            ->assertJsonPath('evidence_classification', 'ai_grounded_docs')
            ->assertJsonPath('title_en', 'How to Record Supplier Expiry Intimation')
            ->assertJsonPath('title_ur', 'سپلائر ایکسپائری انٹیمیشن درج کرنے کا طریقہ');

        $this->assertStringContainsString('Batch Expiry Report', $response->json('answer_en'));
        $this->assertStringContainsString('سپلائر', $response->json('answer_ur'));

        // Verify Gemini was not called since Groq succeeded
        Http::assertSentCount(1);

        $usage = CubeProviderUsage::where('provider', 'groq')->first();
        $this->assertNotNull($usage);
        $this->assertSame(1, $usage->request_count);
        $this->assertSame(1, $usage->success_count);
        $this->assertSame('ready', $usage->status);
    }

    /**
     * 9. Groq HTTP 429 fallback to Gemini (and respects Retry-After cooldown).
     */
    public function test_groq_http_429_respects_retry_after_and_falls_back_to_gemini(): void
    {
        Http::fake([
            'api.groq.com/*' => Http::response([
                'error' => [
                    'message' => 'Rate limit reached for model',
                    'type' => 'tokens',
                    'code' => 'rate_limit_exceeded',
                ],
            ], 429, ['Retry-After' => '90']),
            'generativelanguage.googleapis.com/*' => Http::response([
                'candidates' => [
                    [
                        'content' => [
                            'parts' => [
                                [
                                    'text' => json_encode([
                                        'title_en' => 'Opening & Closing Cash Drawer Shift',
                                        'title_ur' => 'کیش ڈراور شفٹ کھولنے اور بند کرنے کا طریقہ',
                                        'answer_en' => '1. Open Cash Drawer (`/cash-drawer`) and enter Opening Float.\n2. At shift end, enter physical cash count and print the Z-Report.',
                                        'answer_ur' => '1. کیش ڈراور (`/cash-drawer`) کھول کر ابتدائی رقم لکھیں۔\n2. شفٹ کے اختتام پر نقد رقم گن کر Z-Report پرنٹ کریں۔',
                                        'source_ids' => ['intent:cash_drawer_shift_z_report'],
                                    ], JSON_UNESCAPED_UNICODE),
                                ],
                            ],
                        ],
                    ],
                ],
            ], 200),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'Explain cash drawer shift opening and z-report reconciliation in detail',
            'language' => 'en',
            'current_route' => '/cash-drawer',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'gemini')
            ->assertJsonPath('provider_status', 'gemini_backup')
            ->assertJsonPath('fallback_used', true)
            ->assertJsonPath('evidence_classification', 'ai_grounded_docs');

        $groqUsage = CubeProviderUsage::where('provider', 'groq')->first();
        $this->assertNotNull($groqUsage);
        $this->assertSame('rate_limited', $groqUsage->status);
        $this->assertNotNull($groqUsage->cooldown_until);
        $this->assertTrue($groqUsage->cooldown_until->isFuture());
    }

    /**
     * 10. Groq timeout / connection failure fallback to Gemini.
     */
    public function test_groq_timeout_falls_back_to_gemini(): void
    {
        Http::fake([
            'api.groq.com/*' => function () {
                throw new ConnectionException('cURL error 28: Operation timed out');
            },
            'generativelanguage.googleapis.com/*' => Http::response([
                'candidates' => [
                    [
                        'content' => [
                            'parts' => [
                                [
                                    'text' => json_encode([
                                        'title_en' => 'Barcode POS Billing Guide',
                                        'title_ur' => 'بارکوڈ POS بلنگ گائیڈ',
                                        'answer_en' => '1. Scan barcode on POS (`/sale-invoices/pos`).\n2. Press F8 to hold or complete checkout.',
                                        'answer_ur' => '1. بارکوڈ اسکین کریں۔\n2. بل ہولڈ کرنے کے لیے F8 دبائیں۔',
                                        'source_ids' => ['screen:/sale-invoices/pos'],
                                    ], JSON_UNESCAPED_UNICODE),
                                ],
                            ],
                        ],
                    ],
                ],
            ], 200),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How does barcode POS billing and F8 hold bill work in detail?',
            'language' => 'en',
            'current_route' => '/sale-invoices/pos',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'gemini')
            ->assertJsonPath('provider_status', 'gemini_backup')
            ->assertJsonPath('fallback_used', true);
    }

    /**
     * 11. Groq daily limit exhaustion fallback to Gemini without calling Groq HTTP endpoint.
     */
    public function test_groq_daily_limit_exhaustion_falls_back_to_gemini(): void
    {
        config(['cube.providers.groq.daily_limit' => 2]);

        CubeProviderUsage::create([
            'provider' => 'groq',
            'usage_date' => now()->toDateString(),
            'request_count' => 2,
            'success_count' => 2,
            'failure_count' => 0,
            'status' => 'ready',
        ]);

        Http::fake([
            'api.groq.com/*' => Http::response([], 200),
            'generativelanguage.googleapis.com/*' => Http::response([
                'candidates' => [
                    [
                        'content' => [
                            'parts' => [
                                [
                                    'text' => json_encode([
                                        'title_en' => 'Customer Udhar & Ledger Recovery',
                                        'title_ur' => 'کسٹمر ادھار کھاتہ اور وصولی',
                                        'answer_en' => 'Open Customer Ledger (`/customer-ledger`) to record cash/bank recovery.',
                                        'answer_ur' => 'کسٹمر لیجر (`/customer-ledger`) کھول کر وصولی درج کریں۔',
                                        'source_ids' => ['intent:customer_udhar_ledger_payment'],
                                    ], JSON_UNESCAPED_UNICODE),
                                ],
                            ],
                        ],
                    ],
                ],
            ], 200),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'Explain customer udhar ledger recovery workflow in detail',
            'language' => 'en',
            'current_route' => '/customer-ledger',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'gemini')
            ->assertJsonPath('provider_status', 'gemini_backup')
            ->assertJsonPath('provider_trace.groq.status', 'quota_exhausted');

        // Groq was skipped before making any HTTP call
        Http::assertSentCount(1);
    }

    /**
     * 12 & 13. Gemini HTTP 429 and both providers unavailable fallback to local knowledge.
     */
    public function test_both_providers_unavailable_or_rate_limited_falls_back_to_local_knowledge(): void
    {
        Http::fake([
            'api.groq.com/*' => Http::response([
                'error' => ['message' => 'Invalid API key provided', 'code' => 'invalid_api_key'],
            ], 401),
            'generativelanguage.googleapis.com/*' => Http::response([
                'error' => ['status' => 'RESOURCE_EXHAUSTED', 'message' => 'Quota exceeded'],
            ], 429, ['Retry-After' => '120']),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How to create a purchase order for low stock medicines?',
            'language' => 'en',
            'current_route' => '/purchase-orders',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'local')
            ->assertJsonPath('provider_status', 'local_fallback')
            ->assertJsonPath('fallback_used', true)
            ->assertJsonPath('evidence_classification', 'documented_info');

        $this->assertNotEmpty($response->json('answer_en'));
        $this->assertNotEmpty($response->json('answer_ur'));

        // Verify Groq was marked disabled_until_reconfigured on 401 auth failure
        $groqUsage = CubeProviderUsage::where('provider', 'groq')->first();
        $this->assertSame('disabled_until_reconfigured', $groqUsage->status);

        // Verify Gemini was marked rate_limited on 429
        $geminiUsage = CubeProviderUsage::where('provider', 'gemini')->first();
        $this->assertSame('rate_limited', $geminiUsage->status);
    }

    /**
     * 14. Missing or zero daily limit disables provider.
     */
    public function test_missing_or_zero_daily_limit_disables_provider(): void
    {
        config([
            'cube.providers.groq.daily_limit' => 0,
            'cube.providers.gemini.daily_limit' => 0,
        ]);

        Http::fake();

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How to add stock via purchase invoice?',
            'language' => 'en',
            'current_route' => '/purchase-invoices/create',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'local')
            ->assertJsonPath('provider_status', 'local_fallback')
            ->assertJsonPath('provider_trace.groq.reason', 'daily_limit_missing_or_zero')
            ->assertJsonPath('provider_trace.gemini.reason', 'daily_limit_missing_or_zero');

        Http::assertNothingSent();
    }

    /**
     * 15. Invalid or unsafe AI output rejected and replaced by fallback.
     */
    public function test_invalid_or_unsafe_ai_output_is_rejected_and_replaced_by_fallback(): void
    {
        Http::fake([
            'api.groq.com/*' => Http::response([
                'choices' => [
                    [
                        'message' => [
                            'content' => json_encode([
                                'title_en' => 'Unsafe Script Test',
                                'title_ur' => 'غیر محفوظ ٹیسٹ',
                                'answer_en' => 'Click here <script>alert("xss")</script> or visit https://malicious.example.com',
                                'answer_ur' => 'یہاں کلک کریں <script>alert(1)</script>',
                                'source_ids' => ['intent:create_purchase_order'],
                            ]),
                        ],
                    ],
                ],
            ], 200),
            'generativelanguage.googleapis.com/*' => Http::response([
                'candidates' => [
                    [
                        'content' => [
                            'parts' => [
                                ['text' => 'NOT_VALID_JSON_AT_ALL'],
                            ],
                        ],
                    ],
                ],
            ], 200),
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How to create a purchase order step by step?',
            'language' => 'en',
            'current_route' => '/purchase-orders',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'local')
            ->assertJsonPath('provider_status', 'local_fallback')
            ->assertJsonPath('provider_trace.groq.status', 'invalid_output')
            ->assertJsonPath('provider_trace.gemini.status', 'invalid_output');

        $this->assertStringNotContainsString('<script>', $response->json('answer_en'));
        $this->assertStringNotContainsString('malicious.example.com', $response->json('answer_en'));
    }

    /**
     * 18 & 19. Roman Urdu query handling and follow-up question handling with recent context.
     */
    public function test_roman_urdu_and_followup_question_with_recent_conversation_context(): void
    {
        $capturedPrompt = null;

        Http::fake([
            'api.groq.com/*' => function ($request) use (&$capturedPrompt) {
                $capturedPrompt = $request->data();

                return Http::response([
                    'choices' => [
                        [
                            'message' => [
                                'content' => json_encode([
                                    'title_en' => 'Selling Loose Tablets (Pack vs Unit)',
                                    'title_ur' => 'کھلی گولیاں بیچنے کا طریقہ (ڈبہ بمقابلہ یونٹ)',
                                    'answer_en' => 'When Pack Size is set to 30, selecting Unit mode on POS deducts only the loose tablets sold.',
                                    'answer_ur' => 'جب پیک سائز 30 ہو تو POS پر Unit موڈ منتخب کرنے سے صرف فروخت شدہ کھلی گولیاں اسٹاک سے کم ہوتی ہیں۔',
                                    'source_ids' => ['intent:sell_loose_tablets_pack_vs_unit'],
                                ], JSON_UNESCAPED_UNICODE),
                            ],
                        ],
                    ],
                ], 200);
            },
        ]);

        Sanctum::actingAs($this->user);

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'Aur agar customer 5 khuli goliyan wapas kare toh kya hoga?',
            'language' => 'ur',
            'current_route' => '/sale-invoices/pos',
            'history' => [
                ['role' => 'user', 'content' => 'khuli goliyan kaise bechein? Call me at 0300-1234567'],
                ['role' => 'assistant', 'content' => 'Set Pack Size on the product and choose Unit on POS.'],
            ],
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'groq')
            ->assertJsonPath('workflow_id', 'pos_counter_billing');

        $this->assertNotNull($capturedPrompt);
        $userMessageContent = data_get($capturedPrompt, 'messages.1.content', '');
        // Verify recent history was included AND phone number PII was redacted before sending to Groq
        $this->assertStringContainsString('khuli goliyan kaise bechein', $userMessageContent);
        $this->assertStringContainsString('[REDACTED_PHONE]', $userMessageContent);
        $this->assertStringNotContainsString('0300-1234567', $userMessageContent);
    }

    /**
     * 20. Request for live ERP data handled without database disclosure or AI calls.
     */
    public function test_live_erp_data_request_is_handled_without_database_disclosure_or_ai_calls(): void
    {
        Http::fake();

        Sanctum::actingAs($this->user);

        $queriesExecuted = [];
        DB::listen(function ($query) use (&$queriesExecuted) {
            $queriesExecuted[] = strtolower($query->sql);
        });

        $response = $this->postJson('/api/cube/ask', [
            'question' => 'How much stock of Panadol do I have right now and what is the current balance of customer Ali?',
            'language' => 'en',
            'current_route' => '/dashboard',
        ]);

        $response->assertOk()
            ->assertJsonPath('provider', 'local')
            ->assertJsonPath('provider_status', 'local_privacy_boundary')
            ->assertJsonPath('fallback_used', false)
            ->assertJsonPath('evidence_classification', 'documented_info');

        $this->assertStringContainsString('/reports/current-stock', $response->json('answer_en'));
        $this->assertNotEmpty($response->json('links'));

        // External AI providers must NOT be called for live ERP figure queries
        Http::assertNothingSent();

        // Ensure no ERP business tables (products, batches, customers, sale_invoices, accounts, ledgers) were queried
        foreach ($queriesExecuted as $sql) {
            $this->assertDoesNotMatchRegularExpression(
                '/\b(products|batches|customers|suppliers|sale_invoices|purchase_invoices|accounts|journal_entries)\b/',
                $sql
            );
        }
    }

    /**
     * 21. Knowledge exporter output validation.
     */
    public function test_knowledge_exporter_output_is_valid_and_free_of_css_selectors(): void
    {
        $retriever = app(CubeKnowledgeRetriever::class);
        $kb = $retriever->loadKnowledge();

        $this->assertNotEmpty($kb['product_version'] ?? null);
        $this->assertNotEmpty($kb['screens'] ?? []);
        $this->assertNotEmpty($kb['intents'] ?? []);
        $this->assertNotEmpty($kb['workflows'] ?? []);
        $this->assertGreaterThanOrEqual(75, count($kb['screens']));
        $this->assertGreaterThanOrEqual(50, count($kb['intents']));

        // Ensure workflow metadata in cube-knowledge.json does not contain raw CSS target selectors
        foreach ($kb['workflows'] as $wf) {
            foreach (($wf['steps'] ?? []) as $step) {
                $this->assertArrayNotHasKey('target', $step);
                $this->assertArrayNotHasKey('targetSelector', $step);
            }
        }
    }

    /**
     * 24. Cube AI Settings Panel: User can save Groq & Gemini API keys via `/api/settings`,
     * test connectivity via `/api/settings/test-cube-ai`, and `/api/cube/ask` uses the saved DB keys.
     */
    public function test_cube_ai_settings_panel_saves_api_keys_and_powers_cube_ask_without_env(): void
    {
        Permission::firstOrCreate(['name' => 'settings.view', 'guard_name' => 'sanctum']);
        Permission::firstOrCreate(['name' => 'settings.update', 'guard_name' => 'sanctum']);
        $this->user->givePermissionTo(['settings.view', 'settings.update']);

        // Clear any .env / config keys first to prove DB settings alone power Cube AI
        config([
            'cube.providers.groq.api_key' => '',
            'cube.providers.groq.daily_limit' => 0,
            'cube.providers.gemini.api_key' => '',
            'cube.providers.gemini.daily_limit' => 0,
        ]);

        Http::fake([
            'api.groq.com/*' => Http::response([
                'choices' => [
                    [
                        'message' => [
                            'content' => json_encode([
                                'title_en' => 'Connected via DB Settings',
                                'title_ur' => 'ڈیٹا بیس سیٹنگز سے منسلک',
                                'answer_en' => 'Groq key from Settings UI works!',
                                'answer_ur' => 'سیٹنگز پینل کی اے پی آئی کی کامیابی سے کام کر رہی ہے!',
                                'source_ids' => ['intent:create_purchase_order'],
                            ], JSON_UNESCAPED_UNICODE),
                        ],
                    ],
                ],
            ], 200),
        ]);

        Sanctum::actingAs($this->user);

        // 1. Test key via /api/settings/test-cube-ai
        $testResp = $this->postJson('/api/settings/test-cube-ai', [
            'provider' => 'groq',
            'api_key' => 'gsk_ui_saved_key_999999',
            'model' => 'llama-3.1-8b-instant',
        ]);

        $testResp->assertOk()
            ->assertJsonPath('status', 'connected')
            ->assertJsonPath('provider', 'groq');

        // 2. Save settings via POST /api/settings (just like SMTP)
        $saveResp = $this->postJson('/api/settings', [
            'printer_type' => 'thermal',
            'cube_ai_enabled' => true,
            'cube_groq_enabled' => true,
            'cube_groq_api_key' => 'gsk_ui_saved_key_999999',
            'cube_groq_model' => 'llama-3.1-8b-instant',
            'cube_groq_daily_limit' => 300,
            'cube_gemini_enabled' => true,
            'cube_gemini_api_key' => 'AIza_ui_saved_key_888888',
            'cube_gemini_model' => 'gemini-3.8-flash',
            'cube_gemini_daily_limit' => 250,
        ]);

        $saveResp->assertOk()
            ->assertJsonPath('cube_groq_api_key', 'gsk_ui_saved_key_999999')
            ->assertJsonPath('cube_groq_model', 'llama-3.1-8b-instant')
            ->assertJsonPath('cube_groq_daily_limit', 300)
            ->assertJsonPath('cube_gemini_api_key', 'AIza_ui_saved_key_888888');

        // Reset runtime config to empty so we verify CubeAssistantService loads from DB Setting
        config([
            'cube.providers.groq.api_key' => '',
            'cube.providers.groq.daily_limit' => 0,
        ]);

        // 3. Call /api/cube/ask and verify it uses the DB-stored Groq API key
        $askResp = $this->postJson('/api/cube/ask', [
            'question' => 'How to create a purchase order step by step?',
            'language' => 'en',
            'current_route' => '/purchase-orders',
        ]);

        $askResp->assertOk()
            ->assertJsonPath('provider', 'groq')
            ->assertJsonPath('provider_status', 'groq_primary')
            ->assertJsonPath('title_en', 'Connected via DB Settings');
    }
}
