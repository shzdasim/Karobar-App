<?php

use App\Services\LicenseService;

return [
    /*
    |--------------------------------------------------------------------------
    | Pinned Vendor Master Public Key (Ed25519)
    |--------------------------------------------------------------------------
    |
    | The Master Public Key is permanently pinned inside LicenseService::MASTER_PUBLIC_KEY
    | and is intentionally NOT read from .env so that:
    |   1. You never need to configure a key per deployment.
    |   2. Users with filesystem access cannot tamper with .env to forge licenses.
    |
    */
    'public_key' => LicenseService::MASTER_PUBLIC_KEY,

    /*
    |--------------------------------------------------------------------------
    | Hardware Binding Enforcement
    |--------------------------------------------------------------------------
    |
    | Hardware binding is always enforced upon activation (locking the activated
    | database record to the activating machine's deterministic hardware ID) and
    | whenever a signed license payload specifies a target machine ID.
    |
    */
    'bind_to_machine' => true,
];
