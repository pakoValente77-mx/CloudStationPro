"""
Migración v2: Enriquecer hydro_model.dam_params con columnas faltantes
y crear hydro_model.central_params para metadata de centrales CFE.

Esto permite eliminar los diccionarios hardcodeados en StationsApiController.cs.

Uso: python setup_hydro_params_v2.py
"""
import pg8000

PG_HOST = "atlas16.ddns.net"
PG_PORT = 5432
PG_DB = "mycloud_timescale"
PG_USER = "postgres"
PG_PASS = "***REDACTED-PG-PASSWORD***"

MIGRATION_SQL = """
-- =====================================================================
-- 1) Crear tabla central_params (metadata CFE de centrales)
-- =====================================================================
CREATE TABLE IF NOT EXISTS hydro_model.central_params (
    id INT PRIMARY KEY,
    previous_central_id INT,
    id_cuenca INT NOT NULL DEFAULT 1,
    id_subcuenca INT NOT NULL,
    clave20 VARCHAR(10) NOT NULL,
    clave_cenace VARCHAR(10) NOT NULL,
    clave_sap VARCHAR(10) NOT NULL,
    nombre VARCHAR(100) NOT NULL,
    unidades INT NOT NULL DEFAULT 0,
    capacidad_instalada INT NOT NULL DEFAULT 0,
    consumo_especifico DOUBLE PRECISION NOT NULL DEFAULT 0,
    latitud DOUBLE PRECISION,
    longitud DOUBLE PRECISION,
    orden INT NOT NULL
);

-- Seed: 5 centrales de la cascada Grijalva
INSERT INTO hydro_model.central_params
    (id, previous_central_id, id_cuenca, id_subcuenca, clave20, clave_cenace, clave_sap, nombre, unidades, capacidad_instalada, consumo_especifico, latitud, longitud, orden)
VALUES
    (1, NULL, 1, 1, 'ANG', 'K02', 'ANG', 'C.H. Angostura',       5, 900,  4.1,  16.848, -93.535, 1),
    (2, 1,    1, 2, 'CHI', 'K03', 'CHI', 'C.H. Chicoasén',       8, 2400, 3.25, 16.933, -93.148, 2),
    (3, 2,    1, 3, 'MAL', 'K05', 'MAL', 'C.H. Malpaso',         6, 1080, 4.6,  17.163, -93.580, 3),
    (4, 3,    1, 4, 'JGR', 'K18', 'JGR', 'C.H. Juan Grijalva',   0, 0,    0,    17.208, -93.510, 4),
    (5, 4,    1, 5, 'PEN', 'K04', 'PEN', 'C.H. Peñitas',         4, 420,  4.8,  17.369, -93.530, 5)
ON CONFLICT (id) DO UPDATE SET
    previous_central_id = EXCLUDED.previous_central_id,
    id_cuenca = EXCLUDED.id_cuenca,
    id_subcuenca = EXCLUDED.id_subcuenca,
    clave20 = EXCLUDED.clave20,
    clave_cenace = EXCLUDED.clave_cenace,
    clave_sap = EXCLUDED.clave_sap,
    nombre = EXCLUDED.nombre,
    unidades = EXCLUDED.unidades,
    capacidad_instalada = EXCLUDED.capacidad_instalada,
    consumo_especifico = EXCLUDED.consumo_especifico,
    latitud = EXCLUDED.latitud,
    longitud = EXCLUDED.longitud,
    orden = EXCLUDED.orden;

-- =====================================================================
-- 2) Enriquecer dam_params con columnas de presa (NAME/NAMO/volúmenes)
-- =====================================================================
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS code VARCHAR(10);
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS description VARCHAR(100);
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS name_value REAL DEFAULT 0;
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS namo_value REAL DEFAULT 0;
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS namino_value INT DEFAULT 0;
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS useful_volume REAL DEFAULT 0;
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS total_volume REAL DEFAULT 0;
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS input_area REAL DEFAULT 0;

-- Seed valores de presas
UPDATE hydro_model.dam_params SET code='ANG', description='Angostura',            name_value=542.10, namo_value=539.00, namino_value=510, useful_volume=11115, off_volume=6554,  total_volume=17669, input_area=22000 WHERE dam_name='Angostura';
UPDATE hydro_model.dam_params SET code='CHI', description='Chicoasén',            name_value=400.00, namo_value=395.00, namino_value=378, useful_volume=1194,  off_volume=383,   total_volume=1577,  input_area=574   WHERE dam_name='Chicoasen';
UPDATE hydro_model.dam_params SET code='MAL', description='Malpaso',              name_value=192.00, namo_value=189.70, namino_value=163, useful_volume=8641,  off_volume=4862,  total_volume=13503, input_area=32854 WHERE dam_name='Malpaso';
UPDATE hydro_model.dam_params SET code='JGR', description='Tapón Juan Grijalva',  name_value=105.50, namo_value=100.00, namino_value=87,  useful_volume=0,     off_volume=0,     total_volume=1666,  input_area=0     WHERE dam_name='JGrijalva';
UPDATE hydro_model.dam_params SET code='PEN', description='Peñitas',              name_value=99.20,  namo_value=95.10,  namino_value=84,  useful_volume=804,   off_volume=467,   total_volume=1271,  input_area=1868  WHERE dam_name='Penitas';

-- =====================================================================
-- 3) Agregar columnas de subcuenca a dam_params
-- =====================================================================
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS sub_basin_code VARCHAR(10);
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS sub_basin_name VARCHAR(100);
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS input_factor REAL DEFAULT 0;
ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS hours_read INT[] DEFAULT '{6,12,18,24}';

UPDATE hydro_model.dam_params SET sub_basin_code='ANG', sub_basin_name='Angostura',           input_factor=0.15, transfer_time_hours=0  WHERE dam_name='Angostura';
UPDATE hydro_model.dam_params SET sub_basin_code='MMT', sub_basin_name='Medio Mezcalapa',     input_factor=0.30, transfer_time_hours=2  WHERE dam_name='Chicoasen';
UPDATE hydro_model.dam_params SET sub_basin_code='MPS', sub_basin_name='Medio-Bajo Grijalva', input_factor=0.15, transfer_time_hours=4  WHERE dam_name='Malpaso';
UPDATE hydro_model.dam_params SET sub_basin_code='JGR', sub_basin_name='Juan Grijalva',       input_factor=0.10, transfer_time_hours=2  WHERE dam_name='JGrijalva';
UPDATE hydro_model.dam_params SET sub_basin_code='PEA', sub_basin_name='Peñitas',             input_factor=0.20, transfer_time_hours=2  WHERE dam_name='Penitas';
"""

def main():
    print("Conectando a PostgreSQL...")
    conn = pg8000.connect(host=PG_HOST, port=PG_PORT, database=PG_DB, user=PG_USER, password=PG_PASS)
    conn.autocommit = True
    cur = conn.cursor()

    print("Ejecutando migración v2...")

    # 1) Crear tabla central_params
    print("  1) Creando central_params...")
    cur.execute("""
        CREATE TABLE IF NOT EXISTS hydro_model.central_params (
            id INT PRIMARY KEY,
            previous_central_id INT,
            id_cuenca INT NOT NULL DEFAULT 1,
            id_subcuenca INT NOT NULL,
            clave20 VARCHAR(10) NOT NULL,
            clave_cenace VARCHAR(10) NOT NULL,
            clave_sap VARCHAR(10) NOT NULL,
            nombre VARCHAR(100) NOT NULL,
            unidades INT NOT NULL DEFAULT 0,
            capacidad_instalada INT NOT NULL DEFAULT 0,
            consumo_especifico DOUBLE PRECISION NOT NULL DEFAULT 0,
            latitud DOUBLE PRECISION,
            longitud DOUBLE PRECISION,
            orden INT NOT NULL
        )
    """)

    cur.execute("""
        INSERT INTO hydro_model.central_params
            (id, previous_central_id, id_cuenca, id_subcuenca, clave20, clave_cenace, clave_sap, nombre, unidades, capacidad_instalada, consumo_especifico, latitud, longitud, orden)
        VALUES
            (1, NULL, 1, 1, 'ANG', 'K02', 'ANG', 'C.H. Angostura',       5, 900,  4.1,  16.848, -93.535, 1),
            (2, 1,    1, 2, 'CHI', 'K03', 'CHI', 'C.H. Chicoasén',       8, 2400, 3.25, 16.933, -93.148, 2),
            (3, 2,    1, 3, 'MAL', 'K05', 'MAL', 'C.H. Malpaso',         6, 1080, 4.6,  17.163, -93.580, 3),
            (4, 3,    1, 4, 'JGR', 'K18', 'JGR', 'C.H. Juan Grijalva',   0, 0,    0,    17.208, -93.510, 4),
            (5, 4,    1, 5, 'PEN', 'K04', 'PEN', 'C.H. Peñitas',         4, 420,  4.8,  17.369, -93.530, 5)
        ON CONFLICT (id) DO UPDATE SET
            previous_central_id = EXCLUDED.previous_central_id,
            id_cuenca = EXCLUDED.id_cuenca,
            id_subcuenca = EXCLUDED.id_subcuenca,
            clave20 = EXCLUDED.clave20,
            clave_cenace = EXCLUDED.clave_cenace,
            clave_sap = EXCLUDED.clave_sap,
            nombre = EXCLUDED.nombre,
            unidades = EXCLUDED.unidades,
            capacidad_instalada = EXCLUDED.capacidad_instalada,
            consumo_especifico = EXCLUDED.consumo_especifico,
            latitud = EXCLUDED.latitud,
            longitud = EXCLUDED.longitud,
            orden = EXCLUDED.orden
    """)

    # 2) Enriquecer dam_params con columnas de presa
    print("  2) Enriqueciendo dam_params...")
    for col_def in [
        "code VARCHAR(10)",
        "description VARCHAR(100)",
        "name_value REAL DEFAULT 0",
        "namo_value REAL DEFAULT 0",
        "namino_value INT DEFAULT 0",
        "useful_volume REAL DEFAULT 0",
        "total_volume REAL DEFAULT 0",
        "input_area REAL DEFAULT 0",
        "sub_basin_code VARCHAR(10)",
        "sub_basin_name VARCHAR(100)",
        "input_factor REAL DEFAULT 0",
        "hours_read INT[] DEFAULT '{6,12,18,24}'",
    ]:
        try:
            cur.execute(f"ALTER TABLE hydro_model.dam_params ADD COLUMN IF NOT EXISTS {col_def}")
        except Exception as e:
            print(f"    WARN adding column {col_def}: {e}")

    # Seed dam values
    dam_updates = [
        ("Angostura",  "ANG", "Angostura",            542.10, 539.00, 510, 11115, 6554,  17669, 22000, "ANG", "Angostura",           0.15, 0),
        ("Chicoasen",  "CHI", "Chicoasén",            400.00, 395.00, 378, 1194,  383,   1577,  574,   "MMT", "Medio Mezcalapa",     0.30, 2),
        ("Malpaso",    "MAL", "Malpaso",              192.00, 189.70, 163, 8641,  4862,  13503, 32854, "MPS", "Medio-Bajo Grijalva", 0.15, 4),
        ("JGrijalva",  "JGR", "Tapón Juan Grijalva",  105.50, 100.00, 87,  0,     0,     1666,  0,     "JGR", "Juan Grijalva",       0.10, 2),
        ("Penitas",    "PEN", "Peñitas",              99.20,  95.10,  84,  804,   467,   1271,  1868,  "PEA", "Peñitas",             0.20, 2),
    ]
    for dam_name, code, desc, name_v, namo_v, namino_v, useful_v, off_v, total_v, area, sb_code, sb_name, inp_factor, xfer_time in dam_updates:
        cur.execute("""
            UPDATE hydro_model.dam_params SET
                code=%s, description=%s,
                name_value=%s, namo_value=%s, namino_value=%s,
                useful_volume=%s, off_volume=%s, total_volume=%s, input_area=%s,
                sub_basin_code=%s, sub_basin_name=%s,
                input_factor=%s, transfer_time_hours=%s
            WHERE dam_name=%s
        """, (code, desc, name_v, namo_v, namino_v, useful_v, off_v, total_v, area,
              sb_code, sb_name, inp_factor, xfer_time, dam_name))

    # Verify
    cur.execute("SELECT id, nombre, clave_cenace, capacidad_instalada FROM hydro_model.central_params ORDER BY id")
    rows = cur.fetchall()
    print(f"\ncentral_params: {len(rows)} registros")
    for r in rows:
        print(f"  [{r[0]}] {r[1]} — CENACE: {r[2]}, Cap: {r[3]} MW")

    cur.execute("SELECT dam_name, code, description, name_value, namo_value, total_volume, sub_basin_code FROM hydro_model.dam_params ORDER BY cascade_order")
    rows = cur.fetchall()
    print(f"\ndam_params (enriquecido): {len(rows)} registros")
    for r in rows:
        print(f"  {r[0]}: code={r[1]}, desc={r[2]}, NAME={r[3]}, NAMO={r[4]}, Vol={r[5]}, SubBasin={r[6]}")

    conn.close()
    print("\nMigración completada exitosamente.")

if __name__ == "__main__":
    main()
