<?php

return [

    /*
    |--------------------------------------------------------------------------
    | Third Party Services
    |--------------------------------------------------------------------------
    |
    | This file is for storing the credentials for third party services such
    | as Mailgun, Postmark, AWS and more. This file provides the de facto
    | location for this type of information, allowing packages to have
    | a conventional file to locate the various service credentials.
    |
    */

    'postmark' => [
        'token' => env('POSTMARK_TOKEN'),
    ],

    'resend' => [
        'key' => env('RESEND_KEY'),
    ],

    'ses' => [
        'key' => env('AWS_ACCESS_KEY_ID'),
        'secret' => env('AWS_SECRET_ACCESS_KEY'),
        'region' => env('AWS_DEFAULT_REGION', 'us-east-1'),
    ],

    'slack' => [
        'notifications' => [
            'bot_user_oauth_token' => env('SLACK_BOT_USER_OAUTH_TOKEN'),
            'channel' => env('SLACK_BOT_USER_DEFAULT_CHANNEL'),
        ],
    ],

    'whatsapp' => [
        'enabled'              => env('WHATSAPP_ENABLED'),
        'provider'             => env('WHATSAPP_PROVIDER', 'meta_cloud'),
        'access_token'         => env('WHATSAPP_ACCESS_TOKEN'),
        'phone_number_id'      => env('WHATSAPP_PHONE_NUMBER_ID'),
        'business_account_id'  => env('WHATSAPP_BUSINESS_ACCOUNT_ID'),
        'api_version'          => env('WHATSAPP_API_VERSION', 'v20.0'),
        'api_url'              => env('WHATSAPP_API_URL'),
        'default_country_code' => env('WHATSAPP_DEFAULT_COUNTRY_CODE', '92'),
        'template_name'        => env('WHATSAPP_TEMPLATE_NAME'),
        'template_language'    => env('WHATSAPP_TEMPLATE_LANGUAGE', 'en_US'),
        'auto_fallback'        => env('WHATSAPP_AUTO_FALLBACK', true),
    ],

];
