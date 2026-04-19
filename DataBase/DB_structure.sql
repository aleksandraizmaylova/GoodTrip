CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    email VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL
);

CREATE TABLE IF NOT EXISTS attraction_categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    icon_url VARCHAR(500),
    color VARCHAR(7)
);

CREATE TABLE IF NOT EXISTS attractions (
    id SERIAL PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    short_description TEXT,
    full_description TEXT,
    category_id INTEGER REFERENCES attraction_categories(id),
    latitude DECIMAL(10, 8) NOT NULL,
    longitude DECIMAL(11, 8) NOT NULL,
    address VARCHAR(500),
    city VARCHAR(100),
    image_urls TEXT[]
);

CREATE TABLE IF NOT EXISTS user_attraction_status (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    attraction_id INTEGER REFERENCES attractions(id) ON DELETE CASCADE,
    status VARCHAR(20) NOT NULL DEFAULT 'not_visited'
        CHECK (status IN ('not_visited', 'planned', 'visited')),
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, attraction_id)
);

CREATE TABLE IF NOT EXISTS reviews (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    attraction_id INTEGER REFERENCES attractions(id) ON DELETE CASCADE,
    rating INTEGER NOT NULL CHECK (rating >= 1 AND rating <= 5),
    text TEXT,
    photos TEXT[],
    UNIQUE(user_id, attraction_id)
);

CREATE TABLE IF NOT EXISTS achievements (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT NOT NULL,
    icon_url VARCHAR(500),
    criteria_type VARCHAR(50),
    criteria_value INTEGER,
    badge_color VARCHAR(7)
);

CREATE TABLE IF NOT EXISTS user_achievements (
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    achievement_id INTEGER REFERENCES achievements(id) ON DELETE CASCADE,
    earned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, achievement_id)
);

CREATE INDEX IF NOT EXISTS idx_attractions_category ON attractions(category_id);
CREATE INDEX IF NOT EXISTS idx_attractions_city ON attractions(city);
CREATE INDEX IF NOT EXISTS idx_user_status_user ON user_attraction_status(user_id);
CREATE INDEX IF NOT EXISTS idx_user_status_attraction ON user_attraction_status(attraction_id);
CREATE INDEX IF NOT EXISTS idx_reviews_attraction ON reviews(attraction_id);

INSERT INTO attraction_categories (name, icon_url, color)
VALUES
    ('Музей', NULL, '#1E88E5'),
    ('Достопримечательность', NULL, '#8E24AA'),
    ('Парк', NULL, '#43A047')
ON CONFLICT (name) DO NOTHING;

INSERT INTO attractions (name, short_description, full_description, category_id, latitude, longitude, address, city, image_urls)
SELECT
    s.name,
    s.short_description,
    s.full_description,
    c.id,
    s.latitude,
    s.longitude,
    s.address,
    s.city,
    ARRAY[]::TEXT[]
FROM (
    VALUES
        ('Государственный исторический музей', 'Крупнейший национальный исторический музей России', 'Крупнейший национальный исторический музей России', 'Музей', 55.7558, 37.6211, 'Москва, Красная площадь, 1', 'Москва'),
        ('Третьяковская галерея', 'Художественный музей с коллекцией русского искусства', 'Художественный музей с коллекцией русского искусства', 'Музей', 55.7414, 37.6189, 'Москва, Лаврушинский пер., 10', 'Москва'),
        ('Храм Василия Блаженного', 'Православный храм на Красной площади', 'Православный храм на Красной площади', 'Достопримечательность', 55.7525, 37.6231, 'Москва, Красная площадь', 'Москва'),
        ('Эрмитаж', 'Один из крупнейших художественных музеев мира', 'Один из крупнейших художественных музеев мира', 'Музей', 59.9398, 30.3146, 'Санкт-Петербург, Дворцовая наб., 34', 'Санкт-Петербург'),
        ('Петергоф', 'Дворцово-парковый ансамбль с фонтанами', 'Дворцово-парковый ансамбль с фонтанами', 'Парк', 59.8847, 29.9089, 'Санкт-Петербург, г. Петергоф', 'Санкт-Петербург')
) AS s(name, short_description, full_description, category_name, latitude, longitude, address, city)
JOIN attraction_categories c ON c.name = s.category_name
WHERE NOT EXISTS (
    SELECT 1 FROM attractions a WHERE a.name = s.name
);

INSERT INTO reviews (user_id, attraction_id, rating, text, photos)
SELECT NULL, a.id, s.rating, s.text, ARRAY[]::TEXT[]
FROM (
    VALUES
        ('Государственный исторический музей', 5, 'Отличная коллекция и экспозиции'),
        ('Третьяковская галерея', 5, 'Лучшее место для знакомства с русским искусством'),
        ('Храм Василия Блаженного', 5, 'Символ Москвы и уникальная архитектура'),
        ('Эрмитаж', 5, 'Огромная коллекция мирового искусства'),
        ('Петергоф', 4, 'Прекрасные фонтаны и парки')
) AS s(name, rating, text)
JOIN attractions a ON a.name = s.name
WHERE NOT EXISTS (
    SELECT 1 FROM reviews r WHERE r.attraction_id = a.id
);