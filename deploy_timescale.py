#!/usr/bin/env python3
"""
deploy_timescale.py
--------------------
Despliega el esquema completo de TimescaleDB/PostgreSQL para CloudStation PIH.
Equivalente Python de deploy_timescale_v2.sql — idempotente, puede re-ejecutarse.

Uso:
    python deploy_timescale.py                         # usa valores por defecto
    python deploy_timescale.py --host 192.168.1.10    # host personalizado
    python deploy_timescale.py --host atlas16.ddns.net --password MiClave

Requisitos:
    pip install psycopg2-binary
"""

import argparse
import configparser
import os
import sys

# ── Intentar importar psycopg2 con mensaje claro si no está instalado ──────────
try:
    import psycopg2
    from psycopg2.extensions import ISOLATION_LEVEL_AUTOCOMMIT
except ImportError:
    print("[ERROR] psycopg2 no instalado.")
    print("        Ejecute: pip install psycopg2-binary")
    sys.exit(1)

# ==============================================================================
# CONFIGURACIÓN — se lee desde config.ini [timescaledb]
# Los argumentos de línea de comandos tienen prioridad sobre config.ini
# ==============================================================================
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = os.path.join(BASE_DIR, "config.ini")
SQL_FILE    = os.path.join(BASE_DIR, "CloudStationWeb", "deploy_timescale_v2.sql")

def leer_config():
    """Lee config.ini y devuelve los valores de [timescaledb]."""
    cfg = configparser.ConfigParser()
    if os.path.exists(CONFIG_FILE):
        cfg.read(CONFIG_FILE, encoding="utf-8")
    sec = cfg["timescaledb"] if "timescaledb" in cfg else {}
    return {
        "host":     sec.get("host",     "atlas16.ddns.net"),
        "port":     int(sec.get("port", 5432)),
        "user":     sec.get("user",     "postgres"),
        "password": sec.get("password", ""),
        "db":       sec.get("database", "mycloud_timescale"),
    }


# ==============================================================================
# HELPERS
# ==============================================================================
def ok(msg):  print(f"   \033[32m✔\033[0m {msg}")
def warn(msg): print(f"   \033[33m⚠\033[0m {msg}")
def err(msg):  print(f"   \033[31m✘\033[0m {msg}")
def step(n, msg): print(f"\n[{n}] {msg}...")


def crear_base_si_no_existe(host, port, user, password, db_name):
    """Conecta a 'postgres' y crea la BD destino si no existe."""
    conn = psycopg2.connect(
        host=host, port=port, user=user, password=password, dbname="postgres"
    )
    conn.set_isolation_level(ISOLATION_LEVEL_AUTOCOMMIT)
    cur = conn.cursor()
    cur.execute("SELECT 1 FROM pg_database WHERE datname = %s", (db_name,))
    existe = cur.fetchone()
    if not existe:
        cur.execute(f'CREATE DATABASE "{db_name}"')
        ok(f"Base de datos '{db_name}' creada")
    else:
        ok(f"Base de datos '{db_name}' ya existe")
    cur.close()
    conn.close()


def ejecutar_sql(conn, sql_text, descripcion=""):
    """Ejecuta un bloque SQL e imprime el resultado."""
    cur = conn.cursor()
    try:
        cur.execute(sql_text)
        # Mostrar mensajes NOTICE de PostgreSQL
        if conn.notices:
            for notice in conn.notices:
                print(f"        {notice.strip()}")
            conn.notices.clear()
        ok(descripcion or "OK")
    except psycopg2.Error as e:
        err(f"{descripcion}: {e.pgerror or str(e)}")
        raise
    finally:
        cur.close()


def verificar_tablas(conn):
    """Muestra conteo de registros por tabla al final."""
    tablas = [
        "bitacora_goes", "dcp_headers", "dcp_datos",
        "resumen_horario", "resumen_diario", "lluvia_acumulada",
        "pronostico_lluvia", "funvasos_horario", "ultimas_mediciones",
        "estatus_estaciones", "alertas_precipitacion", "eventos_lluvia",
        "precipitacion_cuenca", "bhg_presa_diario", "bhg_estacion_diario",
        "bhg_archivo",
    ]
    cur = conn.cursor()
    print()
    print("   {:<30} {:>10}".format("Tabla", "Registros"))
    print("   " + "-" * 42)
    for tabla in tablas:
        try:
            cur.execute(f"SELECT COUNT(*) FROM public.{tabla}")
            count = cur.fetchone()[0]
            estado = f"{count:>10,}"
        except psycopg2.Error:
            estado = "   (no existe)"
        print(f"   {tabla:<30} {estado}")
    cur.close()


# ==============================================================================
# MAIN
# ==============================================================================
def main():
    cfg = leer_config()

    parser = argparse.ArgumentParser(
        description="Despliega esquema TimescaleDB para CloudStation PIH"
    )
    parser.add_argument("--host",     default=cfg["host"])
    parser.add_argument("--port",     default=cfg["port"], type=int)
    parser.add_argument("--user",     default=cfg["user"])
    parser.add_argument("--password", default=cfg["password"])
    parser.add_argument("--db",       default=cfg["db"],
                        help="Nombre de la base de datos")
    parser.add_argument("--sql-file", default=SQL_FILE,
                        help="Ruta al archivo .sql")
    args = parser.parse_args()

    print("=" * 60)
    print("  CLOUDSTATION PIH — Deploy TimescaleDB")
    print(f"  Config: {CONFIG_FILE}")
    print(f"  Host : {args.host}:{args.port}")
    print(f"  BD   : {args.db}")
    print(f"  SQL  : {args.sql_file}")
    print("=" * 60)

    # 1. Verificar que el archivo SQL existe
    step(1, "Verificando archivo SQL")
    if not os.path.exists(args.sql_file):
        err(f"No se encontró: {args.sql_file}")
        sys.exit(1)
    ok(f"Archivo encontrado ({os.path.getsize(args.sql_file):,} bytes)")

    # 2. Crear base de datos si no existe
    step(2, f"Verificando base de datos '{args.db}'")
    try:
        crear_base_si_no_existe(args.host, args.port, args.user, args.password, args.db)
    except psycopg2.OperationalError as e:
        err(f"No se puede conectar a {args.host}:{args.port} — {e}")
        sys.exit(1)

    # 3. Conectar a la BD destino
    step(3, "Conectando a la base de datos")
    try:
        conn = psycopg2.connect(
            host=args.host, port=args.port,
            user=args.user, password=args.password,
            dbname=args.db
        )
        conn.set_isolation_level(ISOLATION_LEVEL_AUTOCOMMIT)
        ok(f"Conectado a {args.host}/{args.db}")
    except psycopg2.OperationalError as e:
        err(f"Fallo al conectar: {e}")
        sys.exit(1)

    # 4. Leer el archivo SQL
    step(4, "Leyendo deploy_timescale_v2.sql")
    with open(args.sql_file, "r", encoding="utf-8") as f:
        sql_completo = f.read()
    ok(f"{len(sql_completo):,} caracteres leídos")

    # 5. Ejecutar el script completo
    # PostgreSQL acepta el script entero en una sola llamada con autocommit.
    # Los bloques DO $$ ... $$ y CREATE ... IF NOT EXISTS son idempotentes.
    step(5, "Ejecutando script SQL (puede tardar ~10 seg en servidor nuevo)")
    try:
        cur = conn.cursor()
        cur.execute(sql_completo)
        # Imprimir NOTICEs (RAISE NOTICE de los DO $$ ... $$ blocks)
        if conn.notices:
            for notice in conn.notices:
                line = notice.strip().replace("NOTICE:  ", "")
                print(f"        → {line}")
            conn.notices.clear()
        cur.close()
        ok("Script ejecutado sin errores")
    except psycopg2.Error as e:
        err(f"Error al ejecutar SQL: {e.pgerror or str(e)}")
        conn.close()
        sys.exit(1)

    # 6. Verificar tablas creadas
    step(6, "Verificando tablas")
    verificar_tablas(conn)

    conn.close()

    print()
    print("=" * 60)
    print("  DEPLOY COMPLETADO")
    print()
    print("  Siguientes pasos:")
    print("  1. Ajustar appsettings.json con la cadena de conexión")
    print("  2. Ejecutar deploy_produccion.py (SQL Server)")
    print("  3. Lanzar python mycloud_all_timescale.py")
    print("=" * 60)


if __name__ == "__main__":
    main()
