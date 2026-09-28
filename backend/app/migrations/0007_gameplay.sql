-- Partidas medidas pelo app, a pedido do dono (27/09/2026): subir para o banco
-- para analisar no conjunto de PCs o que muda o FPS de verdade e calibrar as
-- regras por nivel de hardware.
--
-- So' com consentimento de telemetria no app. So' numeros: nada de nome do PC,
-- de usuario ou de programa. Das quedas vem so' o tipo de causa.

CREATE TABLE gameplay_sessions (
    id                   BIGSERIAL PRIMARY KEY,
    device_id            BIGINT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    session_key          TEXT NOT NULL,
    game_id              TEXT NOT NULL,
    started_at           TIMESTAMPTZ NOT NULL,
    ended_at             TIMESTAMPTZ NOT NULL,
    measured_seconds     REAL NOT NULL,
    avg_fps              REAL NOT NULL,
    low1_fps             REAL NOT NULL,
    low01_fps            REAL NOT NULL,
    p99_frametime_ms     REAL NOT NULL,
    stutters_per_minute  REAL NOT NULL,
    avg_cpu_percent      REAL,
    avg_gpu_percent      REAL,
    display_hz           INT,
    agent_version        TEXT NOT NULL DEFAULT '',
    -- Resumo do hardware: tier, cpu, threads, gpu, vram_gb, ram_gb, windows_build.
    hardware             JSONB NOT NULL DEFAULT '{}',
    -- [[segundo, fps medio, fps do pior quadro], ...], no maximo 900 pontos.
    timeline             JSONB NOT NULL DEFAULT '[]',
    drops                INT NOT NULL DEFAULT 0,
    drop_causes          JSONB NOT NULL DEFAULT '{}',
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- O app reenvia se a resposta se perder: a mesma partida nao duplica.
    UNIQUE (device_id, session_key)
);

CREATE INDEX gameplay_sessions_game ON gameplay_sessions (game_id, started_at DESC);
CREATE INDEX gameplay_sessions_gpu ON gameplay_sessions (game_id, (hardware->>'gpu'));

-- Telemetria: a fila pode ficar dias no PC sem internet. Sem estes campos o
-- banco guardava a hora e a versao do ENVIO, nao de quando aconteceu.
ALTER TABLE telemetry_events ADD COLUMN occurred_at TIMESTAMPTZ;
ALTER TABLE telemetry_events ADD COLUMN event_agent_version TEXT;
