#!/usr/bin/env python3
"""
CLOUDSTATION PIH - Script de migración PostgreSQL/TimescaleDB
Fecha: 2026-04-01
Descripción: Aplica cambios incrementales a la base mycloud_timescale

Cambios incluidos:
  1. dcp_headers: columna raw_message (TEXT) para exportación MIS
  
Uso:
  python deploy_timescale_v2.py
"""

import configparser
import os
import sys

try:
    import psycopg2
except ImportError:
    print("[ERROR] psycopg2 no instalado. Ejecutar: pip install psycopg2-binary")
    sys.exit(1)


def get_pg_config():
    """Lee config.ini para obtener credenciales de TimescaleDB."""
    config = configparser.ConfigParser()
    config_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.ini")
    if not os.path.exists(config_path):
        print(f"[ERROR] No se encontró {config_path}")
        sys.exit(1)
    config.read(config_path)
    return {
        "host": config.get("timescaledb", "host"),
        "port": config.getint("timescaledb", "port"),
        "user": config.get("timescaledb", "user"),
        "password": config.get("timescaledb", "password"),
        "database": config.get("timescaledb", "database"),
    }


def run_migration(conn):
    """Ejecuta las migraciones incrementales."""
    cur = conn.cursor()
    changes = 0

    # =====================================================================
    # 1. dcp_headers: agregar columna raw_message si no existe
    #    Almacena el mensaje DOMSAT completo (header + payload) 
    #    para reconstruir footer en exportación MIS
    # =====================================================================
    print("[1] dcp_headers.raw_message ...")
    cur.execute("""
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' 
          AND table_name = 'dcp_headers' 
          AND column_name = 'raw_message'
    """)
    if cur.fetchone() is None:
        cur.execute("ALTER TABLE public.dcp_headers ADD COLUMN raw_message TEXT")
        conn.commit()
        print("    + Columna raw_message AGREGADA a dcp_headers")
        changes += 1
    else:
        print("    = raw_message ya existe")

    # =====================================================================
    # Verificación
    # =====================================================================
    print("\n[*] Verificación de estructura dcp_headers:")
    cur.execute("""
        SELECT column_name, data_type, is_nullable
        FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'dcp_headers'
        ORDER BY ordinal_position
    """)
    for row in cur.fetchall():
        print(f"    {row[0]:25s} {row[1]:20s} nullable={row[2]}")

    cur.execute("SELECT COUNT(*) FROM public.dcp_headers")
    total = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM public.dcp_headers WHERE raw_message IS NOT NULL")
    con_raw = cur.fetchone()[0]
    print(f"\n    Total registros: {total}")
    print(f"    Con raw_message: {con_raw}")

    cur.close()
    return changes


def main():
    print("=" * 60)
    print(" CLOUDSTATION PIH - Migración TimescaleDB")
    print(" Fecha: 2026-04-01")
    print("=" * 60)
    print()

    pg = get_pg_config()
    print(f"[*] Conectando a {pg['host']}:{pg['port']}/{pg['database']} ...")

    try:
        conn = psycopg2.connect(
            host=pg["host"],
            port=pg["port"],
            user=pg["user"],
            password=pg["password"],
            dbname=pg["database"],
        )
        conn.autocommit = False
        print("    Conexión OK\n")
    except Exception as e:
        print(f"[ERROR] No se pudo conectar: {e}")
        sys.exit(1)

    try:
        changes = run_migration(conn)
        conn.commit()
        print(f"\n{'=' * 60}")
        print(f" Migración completada: {changes} cambio(s) aplicado(s)")
        print(f"{'=' * 60}")
    except Exception as e:
        conn.rollback()
        print(f"\n[ERROR] Migración fallida, rollback ejecutado: {e}")
        sys.exit(1)
    finally:
        conn.close()


if __name__ == "__main__":
    main()
