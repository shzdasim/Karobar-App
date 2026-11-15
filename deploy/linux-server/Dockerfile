FROM php:8.3-fpm-bookworm

# Install system dependencies, Nginx, Supervisor, Node.js 20, MariaDB client, and SQLite3
RUN apt-get update && apt-get install -y --no-install-recommends \
    nginx \
    supervisor \
    curl \
    ca-certificates \
    gnupg \
    zip \
    unzip \
    git \
    sqlite3 \
    libsqlite3-dev \
    libzip-dev \
    libpng-dev \
    libjpeg62-turbo-dev \
    libfreetype6-dev \
    libonig-dev \
    libxml2-dev \
    libicu-dev \
    libsodium-dev \
    mariadb-client \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && docker-php-ext-configure gd --with-freetype --with-jpeg \
    && docker-php-ext-install -j$(nproc) \
        pdo_mysql \
        pdo_sqlite \
        sqlite3 \
        mbstring \
        zip \
        exif \
        pcntl \
        bcmath \
        gd \
        intl \
        opcache \
        sodium \
    && pecl install redis \
    && docker-php-ext-enable redis \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Install Composer
COPY --from=composer:2 /usr/bin/composer /usr/bin/composer

WORKDIR /opt/karobar-server

# Copy application source
COPY . /opt/karobar-server

# Install PHP & Node dependencies and build production frontend assets
RUN composer install --no-dev --optimize-autoloader --no-interaction \
    && npm ci --no-audit --no-fund \
    && npm run build \
    && chown -R www-data:www-data /opt/karobar-server/storage /opt/karobar-server/bootstrap/cache

# Configure Nginx & PHP-FPM
COPY deploy/linux-server/nginx/karobar.conf /etc/nginx/sites-available/default
COPY deploy/linux-server/php/karobar-fpm.conf /usr/local/etc/php-fpm.d/zz-karobar.conf

EXPOSE 8000

CMD ["sh", "-c", "php-fpm -D && nginx -g 'daemon off;'"]
