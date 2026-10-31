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
use Illuminate\Support\Facades\Cache;
use Illuminate\Support\Facades\Schema;
use Throwable;

class CubeTaskExecutorService
{
    /**
     * Mandatory fields required from the user before Cube will ever create a new product.
     * Cube will NEVER assume or default any of these values from itself.
     *
     * @var array<string, array{en: string, ur: string, hint_en: string, hint_ur: string}>
     */
    protected const REQUIRED_CREATE_PRODUCT_FIELDS = [
        'name' => [
            'en' => 'Product Name',
            'ur' => 'پروڈکٹ کا نام (Product Name)',
            'hint_en' => 'Full product name & strength, e.g., `Panadol 500mg Tablet`',
            'hint_ur' => 'پروڈکٹ کا مکمل نام، مثلاً `Panadol 500mg Tablet`',
        ],
        'pack_size' => [
            'en' => 'Pack Size',
            'ur' => 'پیک سائز (Pack Size)',
            'hint_en' => 'How many units are in 1 pack (integer ≥ 1, e.g., `100`, `30`, `10`, `1`)',
            'hint_ur' => 'ایک پیک میں کتنے یونٹس ہیں (مثلاً `100`، `30`، `10`)',
        ],
        'category' => [
            'en' => 'Category',
            'ur' => 'کیٹیگری (Category)',
            'hint_en' => 'Product category, e.g., `Tablets`, `Capsules`, `Syrups`',
            'hint_ur' => 'پروڈکٹ کی کیٹیگری، مثلاً `Tablets`, `Capsules`, `Syrups`',
        ],
        'brand' => [
            'en' => 'Brand / Manufacturer',
            'ur' => 'برانڈ / کمپنی (Brand)',
            'hint_en' => 'Brand or manufacturer name, e.g., `GSK`, `Abbott`, `Getz`',
            'hint_ur' => 'برانڈ یا کمپنی کا نام، مثلاً `GSK`, `Abbott`',
        ],
        'supplier' => [
            'en' => 'Supplier / Distributor',
            'ur' => 'سپلائر / ڈسٹری بیوٹر (Supplier)',
            'hint_en' => 'Primary supplier/distributor name, e.g., `Ali Gohar`, `Premier`',
            'hint_ur' => 'بنیادی سپلائر کا نام، مثلاً `Ali Gohar`, `Premier`',
        ],
    ];
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
     * a follow-up answer to a pending task with missing required fields, or a forbidden Delete command).
     *
     * Returns `null` if the message is a normal informational Q&A question.
     *
     * @param  array<int, array{role: string, content: string}>  $history
     * @return ?array<string, mixed>
     */
    public function detectAndExecuteFromChat(?User $user, string $question, string $language = 'en', string $productVersion = '2026.10', array $history = []): ?array
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

        // 3. Check if the user has an active pending task waiting for required fields
        $pendingTask = $user ? $this->getPendingTask($user) : null;
        if (is_array($pendingTask) && ! empty($pendingTask['action'])) {
            if ($this->isCancelCommand($lower)) {
                $this->clearPendingTask($user);

                return $this->buildTaskCancelledResponse($productVersion);
            }

            $explicitIntent = $this->classifyActionIntent($lower);

            if ($explicitIntent === 'bulk_autofill_missing_formulations') {
                $this->clearPendingTask($user);

                return $this->executeBulkAutofillMissingFormulations($user, [
                    'batch_size' => 20,
                    'after_id' => 0,
                    'filter_query' => $this->extractOptionalFilterFromPrompt($trimmed),
                ], $productVersion);
            }

            // If the user is replying with the missing fields (or continuing the same task type), merge & continue
            if ($explicitIntent === null || $explicitIntent === $pendingTask['action']) {
                return $this->continuePendingTask($user, $pendingTask, $trimmed, $productVersion);
            }

            // User explicitly started a different task action; clear old pending task and proceed
            $this->clearPendingTask($user);
        }

        // 4. Check if the message is a Bulk Formulation command, Add Product command, or Edit Product command
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
            return $this->executeBulkAssignFormulations($user, $parsedTask, $productVersion, $trimmed);
        }

        if ($action === 'create_products') {
            // Strip any unstated assumptions that AI might have guessed from world knowledge
            if (is_array($parsedTask['products'] ?? null)) {
                $parsedTask['products'] = array_map(
                    fn ($item) => is_array($item) ? $this->verifyExtractedFieldsAgainstUserText($item, $trimmed) : $item,
                    $parsedTask['products']
                );
            }

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
     * Detects if the user is cancelling an active pending task prompt.
     */
    protected function isCancelCommand(string $lower): bool
    {
        return (bool) preg_match(
            '/^(?:please\s+|cube\s+)?(?:cancel|cancel\s+task|stop|abort|never\s*mind|forget\s+it|skip|rahne\s+do|rehne\s+do|choro|منسوخ|کینسل|رہنے\s+دو)$/iu',
            trim($lower, " \t\n\r\0\x0B.!?,")
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
            preg_match('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:edit|update|change|modify|set)\s+(?:the\s+)?(?:product|medicine|item|price|pack\s+price|sale\s+price|purchase\s+price|pack\s+size|rack|max\s+discount|category|brand|supplier|formulation)\b/iu', $lower) ||
            preg_match('/\b(?:product|medicine)\s+(?:update|edit|change)\s+(?:kar\s+do|karein|کر\s+دیں)\b/iu', $lower)
        ) {
            return 'update_products';
        }

        return null;
    }

    /**
     * Uses Groq/Gemini AI (with deterministic local regex fallback) to extract structured
     * product creation, product update, or formulation assignment parameters.
     * NEVER assumes or invents any field value not explicitly stated by the user.
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
HARDCODED SAFETY RULE 1: Deleting anything is strictly forbidden. If the user asks to delete or remove anything, set "action": "delete_blocked".
HARDCODED SAFETY RULE 2 (ZERO ASSUMPTIONS): NEVER assume, guess, infer, or default any field value that the user did NOT explicitly state in their message!
- If the user did not explicitly state "pack_size", set "pack_size": null.
- If the user did not explicitly state "category", set "category": null.
- If the user did not explicitly state "brand", set "brand": null.
- If the user did not explicitly state "supplier", set "supplier": null.
- If the user did not explicitly state "formulation", set "formulation": null.
- If the user did not explicitly state prices or rack, set them to null.

Supported actions:
1. "create_products": Adding 1 or more new products.
   Schema:
   {
     "action": "create_products",
     "products": [
       {
         "name": null,
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
         "description": null
       }
     ]
   }

2. "update_products": Editing 1 or more existing products. Only include fields the user explicitly wants to change (leave others null).
   Schema:
   {
     "action": "update_products",
     "updates": [
       {
         "target_name_or_code": null,
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
         "product_name": null,
         "formulation": null
       }
     ],
     "standalone_formulations": []
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
     * Local deterministic parser fallback (never assumes any field not stated in `$rawText`).
     *
     * @return array<string, mixed>
     */
    protected function parseTaskLocally(string $rawText, string $defaultIntent): array
    {
        if ($defaultIntent === 'bulk_assign_formulations') {
            $assignments = [];
            $standalone = [];

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

            // Detect partial formulation assignment where product was given but formulation was omitted: "set formulation for Panadol"
            if (empty($assignments) && preg_match('/(?:set|assign|add|update|change)\s+(?:the\s+)?formulations?\s+(?:for|of|on)\s+([A-Za-z0-9\s\-\+]+)$/iu', trim($rawText, " \t\n\r\0\x0B.?"), $m)) {
                $prodCandidate = trim($m[1]);
                $prodCandidate = preg_replace('/^(?:product|products|medicine|medicines)\s+/i', '', $prodCandidate) ?? $prodCandidate;
                if ($prodCandidate !== '') {
                    $assignments[] = [
                        'product_name' => $prodCandidate,
                        'formulation' => null,
                    ];
                }
            }

            if (empty($assignments) && preg_match('/add\s+formulations?\s*:\s*(.+)$/iu', $rawText, $m)) {
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
            $body = preg_replace('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:add|create|register|insert|make)\s+(?:a\s+)?(?:new\s+)?(?:product|products|medicine|medicines|item|items)\b\s*(?:named|called|:)?\s*/iu', '', $rawText) ?? $rawText;
            $parsedItem = $this->extractProductFieldsFromText($body, true);

            $hasAnyValue = false;
            foreach ($parsedItem as $v) {
                if ($v !== null && $v !== '') {
                    $hasAnyValue = true;
                    break;
                }
            }

            return [
                'action' => 'create_products',
                'products' => $hasAnyValue ? [$parsedItem] : [],
            ];
        }

        if ($defaultIntent === 'update_products') {
            $body = preg_replace('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:edit|update|change|modify|set)\s+(?:the\s+)?(?:product|medicine|item)?\s*/iu', '', $rawText) ?? $rawText;
            $update = $this->extractUpdateFieldsFromText($body);

            return [
                'action' => 'update_products',
                'updates' => [$update],
            ];
        }

        return ['action' => $defaultIntent];
    }

    /**
     * Extracts explicitly stated product fields from natural-language text without assuming any defaults.
     *
     * @return array<string, mixed>
     */
    protected function extractProductFieldsFromText(string $text, bool $treatLeadingUnlabeledAsName = true): array
    {
        $item = [
            'name' => null,
            'formulation' => null,
            'pack_size' => null,
            'pack_purchase_price' => null,
            'pack_sale_price' => null,
            'unit_purchase_price' => null,
            'unit_sale_price' => null,
            'whole_sale_pack_price' => null,
            'whole_sale_unit_price' => null,
            'category' => null,
            'brand' => null,
            'supplier' => null,
            'rack' => null,
            'narcotic' => null,
            'max_discount' => null,
            'description' => null,
        ];

        $working = trim($text);
        if ($working === '') {
            return $item;
        }

        if (preg_match('/(?:product\s+name|name)\s*(?:is|to|=|:)\s*([A-Za-z0-9\s\-\+\(\)\.\/]+?)(?=,|;|\n|\bpack\s*size\b|\bpacking\b|\bpack\s*purchase\b|\bpurchase\s*price\b|\bcost\s*price\b|\bpack\s*sale\b|\bsale\s*price\b|\bretail\s*price\b|\bunit\s*purchase\b|\bunit\s*sale\b|\bformulation\b|\bsalt\b|\bgeneric\b|\bbrand\b|\bmanufacturer\b|\bcategory\b|\bsupplier\b|\bdistributor\b|\bvendor\b|\brack\b|\bnarcotic\b|\bmax\s*discount\b|$)/iu', $working, $m)) {
            $item['name'] = trim($m[1], " \t\n\r\0\x0B.,:-");
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:pack\s*size|packing)\s*(?:is|to|=|:)?\s*(\d+)/iu', $working, $m)) {
            $item['pack_size'] = max(1, (int) $m[1]);
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/unit\s*(?:purchase|cost|buy)\s*(?:price|rate|cost)?\s*(?:is|to|=|:)?\s*(\d+(?:\.\d+)?)/iu', $working, $m)) {
            $item['unit_purchase_price'] = (float) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:pack\s*)?(?:purchase|cost|buy)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*(\d+(?:\.\d+)?)/iu', $working, $m)) {
            $item['pack_purchase_price'] = (float) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:whole\s*sale|wholesale)\s*pack\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*(\d+(?:\.\d+)?)/iu', $working, $m)) {
            $item['whole_sale_pack_price'] = (float) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:whole\s*sale|wholesale)\s*unit\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*(\d+(?:\.\d+)?)/iu', $working, $m)) {
            $item['whole_sale_unit_price'] = (float) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/unit\s*(?:sale|sell|retail|mrp)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*(\d+(?:\.\d+)?)/iu', $working, $m)) {
            $item['unit_sale_price'] = (float) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:pack\s*)?(?:sale|sell|retail|mrp)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*(\d+(?:\.\d+)?)/iu', $working, $m)) {
            $item['pack_sale_price'] = (float) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:formulation|generic\s*salt|salt|generic)\s*(?:is|to|=|:)?\s*([A-Za-z0-9\s\+\-\/,\(\)]+?)(?=,|;|\n|\bpack\b|\bpacking\b|\bpurchase\b|\bsale\b|\bcost\b|\bprice\b|\bbrand\b|\bmanufacturer\b|\bcategory\b|\bsupplier\b|\bdistributor\b|\bvendor\b|\brack\b|\bnarcotic\b|\bmax\s*discount\b|$)/iu', $working, $m)) {
            $item['formulation'] = trim($m[1], " \t\n\r\0\x0B.,:-");
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:brand|manufacturer|company)\s*(?:is|to|=|:)?\s*([A-Za-z0-9\s\-\.&]+?)(?=,|;|\n|\bpack\b|\bpacking\b|\bpurchase\b|\bsale\b|\bcost\b|\bprice\b|\bcategory\b|\bformulation\b|\bsalt\b|\bsupplier\b|\bdistributor\b|\bvendor\b|\brack\b|\bnarcotic\b|\bmax\s*discount\b|$)/iu', $working, $m)) {
            $item['brand'] = trim($m[1], " \t\n\r\0\x0B.,:-");
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:category|cat)\s*(?:is|to|=|:)?\s*([A-Za-z0-9\s\-\&]+?)(?=,|;|\n|\bpack\b|\bpacking\b|\bpurchase\b|\bsale\b|\bcost\b|\bprice\b|\bbrand\b|\bmanufacturer\b|\bformulation\b|\bsalt\b|\bsupplier\b|\bdistributor\b|\bvendor\b|\brack\b|\bnarcotic\b|\bmax\s*discount\b|$)/iu', $working, $m)) {
            $item['category'] = trim($m[1], " \t\n\r\0\x0B.,:-");
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:supplier|distributor|vendor)\s*(?:is|to|=|:)?\s*([A-Za-z0-9\s\-\.&]+?)(?=,|;|\n|\bpack\b|\bpacking\b|\bpurchase\b|\bsale\b|\bcost\b|\bprice\b|\bbrand\b|\bmanufacturer\b|\bcategory\b|\bformulation\b|\bsalt\b|\brack\b|\bnarcotic\b|\bmax\s*discount\b|$)/iu', $working, $m)) {
            $item['supplier'] = trim($m[1], " \t\n\r\0\x0B.,:-");
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/(?:rack|shelf)\s*(?:is|to|=|:)?\s*([A-Za-z0-9\-\/]+)/iu', $working, $m)) {
            $item['rack'] = trim($m[1], " \t\n\r\0\x0B.,:-");
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/max\s*discount\s*(?:is|to|=|:)?\s*(\d+)/iu', $working, $m)) {
            $item['max_discount'] = (int) $m[1];
            $working = str_replace($m[0], ' , ', $working);
        }

        if (preg_match('/narcotic\s*(?:is|to|=|:)?\s*(yes|no)/iu', $working, $m)) {
            $item['narcotic'] = strtolower($m[1]);
            $working = str_replace($m[0], ' , ', $working);
        }

        if ($treatLeadingUnlabeledAsName && ($item['name'] === null || $item['name'] === '')) {
            $firstSegment = trim(explode(',', $working)[0] ?? '');
            $firstSegment = preg_replace('/\b(?:and|with|having)\s*$/iu', '', $firstSegment) ?? $firstSegment;
            $firstSegment = trim(preg_replace('/\s+/', ' ', $firstSegment) ?? $firstSegment, " \t\n\r\0\x0B.,:-");
            if ($firstSegment !== '') {
                $item['name'] = $firstSegment;
            }
        }

        return $item;
    }

    /**
     * Extracts explicitly stated update fields from text without assuming any values.
     *
     * @return array<string, mixed>
     */
    protected function extractUpdateFieldsFromText(string $body): array
    {
        $extracted = $this->extractProductFieldsFromText($body, false);

        $target = '';
        if (preg_match('/\b(PRD-\d+)\b/i', $body, $m)) {
            $target = strtoupper($m[1]);
        } else {
            // Strip recognized field clauses to isolate the product target name
            $stripped = preg_replace(
                '/\b(?:pack\s*size|packing|unit\s*purchase\s*price|pack\s*purchase\s*price|purchase\s*price|cost\s*price|unit\s*sale\s*price|pack\s*sale\s*price|sale\s*price|retail\s*price|whole\s*sale\s*pack\s*price|whole\s*sale\s*unit\s*price|formulation|generic\s*salt|salt|generic|brand|manufacturer|category|supplier|distributor|vendor|rack|shelf|max\s*discount|narcotic)\b.*$/iu',
                '',
                $body
            ) ?? $body;
            $stripped = preg_replace('/\b(for|of|on|to|and|set|change|update|edit|product|medicine|item)\b/iu', ' ', $stripped) ?? $stripped;
            $target = trim(preg_replace('/\s+/', ' ', $stripped) ?? '', " \t\n\r\0\x0B.,:-");
        }

        $update = [
            'target_name_or_code' => $target !== '' ? $target : null,
        ];

        foreach ($extracted as $k => $v) {
            if ($k === 'name') {
                continue;
            }
            if ($v !== null && $v !== '') {
                $update[$k] = $v;
            }
        }

        if (preg_match('/(?:rename\s+to|new\s+name\s*(?:is|to|=|:)?)\s*([A-Za-z0-9\s\-\+\(\)\.]+?)(?=,|;|\n|$)/iu', $body, $m)) {
            $update['new_name'] = trim($m[1], " \t\n\r\0\x0B.,:-");
        }

        return $update;
    }

    /**
     * Strictly verifies AI-extracted product fields against the actual text typed by the user.
     * Prevents any LLM from assuming or guessing `pack_size`, `category`, `brand`, `supplier`,
     * or `formulation` from world knowledge when the user did not state them.
     *
     * @param  array<string, mixed>  $item
     * @return array<string, mixed>
     */
    protected function verifyExtractedFieldsAgainstUserText(array $item, string $userText): array
    {
        $placeholders = ['null', 'n/a', 'na', 'none', 'unknown', 'default', 'not specified', 'unspecified', 'tbd', '?', '-'];
        $lowerText = mb_strtolower($userText, 'UTF-8');

        foreach (['name', 'formulation', 'category', 'brand', 'supplier', 'rack', 'description'] as $strKey) {
            if (! isset($item[$strKey])) {
                $item[$strKey] = null;
                continue;
            }
            $val = trim((string) $item[$strKey]);
            if ($val === '' || in_array(mb_strtolower($val, 'UTF-8'), $placeholders, true)) {
                $item[$strKey] = null;
            } else {
                $item[$strKey] = $val;
            }
        }

        // Verify `brand`: must appear in the user's text
        if ($item['brand'] !== null) {
            $brandLower = mb_strtolower($item['brand'], 'UTF-8');
            if (! str_contains($lowerText, $brandLower)) {
                $item['brand'] = null;
            }
        }

        // Verify `supplier`: must appear in the user's text
        if ($item['supplier'] !== null) {
            $supLower = mb_strtolower($item['supplier'], 'UTF-8');
            if (! str_contains($lowerText, $supLower)) {
                $item['supplier'] = null;
            }
        }

        // Verify `formulation`: when creating a product, formulation must be stated by the user, not guessed
        if ($item['formulation'] !== null) {
            $formLower = mb_strtolower($item['formulation'], 'UTF-8');
            $productNameLower = mb_strtolower((string) ($item['name'] ?? ''), 'UTF-8');
            $hasFormulationKeyword = (bool) preg_match('/\b(formulation|salt|generic)\b/iu', $lowerText);
            if (! str_contains($lowerText, $formLower) || (! $hasFormulationKeyword && str_contains($productNameLower, $formLower))) {
                $item['formulation'] = null;
            }
        }

        // Verify `category`: must be explicitly mentioned by the user (not inferred from "Tablet" inside the product name)
        if ($item['category'] !== null) {
            $catLower = mb_strtolower($item['category'], 'UTF-8');
            $hasCategoryKeyword = (bool) preg_match('/\b(category|cat|کیٹیگری)\b/iu', $lowerText);
            if (! str_contains($lowerText, $catLower)) {
                $item['category'] = null;
            } elseif (! $hasCategoryKeyword && $item['name'] !== null) {
                $textWithoutName = str_replace(mb_strtolower($item['name'], 'UTF-8'), ' ', $lowerText);
                if (! str_contains($textWithoutName, $catLower)) {
                    $item['category'] = null;
                }
            }
        }

        // Verify `pack_size`: must be an explicit integer in user text outside dosage strength (e.g. 500mg, 120ml)
        if (isset($item['pack_size']) && $item['pack_size'] !== null && $item['pack_size'] !== '') {
            $packVal = (int) $item['pack_size'];
            $textWithoutStrengths = preg_replace('/\b\d+(?:\.\d+)?\s*(?:mg|ml|mcg|g|kg|iu|%)\b/iu', ' ', $lowerText) ?? $lowerText;
            if ($packVal < 1 || ! preg_match('/\b'.preg_quote((string) $packVal, '/').'\b/', $textWithoutStrengths)) {
                $item['pack_size'] = null;
            } else {
                $item['pack_size'] = $packVal;
            }
        } else {
            $item['pack_size'] = null;
        }

        return $item;
    }

    /**
     * Returns the list of required field keys (`name`, `pack_size`, `category`, `brand`, `supplier`)
     * that are still missing for a product draft.
     *
     * @param  array<string, mixed>  $rawItem
     * @return array<int, string>
     */
    protected function getMissingCreateProductFields(array $rawItem): array
    {
        $missing = [];

        if (trim((string) ($rawItem['name'] ?? '')) === '') {
            $missing[] = 'name';
        }

        $packSize = $rawItem['pack_size'] ?? null;
        if ($packSize === null || $packSize === '' || ! is_numeric($packSize) || (int) $packSize < 1) {
            $missing[] = 'pack_size';
        }

        if (trim((string) ($rawItem['category'] ?? '')) === '') {
            $missing[] = 'category';
        }

        if (trim((string) ($rawItem['brand'] ?? '')) === '') {
            $missing[] = 'brand';
        }

        if (trim((string) ($rawItem['supplier'] ?? '')) === '') {
            $missing[] = 'supplier';
        }

        return $missing;
    }

    /**
     * Continues an active pending task when the user replies with missing required fields.
     *
     * @param  array<string, mixed>  $pendingTask
     * @return array<string, mixed>
     */
    protected function continuePendingTask(User $user, array $pendingTask, string $replyText, string $productVersion): array
    {
        $action = (string) ($pendingTask['action'] ?? '');

        if ($action === 'create_products') {
            $drafts = is_array($pendingTask['products'] ?? null) && ! empty($pendingTask['products'])
                ? $pendingTask['products']
                : [[]];

            // Target the first incomplete product draft in the pending queue
            $targetIdx = 0;
            foreach ($drafts as $idx => $d) {
                if (! empty($this->getMissingCreateProductFields(is_array($d) ? $d : []))) {
                    $targetIdx = $idx;
                    break;
                }
            }

            $currentDraft = is_array($drafts[$targetIdx] ?? null) ? $drafts[$targetIdx] : [];
            $missingBefore = $this->getMissingCreateProductFields($currentDraft);

            $mergedDraft = $this->mergeReplyIntoProductDraft($currentDraft, $missingBefore, $replyText);
            $drafts[$targetIdx] = $mergedDraft;

            return $this->executeCreateProducts($user, ['products' => $drafts], $productVersion);
        }

        if ($action === 'update_products') {
            $updates = is_array($pendingTask['updates'] ?? null) && ! empty($pendingTask['updates'])
                ? $pendingTask['updates']
                : [[]];
            $currentUpdate = is_array($updates[0] ?? null) ? $updates[0] : [];

            $newExtracted = $this->extractUpdateFieldsFromText($replyText);
            foreach ($newExtracted as $k => $v) {
                if ($v !== null && $v !== '') {
                    if ($k === 'target_name_or_code' && ! empty($currentUpdate['target_name_or_code'])) {
                        continue;
                    }
                    $currentUpdate[$k] = $v;
                }
            }

            // If target was missing and user just typed the product name
            if (empty($currentUpdate['target_name_or_code'])) {
                $cleanReply = trim($replyText, " \t\n\r\0\x0B.,:-");
                if ($cleanReply !== '') {
                    $currentUpdate['target_name_or_code'] = $cleanReply;
                }
            }

            $updates[0] = $currentUpdate;

            return $this->executeUpdateProducts($user, ['updates' => $updates], $productVersion);
        }

        if ($action === 'bulk_assign_formulations') {
            $assignments = is_array($pendingTask['assignments'] ?? null) && ! empty($pendingTask['assignments'])
                ? $pendingTask['assignments']
                : [['product_name' => null, 'formulation' => null]];
            $currentAssign = is_array($assignments[0] ?? null) ? $assignments[0] : ['product_name' => null, 'formulation' => null];

            $parsedFollowUp = $this->parseTaskLocally($replyText, 'bulk_assign_formulations');
            if (! empty($parsedFollowUp['assignments'][0])) {
                $fa = $parsedFollowUp['assignments'][0];
                if (! empty($fa['product_name'])) {
                    $currentAssign['product_name'] = $fa['product_name'];
                }
                if (! empty($fa['formulation'])) {
                    $currentAssign['formulation'] = $fa['formulation'];
                }
            } else {
                $cleanVal = trim($replyText, " \t\n\r\0\x0B.,:-");
                if (empty($currentAssign['product_name']) && ! empty($currentAssign['formulation'])) {
                    $currentAssign['product_name'] = $cleanVal;
                } elseif (! empty($currentAssign['product_name']) && empty($currentAssign['formulation'])) {
                    $currentAssign['formulation'] = $cleanVal;
                }
            }

            $assignments[0] = $currentAssign;

            return $this->executeBulkAssignFormulations($user, [
                'assignments' => $assignments,
                'standalone_formulations' => [],
            ], $productVersion, $replyText);
        }

        $this->clearPendingTask($user);

        return $this->buildTaskCancelledResponse($productVersion);
    }

    /**
     * Merges a user's follow-up reply (whether labeled, natural sentence, comma-separated, or single bare value)
     * into an existing product draft without assuming any unstated fields.
     *
     * @param  array<string, mixed>  $draft
     * @param  array<int, string>  $missingBefore
     * @return array<string, mixed>
     */
    protected function mergeReplyIntoProductDraft(array $draft, array $missingBefore, string $replyText): array
    {
        $cleanReply = preg_replace('/^(?:please\s+|cube\s+|plz\s+|can\s+you\s+)?(?:add|create|register|insert|make)\s+(?:a\s+)?(?:new\s+)?(?:product|products|medicine|medicines|item|items)\b\s*(?:named|called|:)?\s*/iu', '', $replyText) ?? $replyText;

        // 1. Extract explicitly labeled fields first
        $labeled = $this->extractProductFieldsFromText($cleanReply, false);
        foreach ($labeled as $key => $val) {
            if ($val !== null && $val !== '') {
                $draft[$key] = $val;
            }
        }

        // 2. Determine which required fields are still missing after labeled extraction
        $stillMissing = $this->getMissingCreateProductFields($draft);
        if (empty($stillMissing)) {
            return $draft;
        }

        // 3. Remove labeled clauses from `$cleanReply` to see if the user provided unlabeled values (e.g. "200, Tablets, GSK, Ali Gohar" or "200")
        $remainingText = $cleanReply;
        $labeledPatterns = [
            '/(?:product\s+name|name)\s*(?:is|to|=|:)\s*[A-Za-z0-9\s\-\+\(\)\.\/]+?(?=,|;|\n|\bpack\b|\bcategory\b|\bbrand\b|\bsupplier\b|$)/iu',
            '/(?:pack\s*size|packing)\s*(?:is|to|=|:)?\s*\d+/iu',
            '/unit\s*(?:purchase|cost|buy)\s*(?:price|rate|cost)?\s*(?:is|to|=|:)?\s*\d+(?:\.\d+)?/iu',
            '/(?:pack\s*)?(?:purchase|cost|buy)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*\d+(?:\.\d+)?/iu',
            '/(?:whole\s*sale|wholesale)\s*(?:pack|unit)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*\d+(?:\.\d+)?/iu',
            '/unit\s*(?:sale|sell|retail|mrp)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*\d+(?:\.\d+)?/iu',
            '/(?:pack\s*)?(?:sale|sell|retail|mrp)\s*(?:price|rate)?\s*(?:is|to|=|:)?\s*\d+(?:\.\d+)?/iu',
            '/(?:formulation|generic\s*salt|salt|generic)\s*(?:is|to|=|:)?\s*[A-Za-z0-9\s\+\-\/,\(\)]+?(?=,|;|\n|\bpack\b|\bbrand\b|\bcategory\b|\bsupplier\b|\brack\b|$)/iu',
            '/(?:brand|manufacturer|company)\s*(?:is|to|=|:)?\s*[A-Za-z0-9\s\-\.&]+?(?=,|;|\n|\bpack\b|\bcategory\b|\bformulation\b|\bsupplier\b|\brack\b|$)/iu',
            '/(?:category|cat)\s*(?:is|to|=|:)?\s*[A-Za-z0-9\s\-\&]+?(?=,|;|\n|\bpack\b|\bbrand\b|\bformulation\b|\bsupplier\b|\brack\b|$)/iu',
            '/(?:supplier|distributor|vendor)\s*(?:is|to|=|:)?\s*[A-Za-z0-9\s\-\.&]+?(?=,|;|\n|\bpack\b|\bbrand\b|\bcategory\b|\bformulation\b|\brack\b|$)/iu',
            '/(?:rack|shelf)\s*(?:is|to|=|:)?\s*[A-Za-z0-9\-\/]+/iu',
            '/max\s*discount\s*(?:is|to|=|:)?\s*\d+/iu',
            '/narcotic\s*(?:is|to|=|:)?\s*(?:yes|no)/iu',
        ];
        foreach ($labeledPatterns as $pat) {
            $remainingText = preg_replace($pat, ' , ', $remainingText) ?? $remainingText;
        }

        $unlabeledTokens = array_values(array_filter(array_map(
            fn ($t) => trim(preg_replace('/^(?:and|is|=|:)\s+/iu', '', trim($t)) ?? '', " \t\n\r\0\x0B.,:-"),
            preg_split('/[,;\n]+/', $remainingText) ?: []
        ), fn ($t) => $t !== ''));

        foreach ($unlabeledTokens as $token) {
            $stillMissing = $this->getMissingCreateProductFields($draft);
            if (empty($stillMissing)) {
                break;
            }

            // If token is a pure positive integer (e.g. "200") and `pack_size` is among the missing fields
            if (preg_match('/^(\d+)(?:\s*(?:units?|tabs?|tablets?|caps?|packs?|pcs?))?$/iu', $token, $numMatch) && in_array('pack_size', $stillMissing, true)) {
                $draft['pack_size'] = max(1, (int) $numMatch[1]);
                continue;
            }

            // Assign to the next non-numeric missing field (`name`, `category`, `brand`, `supplier`)
            foreach ($stillMissing as $missingField) {
                if ($missingField === 'pack_size') {
                    continue;
                }
                if (trim((string) ($draft[$missingField] ?? '')) === '') {
                    // Don't overwrite `name` if token is identical to already set `name`
                    if ($missingField !== 'name' && isset($draft['name']) && mb_strtolower($token, 'UTF-8') === mb_strtolower((string) $draft['name'], 'UTF-8')) {
                        break;
                    }
                    $draft[$missingField] = $token;
                    break;
                }
            }
        }

        return $draft;
    }

    /**
     * Executes creation of 1 or multiple products (non-destructive).
     * NEVER assumes missing required fields (`name`, `pack_size`, `category`, `brand`, `supplier`).
     * If any required field is missing, stores the draft and asks the user for the missing fields.
     *
     * @param  array<string, mixed>  $taskData
     * @return array<string, mixed>
     */
    protected function executeCreateProducts(User $user, array $taskData, string $productVersion): array
    {
        if (! $this->canCreateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.create', $productVersion);
        }

        $rawItems = is_array($taskData['products'] ?? null) ? array_values(array_filter($taskData['products'], 'is_array')) : [];

        // If user asked to "Add a product" without providing any fields yet, start an empty draft and ask for all required fields
        if (empty($rawItems)) {
            $emptyDraft = [
                [
                    'name' => null,
                    'pack_size' => null,
                    'category' => null,
                    'brand' => null,
                    'supplier' => null,
                ],
            ];
            $this->savePendingTask($user, [
                'action' => 'create_products',
                'products' => $emptyDraft,
            ]);

            return $this->buildMissingCreateProductFieldsResponse($emptyDraft, $productVersion);
        }

        // Check if any product in the request is missing any required field (`name`, `pack_size`, `category`, `brand`, `supplier`)
        $hasMissingRequiredFields = false;
        foreach ($rawItems as $candidate) {
            $candidateName = trim((string) ($candidate['name'] ?? ''));
            if ($candidateName !== '') {
                $existingProd = Product::query()->whereRaw('LOWER(name) = ?', [mb_strtolower($candidateName, 'UTF-8')])->first();
                if ($existingProd) {
                    continue;
                }
            }
            if (! empty($this->getMissingCreateProductFields($candidate))) {
                $hasMissingRequiredFields = true;
                break;
            }
        }

        if ($hasMissingRequiredFields) {
            $this->savePendingTask($user, [
                'action' => 'create_products',
                'products' => $rawItems,
            ]);

            return $this->buildMissingCreateProductFieldsResponse($rawItems, $productVersion);
        }

        // All required fields are present! Clear any pending task draft and create the product(s).
        $this->clearPendingTask($user);

        $created = [];
        $alreadyExisted = [];

        foreach (array_slice($rawItems, 0, 25) as $rawItem) {
            $name = trim((string) ($rawItem['name'] ?? ''));
            if ($name === '') {
                continue;
            }

            // Check if product already exists by case-insensitive name
            $existing = Product::query()->whereRaw('LOWER(name) = ?', [mb_strtolower($name, 'UTF-8')])->first();
            if ($existing) {
                $explicitForm = trim((string) ($rawItem['formulation'] ?? ''));
                if (empty($existing->formulation) && $explicitForm !== '') {
                    $resolvedF = Formulation::resolveOrCreate($explicitForm);
                    $existing->formulation = $resolvedF?->name ?? $explicitForm;
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
            $packSize = (int) $rawItem['pack_size'];

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

            // Never assume formulation when adding a product unless explicitly provided by the user
            $rawFormulation = trim((string) ($rawItem['formulation'] ?? ''));
            $resolvedFormulation = $rawFormulation !== '' ? Formulation::resolveOrCreate($rawFormulation) : null;

            $categoryName = trim((string) $rawItem['category']);
            $category = $this->resolveOrCreateCategory($categoryName);

            $brandName = trim((string) $rawItem['brand']);
            $brand = $this->resolveOrCreateBrand($brandName);

            $supplierName = trim((string) $rawItem['supplier']);
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
                'supplier' => $supplier->name,
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
                $linesEn[] = "{$num}. **{$c['name']}** (`{$c['product_code']}`){$formStr} • Pack Size: `{$c['pack_size']}` • Category: `{$c['category']}` • Brand: `{$c['brand']}` • Supplier: `{$c['supplier']}` • Pack Sale: `{$c['pack_sale_price']}`";
                $linesUr[] = "{$num}. **{$c['name']}** (`{$c['product_code']}`){$formStr} • پیک سائز: `{$c['pack_size']}` • کیٹیگری: `{$c['category']}` • برانڈ: `{$c['brand']}` • سپلائر: `{$c['supplier']}`";
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
     * Builds an interactive follow-up response asking the user for the exact required fields
     * needed to complete adding the product(s), without assuming any values.
     *
     * @param  array<int, array<string, mixed>>  $draftItems
     * @return array<string, mixed>
     */
    protected function buildMissingCreateProductFieldsResponse(array $draftItems, string $productVersion): array
    {
        $firstDraft = is_array($draftItems[0] ?? null) ? $draftItems[0] : [];
        $missingFields = $this->getMissingCreateProductFields($firstDraft);

        $collectedEn = [];
        $collectedUr = [];
        $fieldLabels = [
            'name' => ['en' => 'Product Name', 'ur' => 'پروڈکٹ کا نام'],
            'pack_size' => ['en' => 'Pack Size', 'ur' => 'پیک سائز'],
            'category' => ['en' => 'Category', 'ur' => 'کیٹیگری'],
            'brand' => ['en' => 'Brand', 'ur' => 'برانڈ'],
            'supplier' => ['en' => 'Supplier', 'ur' => 'سپلائر'],
            'formulation' => ['en' => 'Formulation', 'ur' => 'فارمولیشن'],
            'pack_purchase_price' => ['en' => 'Pack Purchase Price', 'ur' => 'پیک قیمت خرید'],
            'pack_sale_price' => ['en' => 'Pack Sale Price', 'ur' => 'پیک قیمت فروخت'],
            'unit_purchase_price' => ['en' => 'Unit Purchase Price', 'ur' => 'یونٹ قیمت خرید'],
            'unit_sale_price' => ['en' => 'Unit Sale Price', 'ur' => 'یونٹ قیمت فروخت'],
            'rack' => ['en' => 'Rack', 'ur' => 'ریک'],
        ];

        $collectedSummary = [];
        foreach ($fieldLabels as $key => $labels) {
            $val = $firstDraft[$key] ?? null;
            if ($val !== null && trim((string) $val) !== '') {
                $collectedSummary[$key] = $val;
                $collectedEn[] = "- **{$labels['en']}:** `{$val}`";
                $collectedUr[] = "- **{$labels['ur']}:** `{$val}`";
            }
        }

        // Fetch up to 3 existing catalog examples so the user knows what already exists in their store
        $existingCategories = Schema::hasTable('categories')
            ? Category::query()->orderBy('name')->limit(4)->pluck('name')->all()
            : [];
        $existingBrands = Schema::hasTable('brands')
            ? Brand::query()->orderBy('name')->limit(4)->pluck('name')->all()
            : [];
        $existingSuppliers = Schema::hasTable('suppliers')
            ? Supplier::query()->orderBy('name')->limit(4)->pluck('name')->all()
            : [];

        $questionsEn = [];
        $questionsUr = [];
        $exampleParts = [];

        foreach ($missingFields as $idx => $fieldKey) {
            $num = $idx + 1;
            $meta = self::REQUIRED_CREATE_PRODUCT_FIELDS[$fieldKey] ?? [
                'en' => ucfirst($fieldKey),
                'ur' => $fieldKey,
                'hint_en' => '',
                'hint_ur' => '',
            ];

            $catalogHintEn = '';
            $catalogHintUr = '';
            if ($fieldKey === 'category' && ! empty($existingCategories)) {
                $list = implode(', ', array_map(fn ($c) => "`{$c}`", $existingCategories));
                $catalogHintEn = " — Existing in catalog: {$list} (or type a new one)";
                $catalogHintUr = " — موجودہ کیٹیگریز: {$list}";
            } elseif ($fieldKey === 'brand' && ! empty($existingBrands)) {
                $list = implode(', ', array_map(fn ($b) => "`{$b}`", $existingBrands));
                $catalogHintEn = " — Existing in catalog: {$list} (or type a new one)";
                $catalogHintUr = " — موجودہ برانڈز: {$list}";
            } elseif ($fieldKey === 'supplier' && ! empty($existingSuppliers)) {
                $list = implode(', ', array_map(fn ($s) => "`{$s}`", $existingSuppliers));
                $catalogHintEn = " — Existing in catalog: {$list} (or type a new one)";
                $catalogHintUr = " — موجودہ سپلائرز: {$list}";
            }

            $questionsEn[] = "{$num}. **{$meta['en']}** — {$meta['hint_en']}{$catalogHintEn}";
            $questionsUr[] = "{$num}. **{$meta['ur']}** — {$meta['hint_ur']}{$catalogHintUr}";

            $exampleParts[] = match ($fieldKey) {
                'name' => 'name Panadol 500mg Tablet',
                'pack_size' => 'pack size 200',
                'category' => 'category '.($existingCategories[0] ?? 'Tablets'),
                'brand' => 'brand '.($existingBrands[0] ?? 'GSK'),
                'supplier' => 'supplier '.($existingSuppliers[0] ?? 'Ali Gohar'),
                default => "{$fieldKey} value",
            };
        }

        $productHeadingEn = ! empty($firstDraft['name']) ? "**{$firstDraft['name']}**" : 'this new product';
        $productHeadingUr = ! empty($firstDraft['name']) ? "**{$firstDraft['name']}**" : 'نئی پروڈکٹ';

        $linesEn = [
            "To make sure your catalog stays accurate, **Cube never assumes missing required fields**. Please provide the remaining required details for {$productHeadingEn}:",
        ];
        $linesUr = [
            "آپ کے کیٹلاگ کی درستگی کے لیے **کیوب خود سے کوئی بھی مطلوبہ فیلڈ فرض (Assume) نہیں کرتا**۔ براہِ کرم {$productHeadingUr} مکمّل کرنے کے لیے درج ذیل معلومات بتائیں:",
        ];

        if (! empty($collectedEn)) {
            $linesEn[] = "✅ **Details Already Recorded:**\n".implode("\n", $collectedEn);
            $linesUr[] = "✅ **موصول شدہ معلومات:**\n".implode("\n", $collectedUr);
        }

        $linesEn[] = "❓ **Required Details Needed to Complete the Task:**\n".implode("\n", $questionsEn);
        $linesUr[] = "❓ **مطلوبہ معلومات (براہِ کرم جواب میں لکھیں):**\n".implode("\n", $questionsUr);

        $linesEn[] = "*(Optional: You can also include `formulation`, `pack purchase price`, `pack sale price`, or `rack`.)*\n\n"
            ."**Reply example:**\n`".implode(', ', $exampleParts)."`\n*(Or type `cancel` to abort this task.)*";
        $linesUr[] = "*(اختیاری: آپ ساتھ میں `formulation`، `pack purchase price`، `pack sale price` یا `rack` بھی لکھ سکتے ہیں۔)*\n\n"
            ."**جواب لکھنے کی مثال:**\n`".implode(', ', $exampleParts)."`\n*(یا منسوخ کرنے کے لیے `cancel` لکھیں۔)*";

        return $this->formatTaskResponse(
            titleEn: 'Required Product Details Needed (No Assumptions)',
            titleUr: 'پروڈکٹ شامل کرنے کے لیے مطلوبہ معلومات درکار ہیں',
            answerEn: implode("\n\n", $linesEn),
            answerUr: implode("\n\n", $linesUr),
            links: [['path' => '/products/create', 'en' => 'Open Add Product Form', 'ur' => 'پروڈکٹ فارم کھولیں']],
            taskMeta: [
                'action' => 'create_products',
                'status' => 'needs_required_fields',
                'created_count' => 0,
                'missing_fields' => $missingFields,
                'collected_fields' => $collectedSummary,
            ],
            productVersion: $productVersion,
            providerStatus: 'cube_task_needs_input'
        );
    }

    /**
     * Executes non-destructive updates on 1 or multiple existing products.
     * Never assumes which product or which field to edit; asks the user if either is missing.
     *
     * @param  array<string, mixed>  $taskData
     * @return array<string, mixed>
     */
    protected function executeUpdateProducts(User $user, array $taskData, string $productVersion): array
    {
        if (! $this->canUpdateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.update', $productVersion);
        }

        $updates = is_array($taskData['updates'] ?? null) ? array_values(array_filter($taskData['updates'], 'is_array')) : [];
        if (empty($updates)) {
            $emptyUpdate = [['target_name_or_code' => null]];
            $this->savePendingTask($user, [
                'action' => 'update_products',
                'updates' => $emptyUpdate,
            ]);

            return $this->buildMissingUpdateProductFieldsResponse($emptyUpdate[0], $productVersion);
        }

        $editableKeys = [
            'new_name', 'formulation', 'pack_size',
            'pack_purchase_price', 'pack_sale_price', 'unit_purchase_price', 'unit_sale_price',
            'whole_sale_pack_price', 'whole_sale_unit_price',
            'category', 'brand', 'supplier', 'rack', 'narcotic',
            'max_discount', 'expiry_warning_days', 'description',
        ];

        foreach ($updates as $uItem) {
            $target = trim((string) ($uItem['target_name_or_code'] ?? ($uItem['name'] ?? '')));
            $hasFieldToChange = false;
            foreach ($editableKeys as $ek) {
                if (isset($uItem[$ek]) && $uItem[$ek] !== null && trim((string) $uItem[$ek]) !== '') {
                    $hasFieldToChange = true;
                    break;
                }
            }

            if ($target === '' || ! $hasFieldToChange) {
                $this->savePendingTask($user, [
                    'action' => 'update_products',
                    'updates' => $updates,
                ]);

                return $this->buildMissingUpdateProductFieldsResponse($uItem, $productVersion);
            }
        }

        $this->clearPendingTask($user);

        $updatedRecords = [];
        $notFoundTargets = [];

        foreach (array_slice($updates, 0, 25) as $item) {
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
     * Builds a follow-up question when the user asks to edit a product without specifying
     * which product or which field/value to change.
     *
     * @param  array<string, mixed>  $updateDraft
     * @return array<string, mixed>
     */
    protected function buildMissingUpdateProductFieldsResponse(array $updateDraft, string $productVersion): array
    {
        $target = trim((string) ($updateDraft['target_name_or_code'] ?? ($updateDraft['name'] ?? '')));
        $missingFields = [];
        if ($target === '') {
            $missingFields[] = 'target_name_or_code';
        }
        $missingFields[] = 'fields_to_update';

        if ($target !== '') {
            $answerEn = "To update **{$target}** without making any assumptions, please tell me **which field(s) and new value(s)** you want to change:\n\n"
                ."- **Pack Sale Price / Pack Purchase Price** (e.g., `pack sale price 650, pack purchase price 540`)\n"
                ."- **Pack Size** (e.g., `pack size 200`)\n"
                ."- **Formulation / Salt** (e.g., `formulation Paracetamol`)\n"
                ."- **Category / Brand / Supplier** (e.g., `category Tablets, brand GSK, supplier Ali Gohar`)\n"
                ."- **Rack** (e.g., `rack R-12`)\n\n"
                ."*(Reply with the changes, or type `cancel` to abort.)*";
            $answerUr = "**{$target}** کو اپڈیٹ کرنے کے لیے براہِ کرم بتائیں کہ آپ **کون سی فیلڈ اور نئی ویلیو** تبدیل کرنا چاہتے ہیں:\n\n"
                ."- **Pack Sale Price / Pack Purchase Price** (مثلاً `pack sale price 650`)\n"
                ."- **Pack Size** (مثلاً `pack size 200`)\n"
                ."- **Formulation / Brand / Category / Supplier / Rack**\n\n"
                ."*(جواب میں تبدیلی لکھیں یا منسوخ کرنے کے لیے `cancel` لکھیں۔)*";
        } else {
            $answerEn = "Please specify:\n1. **Which Product** (Product Name or `PRD-XXXX` code) you want to edit\n2. **Which Field(s) & New Value(s)** you want to change\n\n**Example:**\n`Update product Panadol 500mg Tablet pack sale price to 650 and rack to R-12`";
            $answerUr = "براہِ کرم بتائیں:\n1. **کون سی پروڈکٹ** ایڈٹ کرنی ہے (پروڈکٹ کا نام یا کوڈ)\n2. **کون سی فیلڈ اور نئی ویلیو** تبدیل کرنی ہے\n\n**مثال:**\n`Update product Panadol 500mg Tablet pack sale price to 650 and rack to R-12`";
        }

        return $this->formatTaskResponse(
            titleEn: 'Details Needed to Update Product',
            titleUr: 'پروڈکٹ اپڈیٹ کرنے کے لیے معلومات درکار ہیں',
            answerEn: $answerEn,
            answerUr: $answerUr,
            links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
            taskMeta: [
                'action' => 'update_products',
                'status' => 'needs_required_fields',
                'updated_count' => 0,
                'missing_fields' => $missingFields,
                'collected_fields' => $target !== '' ? ['target_name_or_code' => $target] : [],
            ],
            productVersion: $productVersion,
            providerStatus: 'cube_task_needs_input'
        );
    }

    /**
     * Executes explicit bulk formulation assignments to named products or registers standalone formulations.
     * Never assumes missing formulation names or product names; asks the user if required details are missing.
     *
     * @param  array<string, mixed>  $taskData
     * @return array<string, mixed>
     */
    protected function executeBulkAssignFormulations(User $user, array $taskData, string $productVersion, string $rawText = ''): array
    {
        if (! $this->canUpdateProducts($user) && ! $this->canCreateProducts($user)) {
            return $this->buildPermissionDeniedResponse('product.update', $productVersion);
        }

        $assignments = is_array($taskData['assignments'] ?? null) ? array_values(array_filter($taskData['assignments'], 'is_array')) : [];
        $standalone = is_array($taskData['standalone_formulations'] ?? null) ? array_values(array_filter($taskData['standalone_formulations'], fn ($f) => is_string($f) && trim($f) !== '')) : [];

        // Check if any assignment is missing product_name or formulation, or if both arrays are empty
        $missingAssignmentDetails = empty($assignments) && empty($standalone);
        foreach ($assignments as $aItem) {
            if (trim((string) ($aItem['product_name'] ?? '')) === '' || trim((string) ($aItem['formulation'] ?? '')) === '') {
                $missingAssignmentDetails = true;
                break;
            }
        }

        if ($missingAssignmentDetails) {
            $firstAssign = $assignments[0] ?? ['product_name' => null, 'formulation' => null];
            $this->savePendingTask($user, [
                'action' => 'bulk_assign_formulations',
                'assignments' => [$firstAssign],
            ]);

            $prodName = trim((string) ($firstAssign['product_name'] ?? ''));
            $formName = trim((string) ($firstAssign['formulation'] ?? ''));
            $missingList = [];
            if ($prodName === '') {
                $missingList[] = 'product_name';
            }
            if ($formName === '') {
                $missingList[] = 'formulation';
            }

            $answerEn = $prodName !== '' && $formName === ''
                ? "I won't assume the formulation. What **Formulation (Generic Salt)** would you like to assign to **{$prodName}**?\n\n**Reply example:** `Paracetamol`"
                : ($formName !== '' && $prodName === ''
                    ? "Which **Product Name** would you like to assign the formulation **{$formName}** to?"
                    : "To assign a formulation without assuming any values, please provide:\n1. **Product Name**\n2. **Formulation (Generic Salt)**\n\n**Example:** `Set formulation Paracetamol for Panadol 500mg Tablet`");

            $answerUr = $prodName !== '' && $formName === ''
                ? "کیوب خود سے فارمولیشن فرض نہیں کرے گا۔ براہِ کرم بتائیں کہ **{$prodName}** کے لیے کون سی **فارمولیشن (Generic Salt)** لگانی ہے؟\n\n**مثال:** `Paracetamol`"
                : "براہِ کرم **پروڈکٹ کا نام** اور **فارمولیشن (Generic Salt)** بتائیں۔\n\n**مثال:** `Set formulation Paracetamol for Panadol`";

            return $this->formatTaskResponse(
                titleEn: 'Formulation Details Needed (No Assumptions)',
                titleUr: 'فارمولیشن شامل کرنے کے لیے معلومات درکار ہیں',
                answerEn: $answerEn,
                answerUr: $answerUr,
                links: [['path' => '/products', 'en' => 'Open Products Catalog', 'ur' => 'پروڈکٹس لسٹ کھولیں']],
                taskMeta: [
                    'action' => 'bulk_assign_formulations',
                    'status' => 'needs_required_fields',
                    'updated_count' => 0,
                    'missing_fields' => $missingList,
                    'collected_fields' => array_filter(['product_name' => $prodName ?: null, 'formulation' => $formName ?: null]),
                ],
                productVersion: $productVersion,
                providerStatus: 'cube_task_needs_input'
            );
        }

        $this->clearPendingTask($user);

        $updatedProducts = [];
        $registeredFormulations = [];

        foreach ($standalone as $rawForm) {
            $resolved = Formulation::resolveOrCreate((string) $rawForm);
            if ($resolved) {
                $registeredFormulations[] = $resolved->name;
            }
        }

        foreach ($assignments as $item) {
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
     * Stores an incomplete pending task draft for the user in Cache for 30 minutes.
     *
     * @param  array<string, mixed>  $pendingTask
     */
    protected function savePendingTask(User $user, array $pendingTask): void
    {
        Cache::put($this->pendingTaskCacheKey($user), $pendingTask, now()->addMinutes(30));
    }

    /**
     * Retrieves any active pending task draft for the user.
     *
     * @return ?array<string, mixed>
     */
    protected function getPendingTask(User $user): ?array
    {
        $val = Cache::get($this->pendingTaskCacheKey($user));

        return is_array($val) ? $val : null;
    }

    /**
     * Clears any active pending task draft for the user.
     */
    public function clearPendingTask(User $user): void
    {
        Cache::forget($this->pendingTaskCacheKey($user));
    }

    protected function pendingTaskCacheKey(User $user): string
    {
        return 'cube_pending_task_user_'.$user->id;
    }

    protected function buildTaskCancelledResponse(string $productVersion): array
    {
        return $this->formatTaskResponse(
            titleEn: 'Pending Task Cancelled',
            titleUr: 'زیرِ التواء ٹاسک منسوخ کر دیا گیا',
            answerEn: '✅ The pending task has been cancelled and no changes were saved. Let me know whenever you want to start a new task or ask a question!',
            answerUr: '✅ زیرِ التواء ٹاسک منسوخ کر دیا گیا ہے اور کوئی تبدیلی محفوظ نہیں کی گئی۔',
            links: [],
            taskMeta: [
                'action' => 'task_cancelled',
                'cancelled' => true,
            ],
            productVersion: $productVersion,
            providerStatus: 'cube_task_cancelled'
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
