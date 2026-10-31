<?php

namespace App\Services\Cube;

use App\Models\Brand;
use App\Models\Category;
use App\Models\Formulation;
use App\Models\Product;
use App\Models\ProductSupplier;
use App\Models\Setting;
use App\Models\StorageLocation;
use App\Models\Supplier;
use App\Models\User;
use Illuminate\Support\Facades\Schema;
use Throwable;

class CubeTaskExecutorService
{
    /**
     * Built-in pharmaceutical generic/salt dictionary for fast, accurate background
     * formulation enrichment even before or alongside external AI calls.
     *
     * @var array<string, string>
     */
    protected const PHARMACY_SALT_DICTIONARY = [
        'panadol cf' => 'Paracetamol + Chlorpheniramine + Pseudoephedrine',
        'panadol extra' => 'Paracetamol + Caffeine',
        'panadol extend' => 'Paracetamol',
        'panadol' => 'Paracetamol',
        'calpol' => 'Paracetamol',
        'febrol' => 'Paracetamol',
        'disprol' => 'Paracetamol',
        'provas' => 'Paracetamol',
        'tylenol' => 'Paracetamol',
        'paracetamol' => 'Paracetamol',
        'acetaminophen' => 'Paracetamol',
        'nuberol forte' => 'Paracetamol + Orphenadrine Citrate',
        'nuberol' => 'Paracetamol + Orphenadrine Citrate',
        'augmentin' => 'Amoxicillin + Clavulanic Acid',
        'calamox' => 'Amoxicillin + Clavulanic Acid',
        'amoxil' => 'Amoxicillin',
        'amoxicillin' => 'Amoxicillin',
        'ospamox' => 'Amoxicillin',
        'brufen' => 'Ibuprofen',
        'advil' => 'Ibuprofen',
        'ibuprofen' => 'Ibuprofen',
        'arinac' => 'Ibuprofen + Pseudoephedrine',
        'ponstan' => 'Mefenamic Acid',
        'mefenamic' => 'Mefenamic Acid',
        'disprin' => 'Aspirin',
        'loprin' => 'Aspirin',
        'ascard' => 'Aspirin',
        'aspirin' => 'Aspirin',
        'flagyl' => 'Metronidazole',
        'entamizole' => 'Diloxanide Furoate + Metronidazole',
        'metronidazole' => 'Metronidazole',
        'risek' => 'Omeprazole',
        'omega' => 'Omeprazole',
        'losec' => 'Omeprazole',
        'omeprazole' => 'Omeprazole',
        'nexum' => 'Esomeprazole',
        'esso' => 'Esomeprazole',
        'esomeprazole' => 'Esomeprazole',
        'pantocid' => 'Pantoprazole',
        'pantoprazole' => 'Pantoprazole',
        'lansoprazole' => 'Lansoprazole',
        'famotidine' => 'Famotidine',
        'gaviscon' => 'Sodium Alginate + Potassium Bicarbonate',
        'mucaine' => 'Oxethazaine + Aluminum Hydroxide + Magnesium Hydroxide',
        'gravinate' => 'Dimenhydrinate',
        'motilium' => 'Domperidone',
        'peridone' => 'Domperidone',
        'domperidone' => 'Domperidone',
        'maxolon' => 'Metoclopramide',
        'metoclopramide' => 'Metoclopramide',
        'onavert' => 'Ondansetron',
        'ondansetron' => 'Ondansetron',
        'rigix' => 'Cetirizine',
        'zyrtec' => 'Cetirizine',
        'cetirizine' => 'Cetirizine',
        'softin' => 'Loratadine',
        'claritin' => 'Loratadine',
        'loratadine' => 'Loratadine',
        'telfast' => 'Fexofenadine',
        'fexet' => 'Fexofenadine',
        'fexofenadine' => 'Fexofenadine',
        'avil' => 'Pheniramine Maleate',
        'piriton' => 'Chlorpheniramine Maleate',
        'ventolin' => 'Salbutamol',
        'salbutamol' => 'Salbutamol',
        'myteka' => 'Montelukast',
        'singulair' => 'Montelukast',
        'montelukast' => 'Montelukast',
        'azomax' => 'Azithromycin',
        'zithromax' => 'Azithromycin',
        'azithromycin' => 'Azithromycin',
        'novidat' => 'Ciprofloxacin',
        'ciproxin' => 'Ciprofloxacin',
        'ciprofloxacin' => 'Ciprofloxacin',
        'leflox' => 'Levofloxacin',
        'cravit' => 'Levofloxacin',
        'levofloxacin' => 'Levofloxacin',
        'avelox' => 'Moxifloxacin',
        'moxifloxacin' => 'Moxifloxacin',
        'cefspan' => 'Cefixime',
        'cefixime' => 'Cefixime',
        'caricef' => 'Cefixime',
        'velosef' => 'Cephradine',
        'cephradine' => 'Cephradine',
        'keflex' => 'Cephalexin',
        'cephalexin' => 'Cephalexin',
        'rocephin' => 'Ceftriaxone',
        'ceftriaxone' => 'Ceftriaxone',
        'klaricid' => 'Clarithromycin',
        'clarithromycin' => 'Clarithromycin',
        'vibramycin' => 'Doxycycline',
        'doxycycline' => 'Doxycycline',
        'glucophage' => 'Metformin',
        'metformin' => 'Metformin',
        'amaryl' => 'Glimepiride',
        'getryl' => 'Glimepiride',
        'glimepiride' => 'Glimepiride',
        'diamicron' => 'Gliclazide',
        'gliclazide' => 'Gliclazide',
        'januvia' => 'Sitagliptin',
        'sitagliptin' => 'Sitagliptin',
        'norvasc' => 'Amlodipine',
        'sofvasc' => 'Amlodipine',
        'amlodipine' => 'Amlodipine',
        'concor' => 'Bisoprolol',
        'bisoprolol' => 'Bisoprolol',
        'tenormin' => 'Atenolol',
        'atenolol' => 'Atenolol',
        'inderal' => 'Propranolol',
        'propranolol' => 'Propranolol',
        'cozaar' => 'Losartan Potassium',
        'eziday' => 'Losartan Potassium',
        'losartan' => 'Losartan Potassium',
        'exforge' => 'Amlodipine + Valsartan',
        'valsartan' => 'Valsartan',
        'capoten' => 'Captopril',
        'ramipril' => 'Ramipril',
        'lipiget' => 'Atorvastatin',
        'lipitor' => 'Atorvastatin',
        'atorvastatin' => 'Atorvastatin',
        'rovista' => 'Rosuvastatin',
        'crestor' => 'Rosuvastatin',
        'rosuvastatin' => 'Rosuvastatin',
        'voltral' => 'Diclofenac Sodium',
        'dicloran' => 'Diclofenac Sodium',
        'diclofenac' => 'Diclofenac Sodium',
        'caflam' => 'Diclofenac Potassium',
        'synflex' => 'Naproxen Sodium',
        'naproxen' => 'Naproxen Sodium',
        'brexin' => 'Piroxicam',
        'piroxicam' => 'Piroxicam',
        'celebrex' => 'Celecoxib',
        'celecoxib' => 'Celecoxib',
        'surbex z' => 'Vitamin B-Complex + Vitamin C + Zinc',
        'surbex' => 'Vitamin B-Complex',
        'neurobion' => 'Vitamin B1 + Vitamin B6 + Vitamin B12',
        'methycobal' => 'Mecobalamin (Vitamin B12)',
        'cac 1000' => 'Calcium + Vitamin C + Vitamin D3',
        'cac-1000' => 'Calcium + Vitamin C + Vitamin D3',
        'sunny d' => 'Cholecalciferol (Vitamin D3)',
        'indrop d' => 'Cholecalciferol (Vitamin D3)',
        'fefol' => 'Ferrous Sulfate + Folic Acid',
        'folic acid' => 'Folic Acid',
        'polyfax' => 'Polymyxin B + Bacitracin Zinc',
        'betnovate' => 'Betamethasone Valerate',
        'fucidin' => 'Fusidic Acid',
        'kenacomb' => 'Triamcinolone + Neomycin + Nystatin + Gramicidin',
        'hydryllin' => 'Aminophylline + Diphenhydramine + Ammonium Chloride',
        'acefyl' => 'Acefylline Piperazine + Diphenhydramine',
        'pulmonol' => 'Chlorpheniramine + Dextromethorphan + Pseudoephedrine',
        'buscopan' => 'Hyoscine Butylbromide',
        'no-spa' => 'Drotaverine',
        'nospa' => 'Drotaverine',
        'duphalac' => 'Lactulose',
        'lilac' => 'Lactulose',
        'lactulose' => 'Lactulose',
        'imodium' => 'Loperamide',
        'loperamide' => 'Loperamide',
        'ors' => 'Oral Rehydration Salts',
        'pedialyte' => 'Oral Rehydration Salts',
        'lasix' => 'Furosemide',
        'furosemide' => 'Furosemide',
        'aldactone' => 'Spironolactone',
        'thyroxine' => 'Levothyroxine Sodium',
        'deltacoortril' => 'Prednisolone',
        'prednisolone' => 'Prednisolone',
        'decadron' => 'Dexamethasone',
        'dexamethasone' => 'Dexamethasone',
    ];

    public function __construct(
        protected GroqProvider $groqProvider,
        protected GeminiProvider $geminiProvider,
        protected CubeQuotaGuard $quotaGuard,
    ) {}

    /**
     * Inspects a natural-language chat message to determine if the user is asking Cube
     * to perform an operational task (Add Product, Edit Product, Bulk Add/Auto-Fill Formulations,
     * or a forbidden Delete command).
     *
     * Returns `null` if the message is a normal informational Q&A question.
     *
     * @return ?array<string, mixed>
     */
    public function detectAndExecuteFromChat(?User $user, string $question, string $language = 'en', string $productVersion = '2026.10'): ?array
    {
        $trimmed = trim($question);
        if ($trimmed === '') {
            return null;
        }

        $lower = mb_strtolower($trimmed, 'UTF-8');

        // 1. Do NOT intercept informational "how to / what is / why / explain / show me" questions
        $isInformationalQuestion = (bool) preg_match(
            '/^(how\s+(to|do|can|does|should)|what\s+(is|are|does|happens)|where\s+(is|can|do)|why\s+(is|do|does|can|cant|cannot)|when\s+(should|do)|explain\b|compare\b|difference\b|show\s+me\b|walk\s+me\b|take\s+me\b|demonstrate\b|tell\s+me\s+(how|about|what))/iu',
            $lower
        ) || (bool) preg_match(
            '/\b(how\s+to\s+(add|create|edit|update|delete|remove|make)|kaise\s+(add|create|edit|update|delete|banayein|karein)|kesay\s+(add|create|edit|delete)|tareeqa\s+kya|tarika\s+kya|کیسے\s+(بنائیں|کریں|ڈالیں|ڈیلیٹ)|طریقہ\s+کیا)\b/iu',
            $lower
        );

        if ($isInformationalQuestion) {
            return null;
        }

        // 2. HARDCODED NON-DESTRUCTIVE GUARDRAIL: Block any imperative Delete / Remove / Wipe command
        if ($this->isDestructiveDeleteCommand($lower)) {
            return $this->buildDeleteBlockedResponse($productVersion);
        }

        // 3. Check if the message is a Bulk Formulation command, Add Product command, or Edit Product command
        $intentType = $this->classifyActionIntent($lower);
        if ($intentType === null) {
            return null;
        }

        if (! $user) {
            return $this->buildUnauthorizedResponse($productVersion);
        }

        if ($intentType === 'bulk_autofill_missing_formulations') {
            return $this->executeBulkAutofillMissingFormulations($user, [
                'batch_size' => 20,
                'after_id' => 0,
                'filter_query' => $this->extractOptionalFilterFromPrompt($trimmed),
            ], $productVersion);
        }

        // Parse structured parameters using AI (with deterministic local parser fallback)
        $parsedTask = $this->parseTaskWithAiOrLocal($trimmed, $intentType);

        $action = (string) ($parsedTask['action'] ?? $intentType);
        if (str_contains($action, 'delete') || str_contains($action, 'remove') || str_contains($action, 'destroy')) {
            return $this->buildDeleteBlockedResponse($productVersion);
        }

        if ($action === 'bulk_autofill_missing_formulations') {
            return $this->executeBulkAutofillMissingFormulations($user, [
                'batch_size' => 20,
                'after_id' => 0,
                'filter_query' => (string) ($parsedTask['filter_query'] ?? ''),
            ], $productVersion);
        }

        if ($action === 'bulk_assign_formulations') {
            return $this->executeBulkAssignFormulations($user, $parsedTask, $productVersion);
        }

        if ($action === 'create_products') {
            return $this->executeCreateProducts($user, $parsedTask, $productVersion);
        }

        if ($action === 'update_products') {
            return $this->executeUpdateProducts($user, $parsedTask, $productVersion);
        }

        return null;
    }

    /**
     * Executes a structured task directly via `POST /api/cube/execute-task`
     * (used by background formulation batch continuation and Quick Task buttons).
     *
     * @param  array<string, mixed>  $payload
     * @return array<string, mixed>
     */
    public function executeStructuredTask(?User $user, array $payload, string $productVersion = '2026.10'): array
    {
        if (! $user) {
            return $this->buildUnauthorizedResponse($productVersion);
        }

        $taskType = trim((string) ($payload['task_type'] ?? ''));

        // Hardcoded non-destructive check: never allow any delete task type
        if (
            $taskType === '' ||
            str_contains(strtolower($taskType), 'delete') ||
            str_contains(strtolower($taskType), 'remove') ||
            str_contains(strtolower($taskType), 'destroy')
        ) {
            return $this->buildDeleteBlockedResponse($productVersion);
        }

        return match ($taskType) {
            'bulk_autofill_missing_formulations' => $this->executeBulkAutofillMissingFormulations($user, $payload, $productVersion),
            'bulk_assign_formulations' => $this->executeBulkAssignFormulations($user, $payload, $productVersion),
            'create_products' => $this->executeCreateProducts($user, $payload, $productVersion),
            'update_products' => $this->executeUpdateProducts($user, $payload, $productVersion),
            default => $this->buildDeleteBlockedResponse($productVersion),
        };
    }

    /**
     * Detects imperative delete/remove commands (which are strictly forbidden in Cube).
     */
    protected function isDestructiveDeleteCommand(string $lower): bool
    {
        return (bool) preg_match(
            '/^(?:please\s+|cube\s+|plz\s+)?(?:delete|remove|destroy|erase|wipe|truncate|drop)\s+(?:the\s+|this\s+|all\s+|my\s+)?(?:product|products|item|items|medicine|medicines|formulation|formulations|category|categories|brand|brands|supplier|suppliers|customer|customers|invoice|invoices|batch|batches|stock|record|records)\b/iu',
            $lower
        ) || (bool) preg_match(
            '/\b(?:product|medicine|item|record|formulation)\b.*\b(?:delete\s+kar|remove\s+kar|khatam\s+kar|مٹا\s+دیں|ڈیلیٹ\s+کر)\b/iu',
            $lower
        );
    }

    /**
     * Classifies whether a user chat message is an action command for Cube.
     */
    protected function classifyActionIntent(string $lower): ?string
    {
        // 1. Background auto-fill missing formulations across products
        if (
            preg_match('/\b(auto[\s\-]?fill|fill|populate|generate|add|assign|update|set)\b.*\b(formulation|formulations|generic|salt|salts)\b.*\b(missing|empty|all\s+products|multiple\s+products|products|medicines|items|in\s+background|background)\b/iu', $lower) ||
            preg_match('/\b(formulation|formulations|salt|salts)\b.*\b(add\s+kar|fill\s+kar|laga\s+do|laga\s+dein|ڈال\s+دیں|شامل\s+کر)\b/iu', $lower)
        ) {
            // Check if the user is assigning a specific formulation to specific named products vs auto-filling missing ones
            if (
                preg_match('/\b(missing|empty|without\s+formulation|all\s+products|my\s+products|in\s+background|background|auto[\s\-]?fill)\b/iu', $lower) ||
                preg_match('/^(?:please\s+|cube\s+)?(?:add|fill|populate|update|assign)\s+formulations?\s+(?:to|for|in)\s+(?:all\s+|my\s+)?products\b/iu', $lower)
            ) {
                return 'bulk_autofill_missing_formulations';
            }

            return 'bulk_assign_formulations';
        }

        // 2. Explicit formulation assignment (e.g. "set formulation Paracetamol for Panadol and Calpol")
        if (
            preg_match('/^(?:please\s+|cube\s+)?(?:set|assign|add|update|change)\s+(?:the\s+)?formulations?\b/iu', $lower) ||
            preg_match('/^(?:please\s+|cube\s+)?add\s+formulations?\s*:/iu', $lower)
        ) {
            return 'bulk_assign_formulations';
        }

        // 3. Create / Add Product(s) command
        if (
            preg_match('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:add|create|register|insert|make)\s+(?:a\s+)?(?:new\s+)?(?:product|products|medicine|medicines|item|items)\b/iu', $lower) ||
            preg_match('/\b(?:new\s+product|product)\s+(?:add|create|banayein|bana\s+do|add\s+kar\s+do|شامل\s+کریں|بنا\s+دیں)\b/iu', $lower)
        ) {
            return 'create_products';
        }

        // 4. Edit / Update Product(s) command
        if (
            preg_match('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:edit|update|change|modify|set)\s+(?:the\s+)?(?:product|medicine|item|price|pack\s+price|sale\s+price|purchase\s+price|pack\s+size|rack|max\s+discount|category|brand|formulation)\b/iu', $lower) ||
            preg_match('/\b(?:product|medicine)\s+(?:update|edit|change)\s+(?:kar\s+do|karein|کر\s+دیں)\b/iu', $lower)
        ) {
            return 'update_products';
        }

        return null;
    }

    /**
     * Uses Groq/Gemini AI (with deterministic local regex fallback) to extract structured
     * product creation, product update, or formulation assignment parameters.
     *
     * @return array<string, mixed>
     */
    protected function parseTaskWithAiOrLocal(string $rawText, string $defaultIntent): array
    {
        try {
            if (Schema::hasTable('settings')) {
                Setting::first()?->applyCubeAiConfig();
            }
        } catch (Throwable) {
            // Ignore
        }

        $systemPrompt = <<<'SYSTEM'
You are Cube's structured ERP Task Parser for Karobar-App.
Convert the user's natural-language request into a strict JSON object for NON-DESTRUCTIVE product and formulation tasks.
HARDCODED SAFETY RULE: Deleting anything is strictly forbidden. If the user asks to delete or remove anything, set "action": "delete_blocked".

Supported actions:
1. "create_products": Adding 1 or more new products.
   Schema:
   {
     "action": "create_products",
     "products": [
       {
         "name": "Product Name (required)",
         "formulation": "Generic / salt name (infer accurate pharmaceutical salt if it is a known medicine)",
         "pack_size": 10,
         "pack_purchase_price": 100,
         "pack_sale_price": 120,
         "unit_purchase_price": null,
         "unit_sale_price": null,
         "whole_sale_pack_price": null,
         "whole_sale_unit_price": null,
         "category": "Category name (e.g. Tablets, Syrups, Capsules, Injections, General)",
         "brand": "Brand / Manufacturer name",
         "supplier": "Supplier name",
         "rack": "Rack code if mentioned",
         "narcotic": "no",
         "max_discount": null,
         "description": null
       }
     ]
   }

2. "update_products": Editing 1 or more existing products. Only include fields the user wants to change (leave others null).
   Schema:
   {
     "action": "update_products",
     "updates": [
       {
         "target_name_or_code": "Existing product name or PRD-XXXX code",
         "new_name": null,
         "formulation": null,
         "pack_size": null,
         "pack_purchase_price": null,
         "pack_sale_price": null,
         "unit_purchase_price": null,
         "unit_sale_price": null,
         "whole_sale_pack_price": null,
         "whole_sale_unit_price": null,
         "category": null,
         "brand": null,
         "supplier": null,
         "rack": null,
         "narcotic": null,
         "max_discount": null,
         "expiry_warning_days": null,
         "description": null
       }
     ]
   }

3. "bulk_assign_formulations": Setting formulations for specific product names or registering new formulations.
   Schema:
   {
     "action": "bulk_assign_formulations",
     "assignments": [
       {
         "product_name": "Product name or prefix",
         "formulation": "Generic salt / formulation name"
       }
     ],
     "standalone_formulations": ["List of standalone formulation names to register if no product specified"]
   }

4. "bulk_autofill_missing_formulations": Auto-filling missing formulations across multiple products in the database.
   Schema:
   {
     "action": "bulk_autofill_missing_formulations",
     "filter_query": ""
   }

Respond ONLY with the valid JSON object.
SYSTEM;

        $userPrompt = "Default detected intent: {$defaultIntent}\nUser request:\n{$rawText}";

        $aiData = $this->callAiJson($systemPrompt, $userPrompt);
        if (is_array($aiData) && ! empty($aiData['action'])) {
            return $aiData;
        }

        // Local deterministic fallback parser when AI is offline or rate-limited
        return $this->parseTaskLocally($rawText, $defaultIntent);
    }

    /**
     * Local regex parser fallback so Cube tasks work even if external AI is temporarily rate-limited.
     *
     * @return array<string, mixed>
     */
    protected function parseTaskLocally(string $rawText, string $defaultIntent): array
    {
        if ($defaultIntent === 'bulk_assign_formulations') {
            $assignments = [];
            $standalone = [];

            // Pattern: "set formulation X for Y, Z" or "for Y set formulation X"
            if (preg_match('/formulations?\s+(?:of\s+)?([A-Za-z0-9\s\-\+]+?)\s+(?:to|as|=)\s+([A-Za-z0-9\s\-\+\/,]+)/iu', $rawText, $m)) {
                $prodCandidate = trim($m[1]);
                $formCandidate = trim($m[2]);
                if (strtolower($prodCandidate) !== 'product' && strtolower($prodCandidate) !== 'products') {
                    $assignments[] = [
                        'product_name' => $prodCandidate,
                        'formulation' => $formCandidate,
                    ];
                }
            }

            if (empty($assignments) && preg_match('/(?:set|assign|add)\s+formulations?\s+([A-Za-z0-9\s\-\+]+?)\s+(?:for|to|on)\s+(.+)$/iu', $rawText, $m)) {
                $formulation = trim($m[1]);
                $prodList = preg_split('/(?:,|\band\b)/i', $m[2]) ?: [];
                foreach ($prodList as $pName) {
                    $cleanP = trim($pName, " \t\n\r\0\x0B.");
                    $cleanP = preg_replace('/^(?:product|products|medicine|medicines)\s+/i', '', $cleanP) ?? $cleanP;
                    if ($cleanP !== '') {
                        $assignments[] = [
                            'product_name' => $cleanP,
                            'formulation' => $formulation,
                        ];
                    }
                }
            }

            if (empty($assignments) && preg_match('/add\s+formulations?\s*:?\s*(.+)$/iu', $rawText, $m)) {
                $parts = preg_split('/(?:,|\band\b|\n)/i', $m[1]) ?: [];
                foreach ($parts as $part) {
                    $clean = trim($part, " \t\n\r\0\x0B.");
                    if ($clean !== '') {
                        $standalone[] = $clean;
                    }
                }
            }

            return [
                'action' => 'bulk_assign_formulations',
                'assignments' => $assignments,
                'standalone_formulations' => $standalone,
            ];
        }

        if ($defaultIntent === 'create_products') {
            $body = preg_replace('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:add|create|register|insert|make)\s+(?:a\s+)?(?:new\s+)?(?:product|products|medicine|medicines|item|items)\s*(?:named|called|:)?\s*/iu', '', $rawText) ?? $rawText;

            $packSize = 1;
            if (preg_match('/pack\s*size\s*(?:is|=|:)?\s*(\d+)/iu', $body, $m)) {
                $packSize = max(1, (int) $m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            $packPurchasePrice = null;
            if (preg_match('/(?:pack\s*)?(?:purchase|cost|buy)\s*price\s*(?:is|=|:)?\s*(\d+(?:\.\d+)?)/iu', $body, $m)) {
                $packPurchasePrice = (float) $m[1];
                $body = str_replace($m[0], ' ', $body);
            }

            $packSalePrice = null;
            if (preg_match('/(?:pack\s*)?(?:sale|sell|retail)?\s*price\s*(?:is|=|:)?\s*(\d+(?:\.\d+)?)/iu', $body, $m)) {
                $packSalePrice = (float) $m[1];
                $body = str_replace($m[0], ' ', $body);
            }

            $formulation = null;
            if (preg_match('/(?:formulation|salt|generic)\s*(?:is|=|:)?\s*([A-Za-z0-9\s\+]+?)(?=,|\bpack\b|\bprice\b|\bbrand\b|\bcategory\b|\brack\b|$)/iu', $body, $m)) {
                $formulation = trim($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            $brand = null;
            if (preg_match('/brand\s*(?:is|=|:)?\s*([A-Za-z0-9\s\-]+?)(?=,|\bpack\b|\bprice\b|\bcategory\b|\bformulation\b|\brack\b|$)/iu', $body, $m)) {
                $brand = trim($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            $category = null;
            if (preg_match('/category\s*(?:is|=|:)?\s*([A-Za-z0-9\s\-]+?)(?=,|\bpack\b|\bprice\b|\bbrand\b|\bformulation\b|\brack\b|$)/iu', $body, $m)) {
                $category = trim($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            $rack = null;
            if (preg_match('/rack\s*(?:is|=|:)?\s*([A-Za-z0-9\-]+)/iu', $body, $m)) {
                $rack = trim($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            $namePart = trim( explode(',', $body)[0] ?? $body );
            $namePart = trim(preg_replace('/\s+/', ' ', $namePart) ?? $namePart, " \t\n\r\0\x0B.,:-");

            return [
                'action' => 'create_products',
                'products' => $namePart !== '' ? [
                    [
                        'name' => $namePart,
                        'formulation' => $formulation,
                        'pack_size' => $packSize,
                        'pack_purchase_price' => $packPurchasePrice,
                        'pack_sale_price' => $packSalePrice,
                        'brand' => $brand,
                        'category' => $category,
                        'rack' => $rack,
                    ],
                ] : [],
            ];
        }

        if ($defaultIntent === 'update_products') {
            $body = preg_replace('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:edit|update|change|modify|set)\s+(?:the\s+)?(?:product|medicine|item)?\s*/iu', '', $rawText) ?? $rawText;

            $update = [
                'target_name_or_code' => '',
            ];

            if (preg_match('/\b(PRD-\d+)\b/i', $body, $m)) {
                $update['target_name_or_code'] = strtoupper($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            if (preg_match('/pack\s*size\s*(?:to|is|=|:)?\s*(\d+)/iu', $body, $m)) {
                $update['pack_size'] = max(1, (int) $m[1]);
                $body = str_replace($m[0], ' ', $body);
            }
            if (preg_match('/(?:pack\s*)?(?:purchase|cost)\s*price\s*(?:to|is|=|:)?\s*(\d+(?:\.\d+)?)/iu', $body, $m)) {
                $update['pack_purchase_price'] = (float) $m[1];
                $body = str_replace($m[0], ' ', $body);
            }
            if (preg_match('/(?:pack\s*)?(?:sale|sell|retail)?\s*price\s*(?:to|is|=|:)?\s*(\d+(?:\.\d+)?)/iu', $body, $m)) {
                $update['pack_sale_price'] = (float) $m[1];
                $body = str_replace($m[0], ' ', $body);
            }
            if (preg_match('/(?:formulation|salt|generic)\s*(?:to|is|=|:)?\s*([A-Za-z0-9\s\+]+?)(?=,|\band\b|\bpack\b|\bprice\b|\brack\b|$)/iu', $body, $m)) {
                $update['formulation'] = trim($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }
            if (preg_match('/rack\s*(?:to|is|=|:)?\s*([A-Za-z0-9\-]+)/iu', $body, $m)) {
                $update['rack'] = trim($m[1]);
                $body = str_replace($m[0], ' ', $body);
            }

            if ($update['target_name_or_code'] === '') {
                $cleanTarget = preg_replace('/\b(for|of|on|to|and|set|change|update|edit)\b/iu', ' ', $body) ?? $body;
                $cleanTarget = trim(preg_replace('/\s+/', ' ', $cleanTarget) ?? '', " \t\n\r\0\x0B.,:-");
                $update['target_name_or_code'] = $cleanTarget;
            }

            return [
                'action' => 'update_products',
                'updates' => $update['target_name_or_code'] !== '' ? [$update] : [],
            ];
        }

        return ['action' => $defaultIntent];
    }

    /**
     * Executes creation of 1 or multiple products (non-destructive).
     *
     * @param  array<string, mixed>  $taskData
     * @return array<string, mixed>
     */
    protected function executeCreateProducts(User $user, array $taskData, string $productVersion): array
    {
        if (! $this->canCreateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.create', $productVersion);
        }

        $items = is_array($taskData['products'] ?? null) ? $taskData['products'] : [];
        if (empty($items)) {
            return $this->formatTaskResponse(
                titleEn: 'More Product Details Needed',
                titleUr: 'پروڈکٹ شامل کرنے کے لیے نام درکار ہے',
                answerEn: "Please specify the **Product Name** (and optional Pack Size, Purchase/Sale Price, Brand, Category, and Formulation).\n\n**Example:**\n`Add product Panadol 500mg Tablet, formulation Paracetamol, pack size 200, pack purchase price 510, pack sale price 600, brand GSK, category Tablets`",
                answerUr: "براہِ کرم پروڈکٹ کا **نام** (اور پیک سائز، قیمت خرید/فروخت، برانڈ اور فارمولیشن) لکھیں۔\n\n**مثال:**\n`Add product Panadol 500mg Tablet, formulation Paracetamol, pack size 200, pack sale price 600`",
                links: [['path' => '/products/create', 'en' => 'Open Add Product Form', 'ur' => 'پروڈکٹ فارم کھولیں']],
                taskMeta: ['action' => 'create_products', 'created_count' => 0],
                productVersion: $productVersion
            );
        }

        $created = [];
        $alreadyExisted = [];

        foreach (array_slice($items, 0, 25) as $rawItem) {
            if (! is_array($rawItem)) {
                continue;
            }
            $name = trim((string) ($rawItem['name'] ?? ''));
            if ($name === '') {
                continue;
            }

            // Check if product already exists by case-insensitive name
            $existing = Product::query()->whereRaw('LOWER(name) = ?', [mb_strtolower($name, 'UTF-8')])->first();
            if ($existing) {
                // Non-destructive enrichment: if existing product is missing formulation and we have one, fill it!
                $inferredForm = trim((string) ($rawItem['formulation'] ?? '')) ?: $this->inferFormulationFromProductName($name);
                if (empty($existing->formulation) && $inferredForm !== null && $inferredForm !== '') {
                    $resolvedF = Formulation::resolveOrCreate($inferredForm);
                    $existing->formulation = $resolvedF?->name ?? $inferredForm;
                    $existing->save();
                }
                $alreadyExisted[] = [
                    'id' => $existing->id,
                    'product_code' => $existing->product_code,
                    'name' => $existing->name,
                    'formulation' => $existing->formulation,
                ];
                continue;
            }

            $codeAndBarcode = $this->generateUniqueCodeAndBarcode();
            $packSize = max(1, (int) ($rawItem['pack_size'] ?? 1));

            $packPurchase = isset($rawItem['pack_purchase_price']) && is_numeric($rawItem['pack_purchase_price'])
                ? round((float) $rawItem['pack_purchase_price'], 2)
                : null;
            $unitPurchase = isset($rawItem['unit_purchase_price']) && is_numeric($rawItem['unit_purchase_price'])
                ? round((float) $rawItem['unit_purchase_price'], 2)
                : null;

            if ($packPurchase !== null && $unitPurchase === null) {
                $unitPurchase = round($packPurchase / $packSize, 2);
            } elseif ($unitPurchase !== null && $packPurchase === null) {
                $packPurchase = round($unitPurchase * $packSize, 2);
            }

            $packSale = isset($rawItem['pack_sale_price']) && is_numeric($rawItem['pack_sale_price'])
                ? round((float) $rawItem['pack_sale_price'], 2)
                : null;
            $unitSale = isset($rawItem['unit_sale_price']) && is_numeric($rawItem['unit_sale_price'])
                ? round((float) $rawItem['unit_sale_price'], 2)
                : null;

            if ($packSale !== null && $unitSale === null) {
                $unitSale = round($packSale / $packSize, 2);
            } elseif ($unitSale !== null && $packSale === null) {
                $packSale = round($unitSale * $packSize, 2);
            }

            $rawFormulation = trim((string) ($rawItem['formulation'] ?? ''));
            if ($rawFormulation === '') {
                $rawFormulation = (string) ($this->inferFormulationFromProductName($name) ?? '');
            }
            $resolvedFormulation = $rawFormulation !== '' ? Formulation::resolveOrCreate($rawFormulation) : null;

            $categoryName = trim((string) ($rawItem['category'] ?? '')) ?: $this->inferCategoryFromProductName($name);
            $category = $this->resolveOrCreateCategory($categoryName);

            $brandName = trim((string) ($rawItem['brand'] ?? '')) ?: 'General';
            $brand = $this->resolveOrCreateBrand($brandName);

            $supplierName = trim((string) ($rawItem['supplier'] ?? '')) ?: 'General Supplier';
            $supplier = $this->resolveOrCreateSupplier($supplierName);

            $storageLocationId = null;
            if (Schema::hasTable('storage_locations')) {
                $storageLocationId = StorageLocation::where('is_default', true)->value('id')
                    ?? StorageLocation::orderBy('id')->value('id');
            }

            $avgPrice = $unitPurchase ?? 0.0;
            $margin = ($unitSale !== null && $unitSale > 0 && $avgPrice > 0)
                ? round((($unitSale - $avgPrice) / $unitSale) * 100, 2)
                : 0.0;

            $product = Product::create([
                'product_code' => $codeAndBarcode['product_code'],
                'barcode' => $codeAndBarcode['barcode'],
                'name' => $name,
                'formulation' => $resolvedFormulation?->name,
                'description' => isset($rawItem['description']) ? trim((string) $rawItem['description']) : null,
                'pack_size' => $packSize,
                'quantity' => 0,
                'pack_purchase_price' => $packPurchase ?? 0,
                'unit_purchase_price' => $unitPurchase ?? 0,
                'pack_sale_price' => $packSale ?? 0,
                'unit_sale_price' => $unitSale ?? 0,
                'whole_sale_pack_price' => isset($rawItem['whole_sale_pack_price']) && is_numeric($rawItem['whole_sale_pack_price'])
                    ? round((float) $rawItem['whole_sale_pack_price'], 2)
                    : ($packSale ?? 0),
                'whole_sale_unit_price' => isset($rawItem['whole_sale_unit_price']) && is_numeric($rawItem['whole_sale_unit_price'])
                    ? round((float) $rawItem['whole_sale_unit_price'], 2)
                    : ($unitSale ?? 0),
                'avg_price' => $avgPrice,
                'margin' => $margin,
                'whole_sale_margin' => $margin,
                'narcotic' => in_array(($rawItem['narcotic'] ?? 'no'), ['yes', 'no'], true) ? ($rawItem['narcotic'] ?? 'no') : 'no',
                'max_discount' => isset($rawItem['max_discount']) && is_numeric($rawItem['max_discount']) ? (int) $rawItem['max_discount'] : 0,
                'expiry_warning_days' => isset($rawItem['expiry_warning_days']) && is_numeric($rawItem['expiry_warning_days']) ? (int) $rawItem['expiry_warning_days'] : 90,
                'category_id' => $category->id,
                'brand_id' => $brand->id,
                'supplier_id' => $supplier->id,
                'rack' => isset($rawItem['rack']) ? trim((string) $rawItem['rack']) : null,
                'storage_location_id' => $storageLocationId,
            ]);

            if (! empty($product->supplier_id) && Schema::hasTable('product_suppliers')) {
                ProductSupplier::updateOrCreate(
                    ['product_id' => $product->id, 'supplier_id' => $product->supplier_id],
                    [
                        'last_purchase_price' => $product->unit_purchase_price,
                        'is_preferred' => true,
                    ]
                );
            }

            $created[] = [
                'id' => $product->id,
                'product_code' => $product->product_code,
                'name' => $product->name,
                'formulation' => $product->formulation,
                'pack_size' => $product->pack_size,
                'pack_purchase_price' => $product->pack_purchase_price,
                'pack_sale_price' => $product->pack_sale_price,
                'category' => $category->name,
                'brand' => $brand->name,
                'rack' => $product->rack,
            ];
        }

        $linesEn = [];
        $linesUr = [];

        if (! empty($created)) {
            $linesEn[] = '✅ **Cube AI Task Completed — Created '.count($created).' Product(s):**';
            $linesUr[] = '✅ **کیوب اے آئی نے '.count($created).' نئی پروڈکٹ(س) کامیابی سے شامل کر دیں:**';

            foreach ($created as $idx => $c) {
                $num = $idx + 1;
                $formStr = $c['formulation'] ? " • Formulation: **{$c['formulation']}**" : '';
                $linesEn[] = "{$num}. **{$c['name']}** (`{$c['product_code']}`){$formStr} • Pack Size: `{$c['pack_size']}` • Pack Sale: `{$c['pack_sale_price']}` • Category: `{$c['category']}` • Brand: `{$c['brand']}`";
                $linesUr[] = "{$num}. **{$c['name']}** (`{$c['product_code']}`){$formStr} • پیک سائز: `{$c['pack_size']}` • قیمت فروخت: `{$c['pack_sale_price']}`";
            }
        }

        if (! empty($alreadyExisted)) {
            $names = implode(', ', array_map(fn ($e) => "**{$e['name']}** (`{$e['product_code']}`)", $alreadyExisted));
            $linesEn[] = "ℹ️ Already in catalog (skipped duplicate creation): {$names}";
            $linesUr[] = "ℹ️ یہ پروڈکٹس پہلے سے موجود ہیں: {$names}";
        }

        $links = [
            ['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں'],
        ];
        if (count($created) === 1) {
            $links[] = [
                'path' => '/products/'.$created[0]['id'].'/edit',
                'en' => 'Edit '.$created[0]['name'],
                'ur' => $created[0]['name'].' ایڈٹ کریں',
            ];
        }

        return $this->formatTaskResponse(
            titleEn: count($created) > 0
                ? 'Added '.count($created).' Product(s) to Catalog'
                : 'Product Already Exists in Catalog',
            titleUr: count($created) > 0
                ? count($created).' نئی پروڈکٹ(س) کیٹلاگ میں شامل کر دی گئیں'
                : 'پروڈکٹ پہلے سے کیٹلاگ میں موجود ہے',
            answerEn: implode("\n\n", $linesEn),
            answerUr: implode("\n\n", $linesUr),
            links: $links,
            taskMeta: [
                'action' => 'create_products',
                'created_count' => count($created),
                'created_products' => $created,
                'already_existed' => $alreadyExisted,
                'refresh_products' => true,
            ],
            productVersion: $productVersion
        );
    }

    /**
     * Executes non-destructive updates on 1 or multiple existing products.
     *
     * @param  array<string, mixed>  $taskData
     * @return array<string, mixed>
     */
    protected function executeUpdateProducts(User $user, array $taskData, string $productVersion): array
    {
        if (! $this->canUpdateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.update', $productVersion);
        }

        $updates = is_array($taskData['updates'] ?? null) ? $taskData['updates'] : [];
        if (empty($updates)) {
            return $this->formatTaskResponse(
                titleEn: 'Specify Which Product & Field to Edit',
                titleUr: 'ایڈٹ کرنے کے لیے پروڈکٹ کا نام اور تبدیلی بتائیں',
                answerEn: "Please tell me which product you want to edit and what to change.\n\n**Example:**\n`Update product Panadol pack sale price to 650, formulation Paracetamol, and rack to R-12`",
                answerUr: "براہِ کرم پروڈکٹ کا نام اور جو تبدیلی کرنی ہے وہ لکھیں۔\n\n**مثال:**\n`Update product Panadol pack sale price to 650 and rack to R-12`",
                links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
                taskMeta: ['action' => 'update_products', 'updated_count' => 0],
                productVersion: $productVersion
            );
        }

        $updatedRecords = [];
        $notFoundTargets = [];

        foreach (array_slice($updates, 0, 25) as $item) {
            if (! is_array($item)) {
                continue;
            }
            $target = trim((string) ($item['target_name_or_code'] ?? ($item['name'] ?? '')));
            if ($target === '') {
                continue;
            }

            $product = $this->findProductByNameOrCode($target);
            if (! $product) {
                $notFoundTargets[] = $target;
                continue;
            }

            $changes = [];

            if (! empty($item['new_name'])) {
                $newName = trim((string) $item['new_name']);
                $conflict = Product::whereRaw('LOWER(name) = ?', [mb_strtolower($newName, 'UTF-8')])
                    ->where('id', '!=', $product->id)
                    ->exists();
                if ($newName !== '' && ! $conflict) {
                    $product->name = $newName;
                    $changes[] = "Name → **{$newName}**";
                }
            }

            if (isset($item['formulation']) && trim((string) $item['formulation']) !== '') {
                $resolvedF = Formulation::resolveOrCreate((string) $item['formulation']);
                $product->formulation = $resolvedF?->name ?? trim((string) $item['formulation']);
                $changes[] = "Formulation → **{$product->formulation}**";
            }

            if (isset($item['pack_size']) && is_numeric($item['pack_size']) && (int) $item['pack_size'] >= 1) {
                $product->pack_size = (int) $item['pack_size'];
                $changes[] = "Pack Size → `{$product->pack_size}`";
            }

            $packSize = max(1, (int) ($product->pack_size ?: 1));

            if (isset($item['pack_purchase_price']) && is_numeric($item['pack_purchase_price'])) {
                $product->pack_purchase_price = round((float) $item['pack_purchase_price'], 2);
                if (! isset($item['unit_purchase_price']) || ! is_numeric($item['unit_purchase_price'])) {
                    $product->unit_purchase_price = round($product->pack_purchase_price / $packSize, 2);
                }
                $changes[] = "Pack Purchase Price → `{$product->pack_purchase_price}`";
            }

            if (isset($item['unit_purchase_price']) && is_numeric($item['unit_purchase_price'])) {
                $product->unit_purchase_price = round((float) $item['unit_purchase_price'], 2);
                if (! isset($item['pack_purchase_price']) || ! is_numeric($item['pack_purchase_price'])) {
                    $product->pack_purchase_price = round($product->unit_purchase_price * $packSize, 2);
                }
                $changes[] = "Unit Purchase Price → `{$product->unit_purchase_price}`";
            }

            if (isset($item['pack_sale_price']) && is_numeric($item['pack_sale_price'])) {
                $product->pack_sale_price = round((float) $item['pack_sale_price'], 2);
                if (! isset($item['unit_sale_price']) || ! is_numeric($item['unit_sale_price'])) {
                    $product->unit_sale_price = round($product->pack_sale_price / $packSize, 2);
                }
                $changes[] = "Pack Sale Price → `{$product->pack_sale_price}`";
            }

            if (isset($item['unit_sale_price']) && is_numeric($item['unit_sale_price'])) {
                $product->unit_sale_price = round((float) $item['unit_sale_price'], 2);
                if (! isset($item['pack_sale_price']) || ! is_numeric($item['pack_sale_price'])) {
                    $product->pack_sale_price = round($product->unit_sale_price * $packSize, 2);
                }
                $changes[] = "Unit Sale Price → `{$product->unit_sale_price}`";
            }

            if (isset($item['whole_sale_pack_price']) && is_numeric($item['whole_sale_pack_price'])) {
                $product->whole_sale_pack_price = round((float) $item['whole_sale_pack_price'], 2);
                $changes[] = "Wholesale Pack Price → `{$product->whole_sale_pack_price}`";
            }

            if (isset($item['whole_sale_unit_price']) && is_numeric($item['whole_sale_unit_price'])) {
                $product->whole_sale_unit_price = round((float) $item['whole_sale_unit_price'], 2);
                $changes[] = "Wholesale Unit Price → `{$product->whole_sale_unit_price}`";
            }

            if (! empty($item['category'])) {
                $cat = $this->resolveOrCreateCategory((string) $item['category']);
                $product->category_id = $cat->id;
                $changes[] = "Category → **{$cat->name}**";
            }

            if (! empty($item['brand'])) {
                $br = $this->resolveOrCreateBrand((string) $item['brand']);
                $product->brand_id = $br->id;
                $changes[] = "Brand → **{$br->name}**";
            }

            if (! empty($item['supplier'])) {
                $sup = $this->resolveOrCreateSupplier((string) $item['supplier']);
                $product->supplier_id = $sup->id;
                $changes[] = "Supplier → **{$sup->name}**";
            }

            if (isset($item['rack']) && trim((string) $item['rack']) !== '') {
                $product->rack = trim((string) $item['rack']);
                $changes[] = "Rack → `{$product->rack}`";
            }

            if (isset($item['max_discount']) && is_numeric($item['max_discount'])) {
                $product->max_discount = max(0, (int) $item['max_discount']);
                $changes[] = "Max Discount → `{$product->max_discount}%`";
            }

            if (isset($item['expiry_warning_days']) && is_numeric($item['expiry_warning_days'])) {
                $product->expiry_warning_days = max(1, min(3650, (int) $item['expiry_warning_days']));
                $changes[] = "Expiry Warning → `{$product->expiry_warning_days} days`";
            }

            if (isset($item['narcotic']) && in_array($item['narcotic'], ['yes', 'no'], true)) {
                $product->narcotic = $item['narcotic'];
                $changes[] = "Narcotic → `{$product->narcotic}`";
            }

            if (isset($item['description']) && trim((string) $item['description']) !== '') {
                $product->description = trim((string) $item['description']);
                $changes[] = 'Description updated';
            }

            if (! empty($changes)) {
                $product->save();
                $updatedRecords[] = [
                    'id' => $product->id,
                    'product_code' => $product->product_code,
                    'name' => $product->name,
                    'changes' => $changes,
                ];
            }
        }

        $linesEn = [];
        $linesUr = [];

        if (! empty($updatedRecords)) {
            $linesEn[] = '✅ **Cube AI Task Completed — Updated '.count($updatedRecords).' Product(s):**';
            $linesUr[] = '✅ **کیوب اے آئی نے '.count($updatedRecords).' پروڈکٹ(س) کامیابی سے اپڈیٹ کر دیں:**';

            foreach ($updatedRecords as $idx => $rec) {
                $num = $idx + 1;
                $changeStr = implode(' • ', $rec['changes']);
                $linesEn[] = "{$num}. **{$rec['name']}** (`{$rec['product_code']}`): {$changeStr}";
                $linesUr[] = "{$num}. **{$rec['name']}** (`{$rec['product_code']}`): {$changeStr}";
            }
        }

        if (! empty($notFoundTargets)) {
            $missingStr = implode(', ', array_map(fn ($t) => "**{$t}**", $notFoundTargets));
            $linesEn[] = "⚠️ Could not find product(s) matching: {$missingStr}";
            $linesUr[] = "⚠️ یہ پروڈکٹ کیٹلاگ میں نہیں ملی: {$missingStr}";
        }

        $links = [
            ['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں'],
        ];
        if (count($updatedRecords) === 1) {
            $links[] = [
                'path' => '/products/'.$updatedRecords[0]['id'].'/edit',
                'en' => 'Inspect '.$updatedRecords[0]['name'],
                'ur' => $updatedRecords[0]['name'].' دیکھیں',
            ];
        }

        return $this->formatTaskResponse(
            titleEn: ! empty($updatedRecords)
                ? 'Updated '.count($updatedRecords).' Product(s)'
                : 'No Matching Product Updated',
            titleUr: ! empty($updatedRecords)
                ? count($updatedRecords).' پروڈکٹ(س) اپڈیٹ کر دی گئیں'
                : 'مطلوبہ پروڈکٹ نہیں ملی',
            answerEn: implode("\n\n", $linesEn) ?: 'No changes were applied.',
            answerUr: implode("\n\n", $linesUr) ?: 'کوئی تبدیلی نہیں کی گئی۔',
            links: $links,
            taskMeta: [
                'action' => 'update_products',
                'updated_count' => count($updatedRecords),
                'updated_products' => $updatedRecords,
                'not_found' => $notFoundTargets,
                'refresh_products' => ! empty($updatedRecords),
            ],
            productVersion: $productVersion
        );
    }

    /**
     * Executes explicit bulk formulation assignments to named products or registers standalone formulations.
     *
     * @param  array<string, mixed>  $taskData
     * @return array<string, mixed>
     */
    protected function executeBulkAssignFormulations(User $user, array $taskData, string $productVersion): array
    {
        if (! $this->canUpdateProducts($user) && ! $this->canCreateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.update', $productVersion);
        }

        $assignments = is_array($taskData['assignments'] ?? null) ? $taskData['assignments'] : [];
        $standalone = is_array($taskData['standalone_formulations'] ?? null) ? $taskData['standalone_formulations'] : [];

        // If neither was extracted, fall back to background auto-fill of missing formulations!
        if (empty($assignments) && empty($standalone)) {
            return $this->executeBulkAutofillMissingFormulations($user, [
                'batch_size' => 20,
                'after_id' => 0,
            ], $productVersion);
        }

        $updatedProducts = [];
        $registeredFormulations = [];

        foreach ($standalone as $rawForm) {
            $resolved = Formulation::resolveOrCreate((string) $rawForm);
            if ($resolved) {
                $registeredFormulations[] = $resolved->name;
            }
        }

        foreach ($assignments as $item) {
            if (! is_array($item)) {
                continue;
            }
            $prodQuery = trim((string) ($item['product_name'] ?? ''));
            $formName = trim((string) ($item['formulation'] ?? ''));
            if ($prodQuery === '' || $formName === '') {
                continue;
            }

            $resolvedF = Formulation::resolveOrCreate($formName);
            $canonicalSalt = $resolvedF?->name ?? $formName;

            $matchingProducts = Product::query()
                ->whereRaw('LOWER(name) = ?', [mb_strtolower($prodQuery, 'UTF-8')])
                ->orWhere('name', 'like', $prodQuery.'%')
                ->orWhere('product_code', $prodQuery)
                ->limit(25)
                ->get();

            foreach ($matchingProducts as $prod) {
                $prod->formulation = $canonicalSalt;
                $prod->save();
                $updatedProducts[] = [
                    'id' => $prod->id,
                    'product_code' => $prod->product_code,
                    'name' => $prod->name,
                    'formulation' => $canonicalSalt,
                ];
            }
        }

        $linesEn = [];
        $linesUr = [];

        if (! empty($updatedProducts)) {
            $linesEn[] = '✅ **Assigned Formulations to '.count($updatedProducts).' Product(s):**';
            $linesUr[] = '✅ **'.count($updatedProducts).' پروڈکٹ(س) میں فارمولیشن (Generic Salt) شامل کر دی گئی:**';
            foreach ($updatedProducts as $idx => $up) {
                $num = $idx + 1;
                $linesEn[] = "{$num}. **{$up['name']}** (`{$up['product_code']}`) → **{$up['formulation']}**";
                $linesUr[] = "{$num}. **{$up['name']}** (`{$up['product_code']}`) ← **{$up['formulation']}**";
            }
        }

        if (! empty($registeredFormulations)) {
            $listStr = implode(', ', array_map(fn ($f) => "**{$f}**", array_unique($registeredFormulations)));
            $linesEn[] = "✅ **Registered Canonical Formulation(s):** {$listStr}";
            $linesUr[] = "✅ **نئی فارمولیشنز محفوظ کر دی گئیں:** {$listStr}";
        }

        return $this->formatTaskResponse(
            titleEn: 'Bulk Formulation Assignment Completed',
            titleUr: 'پروڈکٹس میں فارمولیشن کامیابی سے اپڈیٹ کر دی گئی',
            answerEn: implode("\n\n", $linesEn) ?: 'No matching products were found for formulation assignment.',
            answerUr: implode("\n\n", $linesUr) ?: 'مطلوبہ پروڈکٹ نہیں ملی۔',
            links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
            taskMeta: [
                'action' => 'bulk_assign_formulations',
                'updated_count' => count($updatedProducts),
                'updated_products' => $updatedProducts,
                'registered_formulations' => $registeredFormulations,
                'refresh_products' => ! empty($updatedProducts),
            ],
            productVersion: $productVersion
        );
    }

    /**
     * Background Batch Processor: Automatically identifies and populates missing generic/salt
     * formulations across multiple products at a time using our pharmaceutical dictionary + Groq/Gemini AI.
     *
     * @param  array<string, mixed>  $options
     * @return array<string, mixed>
     */
    public function executeBulkAutofillMissingFormulations(User $user, array $options = [], string $productVersion = '2026.10'): array
    {
        if (! $this->canUpdateProducts($user) && ! $this->canCreateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.update', $productVersion);
        }

        $batchSize = max(1, min(30, (int) ($options['batch_size'] ?? 20)));
        $afterId = max(0, (int) ($options['after_id'] ?? 0));
        $filterQuery = trim((string) ($options['filter_query'] ?? ''));

        $baseQuery = Product::query()
            ->where(function ($q) {
                $q->whereNull('formulation')
                    ->orWhere('formulation', '');
            });

        if ($filterQuery !== '') {
            $baseQuery->where('name', 'like', '%'.$filterQuery.'%');
        }

        $totalMissingBefore = (clone $baseQuery)->count();

        $batchProducts = (clone $baseQuery)
            ->where('id', '>', $afterId)
            ->orderBy('id')
            ->limit($batchSize)
            ->get(['id', 'product_code', 'name', 'formulation']);

        if ($batchProducts->isEmpty()) {
            return $this->formatTaskResponse(
                titleEn: 'All Products Have Formulations',
                titleUr: 'تمام پروڈکٹس میں فارمولیشن پہلے سے موجود ہے',
                answerEn: $totalMissingBefore === 0
                    ? "✅ **All products in your catalog already have a formulation assigned!**\n\nYou can also assign or update specific formulations anytime by typing:\n`Set formulation Paracetamol for Panadol and Calpol`"
                    : '✅ **Background formulation enrichment batch finished!**',
                answerUr: $totalMissingBefore === 0
                    ? "✅ **آپ کے کیٹلاگ کی تمام پروڈکٹس میں فارمولیشن (Generic Salt) پہلے سے موجود ہے!**\n\nاگر آپ کسی مخصوص پروڈکٹ کی فارمولیشن تبدیل کرنا چاہتے ہیں تو لکھیں:\n`Set formulation Paracetamol for Panadol`"
                    : '✅ **پروڈکٹس میں فارمولیشن شامل کرنے کا مرحلہ مکمل ہو گیا!**',
                links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
                taskMeta: [
                    'action' => 'bulk_autofill_missing_formulations',
                    'updated_count' => 0,
                    'updated_products' => [],
                    'background_task' => [
                        'task_type' => 'bulk_autofill_missing_formulations',
                        'has_more' => false,
                        'processed_in_batch' => 0,
                        'remaining_count' => 0,
                        'last_processed_id' => $afterId,
                    ],
                ],
                productVersion: $productVersion
            );
        }

        $resolvedMap = [];
        $unresolvedForAi = [];

        // Pass 1: Fast local pharmaceutical dictionary + embedded generic name detection
        foreach ($batchProducts as $prod) {
            $localSalt = $this->inferFormulationFromProductName((string) $prod->name);
            if ($localSalt !== null && $localSalt !== '') {
                $resolvedMap[$prod->id] = $localSalt;
            } else {
                $unresolvedForAi[$prod->id] = (string) $prod->name;
            }
        }

        // Pass 2: Use connected Groq / Gemini AI for any remaining products in the batch
        if (! empty($unresolvedForAi)) {
            $aiSalts = $this->inferBatchFormulationsWithAi($unresolvedForAi);
            foreach ($aiSalts as $pid => $saltName) {
                $cleanSalt = trim((string) $saltName);
                if (
                    $cleanSalt !== '' &&
                    strtolower($cleanSalt) !== 'unknown' &&
                    strtolower($cleanSalt) !== 'null' &&
                    strtolower($cleanSalt) !== 'n/a'
                ) {
                    $resolvedMap[(int) $pid] = $cleanSalt;
                }
            }
        }

        $updatedItems = [];
        $skippedItems = [];
        $lastProcessedId = $afterId;

        foreach ($batchProducts as $prod) {
            $lastProcessedId = max($lastProcessedId, (int) $prod->id);
            $salt = $resolvedMap[$prod->id] ?? null;
            if ($salt === null || trim($salt) === '') {
                $skippedItems[] = [
                    'id' => $prod->id,
                    'product_code' => $prod->product_code,
                    'name' => $prod->name,
                ];
                continue;
            }

            $canonical = Formulation::resolveOrCreate($salt);
            $finalFormulation = $canonical?->name ?? trim($salt);

            $prod->formulation = $finalFormulation;
            $prod->save();

            $updatedItems[] = [
                'id' => $prod->id,
                'product_code' => $prod->product_code,
                'name' => $prod->name,
                'formulation' => $finalFormulation,
            ];
        }

        $remainingAfterBatch = (clone $baseQuery)
            ->where('id', '>', $lastProcessedId)
            ->count();

        $hasMore = $remainingAfterBatch > 0;

        $linesEn = [];
        $linesUr = [];

        if (! empty($updatedItems)) {
            $linesEn[] = '🧪 **Auto-Filled Formulations for '.count($updatedItems).' Product(s) in This Batch:**';
            $linesUr[] = '🧪 **اس مرحلے میں '.count($updatedItems).' پروڈکٹ(س) کی فارمولیشن (Generic Salt) خودکار طریقے سے شامل کر دی گئی:**';

            foreach (array_slice($updatedItems, 0, 15) as $idx => $up) {
                $num = $idx + 1;
                $linesEn[] = "{$num}. **{$up['name']}** (`{$up['product_code']}`) → **{$up['formulation']}**";
                $linesUr[] = "{$num}. **{$up['name']}** (`{$up['product_code']}`) ← **{$up['formulation']}**";
            }

            if (count($updatedItems) > 15) {
                $extra = count($updatedItems) - 15;
                $linesEn[] = "...and **{$extra} more** product(s) in this batch.";
                $linesUr[] = "...اور مزید **{$extra}** پروڈکٹس۔";
            }
        } else {
            $linesEn[] = 'ℹ️ Processed '.count($batchProducts).' product(s) in this batch (no recognizable medicine formulation found for non-medical/custom names).';
            $linesUr[] = 'ℹ️ اس مرحلے کی پروڈکٹس چیک کی گئیں۔';
        }

        if ($hasMore) {
            $linesEn[] = "⏳ **Background Progress:** `{$remainingAfterBatch}` more product(s) without formulation remaining — Cube is continuing in the background automatically...";
            $linesUr[] = "⏳ **بیک گراؤنڈ پراگریس:** مزید `{$remainingAfterBatch}` پروڈکٹس باقی ہیں — کیوب بیک گراؤنڈ میں خودکار طور پر کام جاری رکھے ہوئے ہے...";
        } else {
            $linesEn[] = '🎉 **All batches completed!** Your product catalog formulations are now up to date.';
            $linesUr[] = '🎉 **تمام مراحل مکمل ہو گئے!** آپ کے پروڈکٹ کیٹلاگ میں فارمولیشنز اپڈیٹ ہو چکی ہیں۔';
        }

        return $this->formatTaskResponse(
            titleEn: $hasMore
                ? 'Background Task: Auto-Filling Product Formulations ('.count($updatedItems).' Updated)'
                : 'Completed: Auto-Filled '.count($updatedItems).' Product Formulation(s)',
            titleUr: $hasMore
                ? 'بیک گراؤنڈ ٹاسک: پروڈکٹس میں فارمولیشن شامل کی جا رہی ہے ('.count($updatedItems).' اپڈیٹ)'
                : 'مکمل: '.count($updatedItems).' پروڈکٹس میں فارمولیشن شامل کر دی گئی',
            answerEn: implode("\n\n", $linesEn),
            answerUr: implode("\n\n", $linesUr),
            links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
            taskMeta: [
                'action' => 'bulk_autofill_missing_formulations',
                'updated_count' => count($updatedItems),
                'updated_products' => $updatedItems,
                'skipped_products' => $skippedItems,
                'refresh_products' => ! empty($updatedItems),
                'background_task' => [
                    'task_type' => 'bulk_autofill_missing_formulations',
                    'has_more' => $hasMore,
                    'processed_in_batch' => count($batchProducts),
                    'updated_in_batch' => count($updatedItems),
                    'remaining_count' => $remainingAfterBatch,
                    'last_processed_id' => $lastProcessedId,
                    'filter_query' => $filterQuery,
                ],
            ],
            productVersion: $productVersion
        );
    }

    /**
     * Infers the active pharmaceutical ingredient (generic salt) for a batch of product names
     * using Groq Primary -> Gemini Backup AI.
     *
     * @param  array<int, string>  $idToProductName
     * @return array<int, string>
     */
    protected function inferBatchFormulationsWithAi(array $idToProductName): array
    {
        if (empty($idToProductName)) {
            return [];
        }

        try {
            if (Schema::hasTable('settings')) {
                Setting::first()?->applyCubeAiConfig();
            }
        } catch (Throwable) {
            // Ignore
        }

        $systemPrompt = <<<'SYSTEM'
You are a pharmaceutical & ERP catalog assistant for Karobar-App.
Given a JSON map of product IDs to product names (including Pakistani and international brand medicines), return the accurate Generic / Active Pharmaceutical Salt Formulation for each item (e.g. "Panadol 500mg" -> "Paracetamol", "Augmentin 625mg" -> "Amoxicillin + Clavulanic Acid", "Risek 40mg" -> "Omeprazole").
If an item is a general non-medical grocery/consumer item or you are not confident of its generic salt, set its value to null.
Respond ONLY with a valid JSON object matching this exact schema:
{
  "formulations": {
    "PRODUCT_ID": "Generic Salt Name or null"
  }
}
SYSTEM;

        $userPrompt = "Identify the generic formulation/salt for these products:\n"
            .json_encode($idToProductName, JSON_UNESCAPED_UNICODE);

        $decoded = $this->callAiJson($systemPrompt, $userPrompt);
        if (! is_array($decoded)) {
            return [];
        }

        $map = is_array($decoded['formulations'] ?? null) ? $decoded['formulations'] : $decoded;
        $result = [];
        foreach ($map as $k => $v) {
            if (is_numeric($k) && is_string($v) && trim($v) !== '') {
                $result[(int) $k] = trim($v);
            }
        }

        return $result;
    }

    /**
     * Calls Groq Primary -> Gemini Backup and returns the decoded JSON array.
     *
     * @return ?array<string, mixed>
     */
    protected function callAiJson(string $systemPrompt, string $userPrompt): ?array
    {
        $primary = (string) config('cube.primary_provider', 'groq');
        $backup = (string) config('cube.backup_provider', 'gemini');
        $providers = array_values(array_unique(array_filter([$primary, $backup])));

        foreach ($providers as $providerName) {
            if (! in_array($providerName, ['groq', 'gemini'], true)) {
                continue;
            }

            $inspection = $this->quotaGuard->inspectProvider($providerName);
            if (! $inspection['can_attempt']) {
                continue;
            }

            $this->quotaGuard->recordAttempt($providerName);

            $res = $providerName === 'groq'
                ? $this->groqProvider->generate($systemPrompt, $userPrompt)
                : $this->geminiProvider->generate($systemPrompt, $userPrompt);

            if (($res['ok'] ?? false) === true && isset($res['data']) && is_array($res['data'])) {
                $this->quotaGuard->recordSuccess($providerName);

                return $res['data'];
            }
        }

        return null;
    }

    /**
     * Infers the pharmaceutical salt / generic formulation from a medicine's name using
     * our built-in dictionary of common Pakistani & international brands and generic salts.
     */
    public function inferFormulationFromProductName(string $productName): ?string
    {
        $lower = mb_strtolower(trim($productName), 'UTF-8');
        if ($lower === '') {
            return null;
        }

        foreach (self::PHARMACY_SALT_DICTIONARY as $brandKey => $salt) {
            if (preg_match('/\b'.preg_quote($brandKey, '/').'\b/iu', $lower)) {
                return $salt;
            }
        }

        return null;
    }

    protected function inferCategoryFromProductName(string $productName): string
    {
        $lower = mb_strtolower($productName, 'UTF-8');
        if (preg_match('/\b(tab|tabs|tablet|tablets)\b/iu', $lower)) {
            return 'Tablets';
        }
        if (preg_match('/\b(cap|caps|capsule|capsules)\b/iu', $lower)) {
            return 'Capsules';
        }
        if (preg_match('/\b(syp|syrup|susp|suspension|liquid|linctus)\b/iu', $lower)) {
            return 'Syrups & Suspensions';
        }
        if (preg_match('/\b(inj|injection|vial|amp|ampoule|infusion|iv)\b/iu', $lower)) {
            return 'Injections';
        }
        if (preg_match('/\b(cream|ointment|gel|lotion)\b/iu', $lower)) {
            return 'Creams & Ointments';
        }
        if (preg_match('/\b(drop|drops|eye|ear|nasal)\b/iu', $lower)) {
            return 'Drops';
        }
        if (preg_match('/\b(sachet|powder|ors)\b/iu', $lower)) {
            return 'Sachets & Powders';
        }

        return 'General';
    }

    protected function resolveOrCreateCategory(string $name): Category
    {
        $clean = trim($name) ?: 'General';
        $existing = Category::query()->whereRaw('LOWER(name) = ?', [mb_strtolower($clean, 'UTF-8')])->first();
        if ($existing) {
            return $existing;
        }

        return Category::create(['name' => $clean]);
    }

    protected function resolveOrCreateBrand(string $name): Brand
    {
        $clean = trim($name) ?: 'General';
        $existing = Brand::query()->whereRaw('LOWER(name) = ?', [mb_strtolower($clean, 'UTF-8')])->first();
        if ($existing) {
            return $existing;
        }

        return Brand::create([
            'name' => $clean,
            'expiry_return_policy' => 'before_expiry',
            'expiry_return_months' => 3,
        ]);
    }

    protected function resolveOrCreateSupplier(string $name): Supplier
    {
        $clean = trim($name);
        if ($clean !== '' && strtolower($clean) !== 'general supplier') {
            $existing = Supplier::query()->whereRaw('LOWER(name) = ?', [mb_strtolower($clean, 'UTF-8')])->first();
            if ($existing) {
                return $existing;
            }

            return Supplier::create(['name' => $clean]);
        }

        $first = Supplier::query()->orderBy('id')->first();
        if ($first) {
            return $first;
        }

        return Supplier::create(['name' => 'General Supplier']);
    }

    protected function findProductByNameOrCode(string $target): ?Product
    {
        $clean = trim($target);
        if ($clean === '') {
            return null;
        }

        return Product::query()
            ->whereRaw('LOWER(product_code) = ?', [mb_strtolower($clean, 'UTF-8')])
            ->orWhere('barcode', $clean)
            ->orWhereRaw('LOWER(name) = ?', [mb_strtolower($clean, 'UTF-8')])
            ->first()
            ?? Product::query()
                ->where('name', 'like', $clean.'%')
                ->orderByRaw('LENGTH(name) ASC')
                ->first();
    }

    /**
     * @return array{product_code: string, barcode: string}
     */
    protected function generateUniqueCodeAndBarcode(): array
    {
        $lastProduct = Product::orderBy('id', 'desc')->first();
        if ($lastProduct && preg_match('/PRD-(\d+)/', (string) $lastProduct->product_code, $matches)) {
            $nextNum = ((int) $matches[1]) + 1;
        } else {
            $nextNum = Product::count() + 1;
        }

        do {
            $code = 'PRD-'.str_pad((string) $nextNum, 4, '0', STR_PAD_LEFT);
            $nextNum++;
        } while (Product::where('product_code', $code)->exists());

        do {
            $barcode = 'PRD'.str_pad((string) random_int(0, 9999999999), 10, '0', STR_PAD_LEFT);
        } while (Product::where('barcode', $barcode)->exists());

        return [
            'product_code' => $code,
            'barcode' => $barcode,
        ];
    }

    protected function extractOptionalFilterFromPrompt(string $text): string
    {
        if (preg_match('/\b(?:named|like|matching|starting\s+with)\s+([A-Za-z0-9\-]+)/iu', $text, $m)) {
            return trim($m[1]);
        }

        return '';
    }

    protected function canCreateProducts(User $user): bool
    {
        return $user->hasRole(['Admin', 'admin', 'super-admin']) || $user->can('product.create');
    }

    protected function canUpdateProducts(User $user): bool
    {
        return $user->hasRole(['Admin', 'admin', 'super-admin']) || $user->can('product.update');
    }

    protected function buildDeleteBlockedResponse(string $productVersion): array
    {
        return $this->formatTaskResponse(
            titleEn: '🔒 Safety Policy: Deleting Records Is Disabled in Cube',
            titleUr: '🔒 حفاظتی اصول: کیوب (Cube) میں ریکارڈ ڈیلیٹ کرنا ممنوع ہے',
            answerEn: "For your business data protection, **Cube is hardcoded to never delete any records** (products, formulations, categories, brands, invoices, or ledgers).\n\n"
                ."**What Cube CAN do for you automatically:**\n"
                ."1. **➕ Add Products:** e.g. *\"Add product Panadol 500mg, formulation Paracetamol, pack size 200, pack sale price 600\"*\n"
                ."2. **✏️ Edit Products:** e.g. *\"Update product Panadol pack sale price to 650 and rack to R-12\"*\n"
                ."3. **🧪 Auto-Fill Formulations in Background:** e.g. *\"Add formulations to all products missing formulation\"*",
            answerUr: "آپ کے کاروباری ڈیٹا کے تحفظ کے لیے **کیوب (Cube) میں کسی بھی قسم کا ریکارڈ ڈیلیٹ کرنا سختی سے بند (Hardcoded Block) ہے**۔\n\n"
                ."**کیوب آپ کی آسانی کے لیے یہ کام خودکار طور پر کر سکتا ہے:**\n"
                ."1. **➕ نئی پروڈکٹ شامل کرنا:** مثلاً *\"Add product Panadol 500mg, pack size 200, pack sale price 600\"*\n"
                ."2. **✏️ پروڈکٹ ایڈٹ کرنا:** مثلاً *\"Update product Panadol pack sale price to 650 and rack to R-12\"*\n"
                ."3. **🧪 بیک گراؤنڈ میں متعدد پروڈکٹس کی فارمولیشن لگانا:** مثلاً *\"Auto-fill missing formulations for my products\"*",
            links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
            taskMeta: [
                'action' => 'delete_blocked',
                'blocked' => true,
            ],
            productVersion: $productVersion,
            providerStatus: 'cube_task_delete_blocked'
        );
    }

    protected function buildPermissionDeniedResponse(string $permission, string $productVersion): array
    {
        return $this->formatTaskResponse(
            titleEn: 'Permission Required for This Task',
            titleUr: 'اس عمل کے لیے اجازت (Permission) درکار ہے',
            answerEn: "Your user account does not currently have the `{$permission}` permission required to perform this task automatically.",
            answerUr: "آپ کے یوزر اکاؤنٹ کو یہ ٹاسک خودکار طور پر کرنے کے لیے `{$permission}` کی اجازت حاصل نہیں ہے۔",
            links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
            taskMeta: [
                'action' => 'permission_denied',
                'required_permission' => $permission,
            ],
            productVersion: $productVersion,
            providerStatus: 'cube_task_permission_denied'
        );
    }

    protected function buildUnauthorizedResponse(string $productVersion): array
    {
        return $this->formatTaskResponse(
            titleEn: 'Authentication Required',
            titleUr: 'لاگ ان ہونا ضروری ہے',
            answerEn: 'Please log in to ask Cube to perform product or formulation tasks.',
            answerUr: 'یہ عمل کرنے کے لیے براہِ کرم لاگ ان کریں۔',
            links: [],
            taskMeta: ['action' => 'unauthorized'],
            productVersion: $productVersion,
            providerStatus: 'cube_task_unauthorized'
        );
    }

    /**
     * @param  array<int, array<string, string>>  $links
     * @param  array<string, mixed>  $taskMeta
     * @return array<string, mixed>
     */
    protected function formatTaskResponse(
        string $titleEn,
        string $titleUr,
        string $answerEn,
        string $answerUr,
        array $links,
        array $taskMeta,
        string $productVersion,
        string $providerStatus = 'cube_task_executed'
    ): array {
        return [
            'kind' => 'qa',
            'title_en' => $titleEn,
            'title_ur' => $titleUr,
            'answer_en' => $answerEn,
            'answer_ur' => $answerUr,
            'title' => [
                'en' => $titleEn,
                'ur' => $titleUr,
            ],
            'en' => $answerEn,
            'ur' => $answerUr,
            'source_ids' => ['task:'.($taskMeta['action'] ?? 'cube_automation')],
            'evidence_classification' => 'ai_grounded_docs',
            'evidence_badge' => [
                'en' => "⚡ Cube AI Task Automation • Non-Destructive (v{$productVersion})",
                'ur' => "⚡ کیوب اے آئی خودکار ٹاسک • محفوظ موڈ (v{$productVersion})",
            ],
            'provider' => 'cube_task_engine',
            'provider_status' => $providerStatus,
            'fallback_used' => false,
            'links' => $links,
            'workflow_id' => null,
            'task_meta' => $taskMeta,
            'background_task' => $taskMeta['background_task'] ?? null,
            'provider_trace' => [],
        ];
    }
}
