<?php

use Illuminate\Database\Migrations\Migration;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Support\Facades\Schema;
use Illuminate\Support\Facades\DB;

return new class extends Migration
{
    public function up(): void
    {
        Schema::create('barcode_templates', function (Blueprint $table) {
            $table->id();
            $table->string('name');
            $table->string('paper_type')->default('roll'); // 'roll' or 'sheet'
            $table->decimal('width_mm', 6, 2);
            $table->decimal('height_mm', 6, 2);
            $table->string('orientation')->default('landscape'); // 'landscape' or 'portrait'
            $table->unsignedInteger('columns')->default(1);
            $table->unsignedInteger('rows')->default(1);
            $table->decimal('gap_x_mm', 6, 2)->default(0);
            $table->decimal('gap_y_mm', 6, 2)->default(0);
            $table->decimal('margin_top_mm', 6, 2)->default(0);
            $table->decimal('margin_left_mm', 6, 2)->default(0);
            $table->decimal('margin_right_mm', 6, 2)->default(0);
            $table->decimal('margin_bottom_mm', 6, 2)->default(0);
            $table->json('layout_options')->nullable();
            $table->boolean('is_default')->default(false);
            $table->timestamps();
        });

        // Seed standard industry presets
        $presets = [
            [
                'name' => 'Thermal Roll 50x25mm (Standard Retail)',
                'paper_type' => 'roll',
                'width_mm' => 50.00,
                'height_mm' => 25.00,
                'orientation' => 'landscape',
                'columns' => 1,
                'rows' => 1,
                'gap_x_mm' => 0,
                'gap_y_mm' => 3.00,
                'margin_top_mm' => 1.00,
                'margin_left_mm' => 1.00,
                'margin_right_mm' => 1.00,
                'margin_bottom_mm' => 1.00,
                'layout_options' => json_encode([
                    'show_store_name' => true,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 28,
                    'font_scale' => 10,
                ]),
                'is_default' => true,
                'created_at' => now(),
                'updated_at' => now(),
            ],
            [
                'name' => 'Thermal Roll 38x25mm (Pharmacy Compact)',
                'paper_type' => 'roll',
                'width_mm' => 38.00,
                'height_mm' => 25.00,
                'orientation' => 'landscape',
                'columns' => 1,
                'rows' => 1,
                'gap_x_mm' => 0,
                'gap_y_mm' => 3.00,
                'margin_top_mm' => 1.00,
                'margin_left_mm' => 1.00,
                'margin_right_mm' => 1.00,
                'margin_bottom_mm' => 1.00,
                'layout_options' => json_encode([
                    'show_store_name' => false,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 24,
                    'font_scale' => 9,
                ]),
                'is_default' => false,
                'created_at' => now(),
                'updated_at' => now(),
            ],
            [
                'name' => 'Thermal Roll 40x30mm (Detailed with Batch/Expiry)',
                'paper_type' => 'roll',
                'width_mm' => 40.00,
                'height_mm' => 30.00,
                'orientation' => 'landscape',
                'columns' => 1,
                'rows' => 1,
                'gap_x_mm' => 0,
                'gap_y_mm' => 3.00,
                'margin_top_mm' => 1.50,
                'margin_left_mm' => 1.50,
                'margin_right_mm' => 1.50,
                'margin_bottom_mm' => 1.50,
                'layout_options' => json_encode([
                    'show_store_name' => true,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 30,
                    'font_scale' => 10,
                ]),
                'is_default' => false,
                'created_at' => now(),
                'updated_at' => now(),
            ],
            [
                'name' => 'Thermal Roll 2-Up (2 across, 38x25mm each)',
                'paper_type' => 'roll',
                'width_mm' => 38.00,
                'height_mm' => 25.00,
                'orientation' => 'landscape',
                'columns' => 2,
                'rows' => 1,
                'gap_x_mm' => 2.00,
                'gap_y_mm' => 3.00,
                'margin_top_mm' => 1.00,
                'margin_left_mm' => 1.00,
                'margin_right_mm' => 1.00,
                'margin_bottom_mm' => 1.00,
                'layout_options' => json_encode([
                    'show_store_name' => false,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 24,
                    'font_scale' => 9,
                ]),
                'is_default' => false,
                'created_at' => now(),
                'updated_at' => now(),
            ],
            [
                'name' => 'Avery 5160 / 8160 Sheet (30 labels/page, 3x10)',
                'paper_type' => 'sheet',
                'width_mm' => 66.70,
                'height_mm' => 25.40,
                'orientation' => 'portrait',
                'columns' => 3,
                'rows' => 10,
                'gap_x_mm' => 3.10,
                'gap_y_mm' => 0.00,
                'margin_top_mm' => 12.70,
                'margin_left_mm' => 4.80,
                'margin_right_mm' => 4.80,
                'margin_bottom_mm' => 12.70,
                'layout_options' => json_encode([
                    'show_store_name' => true,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 26,
                    'font_scale' => 10,
                ]),
                'is_default' => false,
                'created_at' => now(),
                'updated_at' => now(),
            ],
            [
                'name' => 'Standard A4 Sheet 24-Up (3x8, 70x37mm)',
                'paper_type' => 'sheet',
                'width_mm' => 70.00,
                'height_mm' => 37.00,
                'orientation' => 'portrait',
                'columns' => 3,
                'rows' => 8,
                'gap_x_mm' => 0.00,
                'gap_y_mm' => 0.00,
                'margin_top_mm' => 0.00,
                'margin_left_mm' => 0.00,
                'margin_right_mm' => 0.00,
                'margin_bottom_mm' => 0.00,
                'layout_options' => json_encode([
                    'show_store_name' => true,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 34,
                    'font_scale' => 11,
                ]),
                'is_default' => false,
                'created_at' => now(),
                'updated_at' => now(),
            ],
            [
                'name' => 'Standard A4 Sheet 40-Up (4x10, 48.5x25.4mm)',
                'paper_type' => 'sheet',
                'width_mm' => 48.50,
                'height_mm' => 25.40,
                'orientation' => 'portrait',
                'columns' => 4,
                'rows' => 10,
                'gap_x_mm' => 0.00,
                'gap_y_mm' => 0.00,
                'margin_top_mm' => 0.00,
                'margin_left_mm' => 0.00,
                'margin_right_mm' => 0.00,
                'margin_bottom_mm' => 0.00,
                'layout_options' => json_encode([
                    'show_store_name' => false,
                    'show_product_name' => true,
                    'show_product_code' => false,
                    'show_barcode' => true,
                    'show_barcode_text' => true,
                    'show_price' => true,
                    'price_type' => 'retail',
                    'show_batch' => true,
                    'show_expiry' => true,
                    'expiry_format' => 'MM/YY',
                    'barcode_height' => 24,
                    'font_scale' => 9,
                ]),
                'is_default' => false,
                'created_at' => now(),
                'updated_at' => now(),
            ],
        ];

        DB::table('barcode_templates')->insert($presets);
    }

    public function down(): void
    {
        Schema::dropIfExists('barcode_templates');
    }
};

