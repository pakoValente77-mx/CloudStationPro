#!/usr/bin/env python3
"""
transfer_datos.py — Transfiere datos de dcp_datos entre dos PostgreSQL.

Copia datos crudos + recalcula resúmenes horarios y diarios en destino.

Ejemplos:
  # Transferir de atlas16 (pruebas) a producción (IP del servidor Windows)
  python transfer_datos.py \
    --source-host atlas16.ddns.net --source-pass ***REDACTED-PG-PASSWORD*** \
    --dest-host 192.168.1.72 --dest-pass Cfe2026## \
    --from "2026-03-29 15:00" --to "2026-03-29 18:00"

  # Ejecutar en el propio servidor Windows (destino = localhost)
  python transfer_datos.py \
    --source-host atlas16.ddns.net --source-pass ***REDACTED-PG-PASSWORD*** \
    --dest-host localhost --dest-pass Cfe2026## \
    --from "2026-03-29 15:00" --to "2026-03-29 18:00"

  # Solo una estación
  python transfer_datos.py \
    --source-host atlas16.ddns.net --source-pass ***REDACTED-PG-PASSWORD*** \
    --dest-host 192.168.1.72 --dest-pass Cfe2026## \
    --from "2026-03-29 15:00" --to "2026-03-29 18:00" \
    --station E891DCA2

  # Dry-run (solo mostrar qué se transferiría)
  python transfer_datos.py \
    --source-host atlas16.ddns.net --source-pass ***REDACTED-PG-PASSWORD*** \
    --dest-host 192.168.1.72 --dest-pass Cfe2026## \
    --from "2026-03-29 15:00" --to "2026-03-29 18:00" \
    --dry-run
"""

import argparse
import psycopg2
from psycopg2.extras import execute_values
import datetime
import sys


def parse_args():
    p = argparse.ArgumentParser(
        description="Transferir datos dcp_datos entre dos PostgreSQL y recalcular resúmenes",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    # Origen
    p.add_argument("--source-host", required=True, help="Host PostgreSQL origen")
    p.add_argument("--source-port", type=int, default=5432)
    p.add_argument("--source-user", default="postgres")
    p.add_argument("--source-pass", required=True, help="Contraseña PG origen")
    p.add_argument("--source-db", default="mycloud_timescale")

    # Destino
    p.add_argument("--dest-host", required=True, help="Host PostgreSQL destino")
    p.add_argument("--dest-port", type=int, default=5432)
    p.add_argument("--dest-user", default="postgres")
    p.add_argument("--dest-pass", required=True, help="Contraseña PG destino")
    p.add_argument("--dest-db", default="mycloud_timescale")

    # Rango
    p.add_argument("--from", dest="ts_from", required=True,
                   help="Inicio (hora LOCAL): '2026-03-29 15:00'")
    p.add_argument("--to", dest="ts_to", required=True,
                   help="Fin (hora LOCAL): '2026-03-29 18:00'")
    p.add_argument("--station", "-s", default=None, help="DCP ID específico (opcional)")
    p.add_argument("--dry-run", action="store_true", help="Solo mostrar qué se transferiría")
    p.add_argument("--batch-size", type=int, default=2000, help="Registros por batch INSERT (default: 2000)")

    return p.parse_args()


def connect(host, port, user, password, database, label):
    try:
        conn = psycopg2.connect(host=host, port=port, user=user,
                                password=password, database=database)
        print(f"  [{label}] Conectado a {host}:{port}/{database}")
        return conn
    except Exception as e:
        print(f"  [ERROR] No se pudo conectar a {label} ({host}): {e}")
        sys.exit(1)


def main():
    args = parse_args()

    print("=" * 70)
    print("  TRANSFERENCIA DE DATOS dcp_datos")
    print("=" * 70)

    src = connect(args.source_host, args.source_port, args.source_user,
                  args.source_pass, args.source_db, "ORIGEN")
    dest = connect(args.dest_host, args.dest_port, args.dest_user,
                   args.dest_pass, args.dest_db, "DESTINO")

    src_cur = src.cursor()
    dest_cur = dest.cursor()

    station_filter = ""
    if args.station:
        station_filter = f"AND dcp_id = '{args.station.upper()}'"

    # ── 1) Contar en origen ─────────────────────────────────────────
    src_cur.execute(f"""
        SELECT COUNT(*), COUNT(DISTINCT dcp_id)
        FROM dcp_datos
        WHERE ts >= %s AND ts < %s {station_filter}
    """, (args.ts_from, args.ts_to))
    total_src, estaciones_src = src_cur.fetchone()

    # ── 2) Contar en destino (ya existentes) ────────────────────────
    dest_cur.execute(f"""
        SELECT COUNT(*)
        FROM dcp_datos
        WHERE ts >= %s AND ts < %s {station_filter}
    """, (args.ts_from, args.ts_to))
    ya_en_destino = dest_cur.fetchone()[0]

    print(f"\n  Rango            : {args.ts_from} → {args.ts_to} (hora local)")
    if args.station:
        print(f"  Estación         : {args.station.upper()}")
    print(f"  Registros origen : {total_src:,} ({estaciones_src} estaciones)")
    print(f"  Ya en destino    : {ya_en_destino:,}")
    print(f"  Nuevos estimados : ~{max(0, total_src - ya_en_destino):,}")
    print()

    if total_src == 0:
        print("[WARN] No hay datos en el origen para ese rango.")
        return

    if args.dry_run:
        print("[DRY-RUN] No se transfirieron datos.")
        src.close()
        dest.close()
        return

    # ── 3) Leer datos del origen ────────────────────────────────────
    print("[1/3] Leyendo datos del origen...")
    src_cur.execute(f"""
        SELECT ts, dcp_id, sensor_id, id_asignado, variable, valor, tipo,
               valido, descripcion, anio, mes, dia, hora, minuto
        FROM dcp_datos
        WHERE ts >= %s AND ts < %s {station_filter}
        ORDER BY ts
    """, (args.ts_from, args.ts_to))

    rows = src_cur.fetchall()
    print(f"  {len(rows):,} registros leídos")

    # ── 4) Insertar en destino ──────────────────────────────────────
    print("[2/3] Insertando en destino (ON CONFLICT DO NOTHING)...")
    insertados = 0
    duplicados = 0

    for i in range(0, len(rows), args.batch_size):
        batch = rows[i:i + args.batch_size]
        try:
            execute_values(dest_cur, """
                INSERT INTO dcp_datos
                (ts, dcp_id, sensor_id, id_asignado, variable, valor, tipo,
                 valido, descripcion, anio, mes, dia, hora, minuto)
                VALUES %s
                ON CONFLICT (ts, dcp_id, sensor_id) DO NOTHING
            """, batch, page_size=500)
            new = dest_cur.rowcount
            insertados += new
            duplicados += len(batch) - new
        except Exception as e:
            dest.rollback()
            print(f"  [ERROR] Batch {i}: {e}")
            continue

    dest.commit()
    print(f"  Insertados: {insertados:,} nuevos, {duplicados:,} duplicados ignorados")

    # ── 5) Recalcular resúmenes ─────────────────────────────────────
    if insertados > 0:
        print("[3/3] Recalculando resúmenes horarios y diarios...")
        recalcular_resumenes(dest, dest_cur, args.ts_from, args.ts_to, station_filter)
    else:
        print("[3/3] Sin datos nuevos, no se recalculan resúmenes.")

    # ── Resumen final ───────────────────────────────────────────────
    print(f"\n{'='*70}")
    print(f"  TRANSFERENCIA COMPLETADA")
    print(f"{'='*70}")
    print(f"  Registros transferidos : {insertados:,}")
    print(f"  Duplicados ignorados   : {duplicados:,}")
    print("=" * 70)

    src.close()
    dest.close()


def recalcular_resumenes(conn, cur, ts_from, ts_to, station_filter):
    # Obtener combinaciones únicas para recalcular
    cur.execute(f"""
        SELECT DISTINCT dcp_id, sensor_id, id_asignado, variable, tipo,
               date_trunc('hour', ts - INTERVAL '1 minute') as hora
        FROM dcp_datos
        WHERE ts >= %s AND ts < %s AND valido = true {station_filter}
    """, (ts_from, ts_to))
    combos_horario = cur.fetchall()

    ok_h = 0
    err_h = 0
    for dcp_id, sensor_id, id_asignado, variable, tipo, hora in combos_horario:
        try:
            cur.execute("SAVEPOINT sp_rh")
            hora_fin = hora + datetime.timedelta(hours=1)
            cur.execute("""
                INSERT INTO resumen_horario
                (ts, dcp_id, sensor_id, id_asignado, variable, tipo,
                 suma, conteo, promedio, minimo, maximo, acumulado)
                SELECT
                    date_trunc('hour', ts - INTERVAL '1 minute'),
                    dcp_id, sensor_id, id_asignado, variable, tipo,
                    SUM(valor), COUNT(*), AVG(valor), MIN(valor), MAX(valor), SUM(valor)
                FROM dcp_datos
                WHERE id_asignado = %s AND sensor_id = %s
                  AND ts > %s AND ts <= %s AND valido = true
                GROUP BY date_trunc('hour', ts - INTERVAL '1 minute'),
                         dcp_id, sensor_id, id_asignado, variable, tipo
                ON CONFLICT (ts, dcp_id, sensor_id) DO UPDATE SET
                    suma = EXCLUDED.suma, conteo = EXCLUDED.conteo,
                    promedio = EXCLUDED.promedio, minimo = EXCLUDED.minimo,
                    maximo = EXCLUDED.maximo, acumulado = EXCLUDED.acumulado
            """, (id_asignado, sensor_id, hora, hora_fin))
            cur.execute("RELEASE SAVEPOINT sp_rh")
            ok_h += 1
        except Exception as e:
            cur.execute("ROLLBACK TO SAVEPOINT sp_rh")
            err_h += 1

    conn.commit()
    print(f"  Resúmenes horarios: {ok_h} OK, {err_h} errores")

    # Diarios
    cur.execute(f"""
        SELECT DISTINCT dcp_id, sensor_id, id_asignado, variable, tipo,
               DATE(ts - INTERVAL '1 minute') as fecha
        FROM dcp_datos
        WHERE ts >= %s AND ts < %s AND valido = true {station_filter}
    """, (ts_from, ts_to))
    combos_diario = cur.fetchall()

    ok_d = 0
    err_d = 0
    for dcp_id, sensor_id, id_asignado, variable, tipo, fecha in combos_diario:
        try:
            cur.execute("SAVEPOINT sp_rd")
            cur.execute("""
                INSERT INTO resumen_diario
                (fecha, dcp_id, sensor_id, id_asignado, variable, tipo,
                 suma, conteo, promedio, minimo, maximo, acumulado)
                SELECT
                    DATE(ts - INTERVAL '1 minute'),
                    dcp_id, sensor_id, id_asignado, variable, tipo,
                    SUM(valor), COUNT(*), AVG(valor), MIN(valor), MAX(valor), SUM(valor)
                FROM dcp_datos
                WHERE id_asignado = %s AND sensor_id = %s
                  AND DATE(ts - INTERVAL '1 minute') = %s AND valido = true
                GROUP BY DATE(ts - INTERVAL '1 minute'),
                         dcp_id, sensor_id, id_asignado, variable, tipo
                ON CONFLICT (fecha, dcp_id, sensor_id) DO UPDATE SET
                    suma = EXCLUDED.suma, conteo = EXCLUDED.conteo,
                    promedio = EXCLUDED.promedio, minimo = EXCLUDED.minimo,
                    maximo = EXCLUDED.maximo, acumulado = EXCLUDED.acumulado
            """, (id_asignado, sensor_id, fecha))
            cur.execute("RELEASE SAVEPOINT sp_rd")
            ok_d += 1
        except Exception as e:
            cur.execute("ROLLBACK TO SAVEPOINT sp_rd")
            err_d += 1

    conn.commit()
    print(f"  Resúmenes diarios: {ok_d} OK, {err_d} errores")


if __name__ == "__main__":
    main()
