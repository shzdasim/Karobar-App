<?php

namespace App\Models;

use Carbon\CarbonInterface;
use Illuminate\Support\Carbon;
use Laravel\Sanctum\PersonalAccessToken as SanctumPersonalAccessToken;

class PersonalAccessToken extends SanctumPersonalAccessToken
{
    /**
     * The table associated with the model.
     *
     * @var string
     */
    protected $table = 'personal_access_tokens';

    /**
     * Minimum interval (in seconds) between consecutive `last_used_at` DB writes
     * to prevent SQLite write-lock serialization across parallel GET requests.
     */
    public const LAST_USED_AT_THROTTLE_SECONDS = 300;

    /**
     * Save the model to the database, throttling redundant `last_used_at`-only writes.
     *
     * @param  array<string, mixed>  $options
     * @return bool
     */
    public function save(array $options = [])
    {
        if ($this->exists) {
            $dirty = $this->getDirty();

            if (count($dirty) === 1 && array_key_exists('last_used_at', $dirty)) {
                $original = $this->getOriginal('last_used_at');

                if ($original !== null) {
                    try {
                        $originalTime = $original instanceof CarbonInterface
                            ? $original
                            : Carbon::parse($original);
                        $currentTime = $this->last_used_at instanceof CarbonInterface
                            ? $this->last_used_at
                            : Carbon::now();

                        if (abs($currentTime->getTimestamp() - $originalTime->getTimestamp()) < self::LAST_USED_AT_THROTTLE_SECONDS) {
                            return true;
                        }
                    } catch (\Throwable) {
                        // Fall through to normal save if timestamp parsing fails
                    }
                }
            }
        }

        return parent::save($options);
    }
}
