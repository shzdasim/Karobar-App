import { defineConfig } from 'vite';
import laravel from 'laravel-vite-plugin';
import react from '@vitejs/plugin-react';

export default defineConfig({
    plugins: [
        laravel({
            input: ['resources/css/app.css', 'resources/js/app.jsx'],
            refresh: true,
        }),
        react(),
    ],
    build: {
        rolldownOptions: {
            output: {
                codeSplitting: {
                    groups: [
                        {
                            name: 'vendor-react',
                            test: /node_modules[\\/](react|react-dom|react-router|react-router-dom|scheduler)[\\/]/,
                            priority: 20,
                        },
                        {
                            name: 'vendor-charts',
                            test: /node_modules[\\/](recharts|d3-[^\\/]+|victory-vendor|decimal\.js-light|eventemitter3|tiny-invariant)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-filepond',
                            test: /node_modules[\\/](filepond|react-filepond|filepond-plugin-[^\\/]+)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-datepicker',
                            test: /node_modules[\\/](react-datepicker|date-fns|@floating-ui)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-select',
                            test: /node_modules[\\/](react-select|@emotion|memoize-one)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-icons',
                            test: /node_modules[\\/]@heroicons[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor',
                            test: /node_modules[\\/]/,
                            priority: 10,
                        },
                    ],
                },
            },
        },
    },
});
