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

-- Migrations (project can start with an existing DB volume)
ALTER TABLE achievements ADD COLUMN IF NOT EXISTS code VARCHAR(50);
CREATE UNIQUE INDEX IF NOT EXISTS ux_achievements_code ON achievements(code);

CREATE TABLE IF NOT EXISTS user_achievements (
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    achievement_id INTEGER REFERENCES achievements(id) ON DELETE CASCADE,
    earned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, achievement_id)
);

CREATE TABLE IF NOT EXISTS user_sessions (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token VARCHAR(255) NOT NULL UNIQUE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_attractions_category ON attractions(category_id);
CREATE INDEX IF NOT EXISTS idx_attractions_city ON attractions(city);
CREATE INDEX IF NOT EXISTS idx_user_status_user ON user_attraction_status(user_id);
CREATE INDEX IF NOT EXISTS idx_user_status_attraction ON user_attraction_status(attraction_id);
CREATE INDEX IF NOT EXISTS idx_reviews_attraction ON reviews(attraction_id);
CREATE INDEX IF NOT EXISTS idx_user_sessions_token ON user_sessions(token);

-- Achievements seed (codes are used as JSON keys on backend)
INSERT INTO achievements (code, name, description, icon_url, criteria_type, criteria_value, badge_color)
VALUES
    ('achievement1', 'Первый шаг', 'Зарегистрируйтесь на сайте', '/img/achieve1.jpeg', 'register', 1, '#3b82f6'),
    ('achievement2', 'Святой', 'Посетите 5 храмов', '/img/achieve2.jpeg', 'visit_category', 5, '#f97316'),
    ('achievement3', 'Знаток', 'Предложите свою достопримечательность', '/img/achieve3.jpeg', 'propose_attraction', 1, '#10b981')
ON CONFLICT (code) DO NOTHING;

-- Registration achievement for new users
CREATE OR REPLACE FUNCTION grant_register_achievement()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO user_achievements (user_id, achievement_id, earned_at)
    SELECT NEW.id, a.id, NOW()
    FROM achievements a
    WHERE a.code = 'achievement1'
    ON CONFLICT DO NOTHING;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_users_register_achievement ON users;
CREATE TRIGGER trg_users_register_achievement
AFTER INSERT ON users
FOR EACH ROW
EXECUTE FUNCTION grant_register_achievement();

-- Achievements recalculation on attraction status change
CREATE OR REPLACE FUNCTION update_user_achievements_on_status()
RETURNS TRIGGER AS $$
DECLARE
    temple_category_id INT;
    visited_temples_count INT;
    ach2_id INT;
BEGIN
    SELECT id INTO temple_category_id FROM attraction_categories WHERE name ILIKE 'Храм' LIMIT 1;
    IF temple_category_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT id INTO ach2_id FROM achievements WHERE code = 'achievement2' LIMIT 1;
    IF ach2_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT COUNT(*)
    INTO visited_temples_count
    FROM user_attraction_status uas
    JOIN attractions at ON at.id = uas.attraction_id
    WHERE uas.user_id = NEW.user_id
      AND uas.status = 'visited'
      AND at.category_id = temple_category_id;

    IF visited_temples_count >= 5 THEN
        INSERT INTO user_achievements (user_id, achievement_id, earned_at)
        VALUES (NEW.user_id, ach2_id, NOW())
        ON CONFLICT DO NOTHING;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_status_update_achievements ON user_attraction_status;
CREATE TRIGGER trg_status_update_achievements
AFTER INSERT OR UPDATE OF status ON user_attraction_status
FOR EACH ROW
WHEN (NEW.status IN ('visited','planned','not_visited'))
EXECUTE FUNCTION update_user_achievements_on_status();

INSERT INTO attraction_categories (name, icon_url, color)
VALUES
    ('Музей', NULL, '#1E88E5'),
    ('Храм', NULL, '#F97316'),
    ('Памятник', NULL, '#EAB308'),
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
        ('Петергоф', 'Дворцово-парковый ансамбль с фонтанами', 'Дворцово-парковый ансамбль с фонтанами', 'Парк', 59.8847, 29.9089, 'Санкт-Петербург, г. Петергоф', 'Санкт-Петербург'),

        -- Temples (for achievement2 тестирования: 5 посещенных храмов)
        ('Храм Христа Спасителя', 'Кафедральный собор Русской православной церкви', 'Кафедральный собор Русской православной церкви', 'Храм', 55.7445, 37.6050, 'Москва, ул. Волхонка, 15', 'Москва'),
        ('Казанский собор', 'Один из крупнейших храмов Санкт-Петербурга', 'Один из крупнейших храмов Санкт-Петербурга', 'Храм', 59.9342, 30.3240, 'Санкт-Петербург, Казанская пл., 2', 'Санкт-Петербург'),
        ('Исаакиевский собор', 'Крупнейший православный храм Санкт-Петербурга', 'Крупнейший православный храм Санкт-Петербурга', 'Храм', 59.9343, 30.3061, 'Санкт-Петербург, Исаакиевская пл., 4', 'Санкт-Петербург'),
        ('Троице-Сергиева лавра', 'Крупнейший мужской монастырь Русской православной церкви', 'Крупнейший мужской монастырь Русской православной церкви', 'Храм', 56.3121, 38.1302, 'Сергиев Посад, Красногорская пл., 1', 'Сергиев Посад'),
        ('Храм Спаса на Крови', 'Памятник русской архитектуры в Санкт-Петербурге', 'Памятник русской архитектуры в Санкт-Петербурге', 'Храм', 59.9401, 30.3287, 'Санкт-Петербург, наб. канала Грибоедова, 2Б', 'Санкт-Петербург')
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