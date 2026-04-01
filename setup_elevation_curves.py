"""
Script para crear la tabla elevation_capacity_curves en PostgreSQL
y cargar los datos extraidos del Excel FUNTABLA.

Uso: python3 setup_elevation_curves.py
"""
import os
import json
import pg8000

PG_HOST = "localhost"
PG_PORT = 5432
PG_DB = "mycloud_timescale"
PG_USER = "postgres"
PG_PASS = "Cfe2026##"

DDL = """
CREATE SCHEMA IF NOT EXISTS hydro_model;

DROP TABLE IF EXISTS hydro_model.elevation_capacity CASCADE;
CREATE TABLE hydro_model.elevation_capacity (
    id SERIAL PRIMARY KEY,
    dam_name VARCHAR(50) NOT NULL,
    elevation REAL NOT NULL,
    capacity_mm3 REAL NOT NULL,
    area_km2 REAL,
    specific_consumption REAL,
    UNIQUE(dam_name, elevation)
);

CREATE INDEX idx_elev_cap_dam ON hydro_model.elevation_capacity(dam_name);
CREATE INDEX idx_elev_cap_dam_elev ON hydro_model.elevation_capacity(dam_name, elevation);

-- Tabla de parametros del modelo por presa
DROP TABLE IF EXISTS hydro_model.dam_params CASCADE;
CREATE TABLE hydro_model.dam_params (
    dam_name VARCHAR(50) PRIMARY KEY,
    cuenca_code VARCHAR(10) NOT NULL,
    model_type VARCHAR(10) NOT NULL DEFAULT 'daily',
    off_volume REAL DEFAULT 0,
    hui_factor REAL DEFAULT 1.0,
    drain_coefficient REAL DEFAULT 0.15,
    drain_base REAL DEFAULT 100.0,
    curve_number REAL DEFAULT 75.0,
    transfer_time_hours INT DEFAULT 0,
    has_previous_dam BOOLEAN DEFAULT FALSE,
    previous_dam_name VARCHAR(50),
    cascade_order INT NOT NULL
);

INSERT INTO hydro_model.dam_params (dam_name, cuenca_code, model_type, cascade_order, has_previous_dam, previous_dam_name) VALUES
('Angostura',  'ANG', 'daily',  1, FALSE, NULL),
('Chicoasen',  'MMT', 'hourly', 2, TRUE,  'Angostura'),
('Malpaso',    'MPS', 'daily',  3, TRUE,  'Chicoasen'),
('JGrijalva',  'MPS', 'daily',  4, TRUE,  'Malpaso'),
('Penitas',    'PEA', 'daily',  5, TRUE,  'JGrijalva');

-- Tabla de coeficientes HUI por subcuenca
DROP TABLE IF EXISTS hydro_model.hui_coefficients CASCADE;
CREATE TABLE hydro_model.hui_coefficients (
    id SERIAL PRIMARY KEY,
    cuenca_code VARCHAR(10) NOT NULL,
    hour_index INT NOT NULL,
    coefficient REAL NOT NULL,
    UNIQUE(cuenca_code, hour_index)
);

-- HUI por defecto (triangular simplificado - se pueden ajustar despues)
-- Angostura (ANG) - cuenca grande, respuesta lenta
INSERT INTO hydro_model.hui_coefficients (cuenca_code, hour_index, coefficient) VALUES
('ANG', 0, 0.05), ('ANG', 1, 0.10), ('ANG', 2, 0.20), ('ANG', 3, 0.25),
('ANG', 4, 0.20), ('ANG', 5, 0.10), ('ANG', 6, 0.05), ('ANG', 7, 0.03),
('ANG', 8, 0.02);

-- Chicoasen (MMT) - cuenca pequena, respuesta rapida
INSERT INTO hydro_model.hui_coefficients (cuenca_code, hour_index, coefficient) VALUES
('MMT', 0, 0.15), ('MMT', 1, 0.30), ('MMT', 2, 0.25), ('MMT', 3, 0.15),
('MMT', 4, 0.10), ('MMT', 5, 0.05);

-- Malpaso (MPS) - cuenca mediana
INSERT INTO hydro_model.hui_coefficients (cuenca_code, hour_index, coefficient) VALUES
('MPS', 0, 0.08), ('MPS', 1, 0.18), ('MPS', 2, 0.25), ('MPS', 3, 0.22),
('MPS', 4, 0.15), ('MPS', 5, 0.08), ('MPS', 6, 0.04);

-- Penitas (PEA) - cuenca pequena
INSERT INTO hydro_model.hui_coefficients (cuenca_code, hour_index, coefficient) VALUES
('PEA', 0, 0.12), ('PEA', 1, 0.28), ('PEA', 2, 0.25), ('PEA', 3, 0.18),
('PEA', 4, 0.10), ('PEA', 5, 0.07);
"""

def main():
    print("Conectando a PostgreSQL...")
    conn = pg8000.connect(host=PG_HOST, port=PG_PORT, database=PG_DB, user=PG_USER, password=PG_PASS)
    conn.autocommit = True
    cur = conn.cursor()

    print("Creando esquema y tablas...")
    cur.execute(DDL)

    print("Cargando curvas elevation-capacity desde JSON...")
    script_dir = os.path.dirname(os.path.abspath(__file__))
    json_path = os.path.join(script_dir, 'Datos', 'funtabla_curves.json')
    with open(json_path, 'r') as f:
        data = json.load(f)

    total = 0
    for dam_name, records in data.items():
        # Usar COPY para carga masiva rápida
        import io
        buf = io.StringIO()
        for rec in records:
            area = rec.get('area_km2')
            sc = rec.get('specific_consumption')
            buf.write(f"{dam_name}\t{rec['elevation']}\t{rec['capacity_mm3']}\t{'\\N' if area is None else area}\t{'\\N' if sc is None else sc}\n")
        buf.seek(0)
        cur.execute("COPY hydro_model.elevation_capacity (dam_name, elevation, capacity_mm3, area_km2, specific_consumption) FROM STDIN", stream=buf)
        total += len(records)
        print(f"  {dam_name}: {len(records)} registros insertados")

    conn.close()
    print(f"\nTotal: {total} registros cargados")
    print("Tablas creadas: hydro_model.elevation_capacity, hydro_model.dam_params, hydro_model.hui_coefficients")

if __name__ == '__main__':
    main()
