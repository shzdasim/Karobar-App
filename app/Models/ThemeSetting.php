<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;

class ThemeSetting extends Model
{
    use HasFactory;

    protected $fillable = [
        'name',
        'primary_color',
        'primary_hover',
        'primary_light',
        'secondary_color',
        'secondary_hover',
        'secondary_light',
        'tertiary_color',
        'tertiary_hover',
        'tertiary_light',
        'background_color',
        'surface_color',
        'text_primary',
        'text_secondary',
        'success_color',
        'warning_color',
        'danger_color',
        'border_color',
        'shadow_color',
        'button_style',
        'sidebar_template',
        'is_active',
    ];

    protected $casts = [
        'is_active' => 'boolean',
    ];

    /**
     * Get CSS variables for this theme
     */
    public function getCssVariables(): array
    {
        // Map button style to border radius
        $radiusMap = [
            'rounded' => '0.5rem',
            'outlined' => '0.5rem',
            'pill' => '9999px',
            'soft' => '0.75rem',
        ];
        
        return [
            '--color-primary' => $this->primary_color,
            '--color-primary-hover' => $this->primary_hover,
            '--color-primary-light' => $this->primary_light,
            '--color-secondary' => $this->secondary_color,
            '--color-secondary-hover' => $this->secondary_hover,
            '--color-secondary-light' => $this->secondary_light,
            '--color-tertiary' => $this->tertiary_color,
            '--color-tertiary-hover' => $this->tertiary_hover,
            '--color-tertiary-light' => $this->tertiary_light,
            '--color-background' => $this->background_color,
            '--color-surface' => $this->surface_color,
            '--color-text-primary' => $this->text_primary,
            '--color-text-secondary' => $this->text_secondary,
            '--color-success' => $this->success_color,
            '--color-warning' => $this->warning_color,
            '--color-danger' => $this->danger_color,
            '--color-border' => $this->border_color,
            '--color-shadow' => $this->shadow_color,
            '--btn-radius' => $radiusMap[$this->button_style] ?? '0.5rem',
            '--sidebar-template' => $this->sidebar_template ?? 'classic',
        ];
    }

    /**
     * Get CSS string for inline styles
     */
    public function getCssString(): string
    {
        $vars = $this->getCssVariables();
        return collect($vars)->map(fn($value, $prop) => "{$prop}: {$value};")->implode(' ');
    }

    /**
     * Scope for active theme
     */
    public function scopeActive($query)
    {
        return $query->where('is_active', true);
    }

    /**
     * Activate this theme and deactivate others
     */
    public function activate(): void
    {
        static::where('is_active', true)->update(['is_active' => false]);
        $this->update(['is_active' => true]);
    }
}

