CREATE DATABASE IF NOT EXISTS bilingual_vocab_dau CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE bilingual_vocab_dau;

CREATE TABLE IF NOT EXISTS users (
    id VARCHAR(64) PRIMARY KEY,
    email VARCHAR(255) NOT NULL UNIQUE,
    password VARCHAR(255) NULL,
    name VARCHAR(255) NOT NULL,
    role VARCHAR(32) NOT NULL DEFAULT 'student',
    student_id VARCHAR(64) NULL,
    department VARCHAR(255) NULL,
    expertise VARCHAR(255) NULL,
    target_cefr VARCHAR(16) NULL,
    created_at VARCHAR(64) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'active',
    last_active VARCHAR(128) NULL
);

CREATE TABLE IF NOT EXISTS topics (
    id VARCHAR(64) PRIMARY KEY,
    owner_user_id VARCHAR(64) NULL,
    name VARCHAR(255) NOT NULL,
    created_at VARCHAR(64) NOT NULL,
    level VARCHAR(16) NULL,
    is_dau_curriculum TINYINT(1) NOT NULL DEFAULT 0,
    department VARCHAR(255) NULL
);

CREATE TABLE IF NOT EXISTS vocab_cards (
    id VARCHAR(128) PRIMARY KEY,
    topic_id VARCHAR(64) NOT NULL,
    word_en VARCHAR(255) NOT NULL,
    ipa_en VARCHAR(255) NULL,
    word_zh VARCHAR(255) NOT NULL DEFAULT '',
    pinyin VARCHAR(255) NOT NULL DEFAULT '',
    word_ja VARCHAR(255) NULL,
    reading_ja VARCHAR(255) NULL,
    meaning_vi TEXT NOT NULL,
    example_en TEXT NOT NULL,
    example_zh TEXT NOT NULL,
    example_ja TEXT NULL,
    example_meaning_vi TEXT NOT NULL,
    difficulty VARCHAR(16) NULL,
    learned TINYINT(1) NOT NULL DEFAULT 0,
    note TEXT NULL,
    review_count INT NOT NULL DEFAULT 0,
    last_reviewed_at VARCHAR(64) NULL,
    next_review_at VARCHAR(64) NULL,
    interval_days INT NOT NULL DEFAULT 0,
    ease_factor DOUBLE NOT NULL DEFAULT 2.5
);

CREATE TABLE IF NOT EXISTS video_sessions (
    id VARCHAR(128) PRIMARY KEY,
    youtube_url VARCHAR(512) NOT NULL,
    video_id VARCHAR(64) NOT NULL,
    title VARCHAR(255) NOT NULL,
    channel_title VARCHAR(255) NULL,
    duration_sec INT NOT NULL DEFAULT 0,
    language VARCHAR(16) NOT NULL DEFAULT 'en',
    created_at VARCHAR(64) NOT NULL,
    words_count INT NOT NULL DEFAULT 0,
    subtitles_json LONGTEXT NULL
);

CREATE TABLE IF NOT EXISTS video_words (
    id VARCHAR(128) PRIMARY KEY,
    session_id VARCHAR(128) NOT NULL,
    word VARCHAR(255) NOT NULL,
    language VARCHAR(16) NOT NULL DEFAULT 'en',
    ipa_en VARCHAR(255) NULL,
    pinyin VARCHAR(255) NULL,
    timestamp_sec DOUBLE NOT NULL DEFAULT 0,
    meaning_vi TEXT NOT NULL,
    context_sentence TEXT NULL,
    difficulty VARCHAR(16) NULL
);

CREATE TABLE IF NOT EXISTS assignments (
    id VARCHAR(64) PRIMARY KEY,
    expert_id VARCHAR(64) NOT NULL,
    expert_name VARCHAR(255) NOT NULL,
    title VARCHAR(255) NOT NULL,
    skill VARCHAR(32) NOT NULL DEFAULT 'reading',
    target_level VARCHAR(16) NOT NULL DEFAULT 'B2',
    duration_minutes INT NOT NULL DEFAULT 45,
    department_category VARCHAR(128) NOT NULL DEFAULT '',
    content LONGTEXT NOT NULL,
    instructions LONGTEXT NOT NULL,
    reference_media_url VARCHAR(512) NULL,
    due_date VARCHAR(64) NULL,
    created_at VARCHAR(64) NOT NULL,
    max_score DOUBLE NOT NULL DEFAULT 10.0,
    reading_quiz LONGTEXT NULL,
    target_student_id VARCHAR(64) NULL,
    target_student_name VARCHAR(255) NULL,
    topic_ids_json LONGTEXT NULL,
    topic_names_json LONGTEXT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'draft',
    show_answers TINYINT(1) NOT NULL DEFAULT 0,
    exam_structure_json LONGTEXT NULL
);

CREATE TABLE IF NOT EXISTS submissions (
    id VARCHAR(64) PRIMARY KEY,
    assignment_id VARCHAR(64) NOT NULL,
    assignment_title VARCHAR(255) NOT NULL,
    skill VARCHAR(32) NOT NULL DEFAULT 'reading',
    student_id VARCHAR(64) NOT NULL,
    student_name VARCHAR(255) NOT NULL,
    student_code VARCHAR(64) NULL,
    student_department VARCHAR(255) NULL,
    content LONGTEXT NOT NULL,
    audio_url VARCHAR(512) NULL,
    reading_answers LONGTEXT NULL,
    reading_auto_score DOUBLE NULL,
    topic_sentence TEXT NULL,
    duration_spent INT NULL,
    submitted_at VARCHAR(64) NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'pending',
    score DOUBLE NULL,
    feedback TEXT NULL,
    graded_by VARCHAR(255) NULL,
    graded_at VARCHAR(64) NULL,
    assessed_level VARCHAR(16) NULL,
    exam_answers_json LONGTEXT NULL,
    section_scores_json LONGTEXT NULL,
    auto_score DOUBLE NULL,
    show_answers TINYINT(1) NOT NULL DEFAULT 0,
    exam_review_json LONGTEXT NULL
);

CREATE TABLE IF NOT EXISTS consultations (
    id VARCHAR(64) PRIMARY KEY,
    expert_id VARCHAR(64) NOT NULL,
    expert_name VARCHAR(255) NOT NULL,
    student_id VARCHAR(64) NOT NULL,
    student_name VARCHAR(255) NOT NULL,
    current_level VARCHAR(16) NOT NULL,
    recommended_level VARCHAR(16) NOT NULL,
    advice TEXT NOT NULL,
    strengths_json LONGTEXT NULL,
    weaknesses_json LONGTEXT NULL,
    study_plan TEXT NOT NULL,
    created_at VARCHAR(64) NOT NULL
);

CREATE TABLE IF NOT EXISTS ai_config (
    id INT PRIMARY KEY DEFAULT 1,
    model_name VARCHAR(128) NOT NULL,
    temperature DOUBLE NOT NULL DEFAULT 0.4,
    max_output_tokens INT NOT NULL DEFAULT 8192,
    anti_repetition TINYINT(1) NOT NULL DEFAULT 1,
    system_instruction TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS api_logs (
    id VARCHAR(64) PRIMARY KEY,
    timestamp VARCHAR(64) NOT NULL,
    endpoint VARCHAR(255) NOT NULL,
    method VARCHAR(16) NOT NULL,
    status INT NOT NULL,
    duration_ms INT NOT NULL,
    user_email VARCHAR(255) NULL,
    tokens_used INT NOT NULL DEFAULT 0
);
