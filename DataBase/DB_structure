CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    email VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL
);

CREATE TABLE attraction_categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    icon_url VARCHAR(500),
    color VARCHAR(7)
);

CREATE TABLE attractions (
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

CREATE TABLE user_attraction_status (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    attraction_id INTEGER REFERENCES attractions(id) ON DELETE CASCADE,
    status VARCHAR(20) NOT NULL DEFAULT 'not_visited' 
        CHECK (status IN ('not_visited', 'planned', 'visited')),
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, attraction_id)
);

CREATE TABLE reviews (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    attraction_id INTEGER REFERENCES attractions(id) ON DELETE CASCADE,
    rating INTEGER NOT NULL CHECK (rating >= 1 AND rating <= 5),
    text TEXT,
    photos TEXT[],
    UNIQUE(user_id, attraction_id)
);

CREATE TABLE achievements (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT NOT NULL,
    icon_url VARCHAR(500),
    criteria_type VARCHAR(50),
    criteria_value INTEGER,
    badge_color VARCHAR(7)
);

CREATE TABLE user_achievements (
    user_id INTEGER REFERENCES users(id) ON DELETE CASCADE,
    achievement_id INTEGER REFERENCES achievements(id) ON DELETE CASCADE,
    earned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, achievement_id)
);

CREATE INDEX idx_attractions_category ON attractions(category_id);
CREATE INDEX idx_attractions_city ON attractions(city);
CREATE INDEX idx_user_status_user ON user_attraction_status(user_id);
CREATE INDEX idx_user_status_attraction ON user_attraction_status(attraction_id);
CREATE INDEX idx_reviews_attraction ON reviews(attraction_id);