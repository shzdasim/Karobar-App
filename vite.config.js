import { defineConfig } from 'vite';
import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';

export default defineConfig({
    base: '/build/',
    publicDir: false,
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./resources/js', import.meta.url)),
        },
    },
    server: {
        host: '0.0.0.0',
        cors: {
            origin: '*',
            methods: ['GET', 'HEAD', 'PUT', 'PATCH', 'POST', 'DELETE', 'OPTIONS'],
            allowedHeaders: ['*'],
        },
        hmr: {
            host: 'localhost',
        },
        watch: {
            ignored: [
                '**/storage/**',
                '**/database/**',
                '**/native-win/**',
            ],
        },
    },
    plugins: [
        react(),
    ],
    build: {
        outDir: 'public/build',
        emptyOutDir: true,
        manifest: 'manifest.json',
        assetsInlineLimit: 0,
        rolldownOptions: {
            input: ['resources/css/app.css', 'resources/js/app.jsx'],
            output: {
                codeSplitting: {
                    groups: [
                        {
                            name: 'vendor-react',
                            test: /node_modules[\\/](react|react-dom|react-router|react-router-dom|scheduler)[\\/]/,
                            priority: 20,
                        },
                        {
                            name: 'vendor',
                            test: /node_modules[\\/](clsx|@floating-ui|prop-types|react-is|hoist-non-react-statics|tiny-invariant)[\\/]/,
                            priority: 18,
                        },
                        {
                            name: 'vendor-charts',
                            test: /node_modules[\\/](recharts|d3-[^\\/]+|victory-vendor)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-filepond',
                            test: /node_modules[\\/](filepond|react-filepond|filepond-plugin-[^\\/]+)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-datepicker',
                            test: /node_modules[\\/](react-datepicker|date-fns)[\\/]/,
                            priority: 15,
                        },
                        {
                            name: 'vendor-select',
                            test: /node_modules[\\/](react-select|@emotion|memoize-one)[\\/]/,
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
