#!/usr/bin/env python3
"""
recalcular_resumenes.py - Recalcula resúmenes horarios y diarios faltantes.

Uso:
  python recalcular_resumenes.py                    # Últimas 24 horas (default)
  python recalcular_resumenes.py --hours 48         # Últimas 48 horas
  python recalcular_resumenes.py --station E891DCA2 # Solo una estación
  python recalcular_resumenes.py --dry-run          # Solo mostrar qué falta
"""
import argparse
import os
import sys
import configparser
import psycopg2
import datetime

_base_dir = os.path.dirname(os.path.abspath(__file__))
config = configparser.ConfigParser()
config.read(os.path.join(_base_dir, 'config.ini'))

def get_conn():
    return psycopg2.connect(
        host=config.get('timescaledb', 'host'),
        port=config.getint('timescaledb', 'port', fallback=5432),
        user=config.get('timescaledb', 'user'),
        password=config.get('timescaledb', 'password'),
        database=config.get('timescaledb', 'database'),
    )


def recalcular_horario(conn, hours, station_filter=None, dry_run=False):
    cur = conn.cursor()

    # Buscar horas con datos crudos pero sin resumen (o resumen incompleto)
    where_station = f"AND d.dcp_id = '{station_filter}'" if station_filter else ""

    cur.execute(f"""
        SELECT d.dcp_id, d.sensor_id, d.id_asignado, d.variable, d.tipo,
               date_trunc('hour', d.ts - INTERVAL '1 minute') as hora
        FROM dcp_datos d
        WHERE d.ts > NOW() - INTERVAL '{hours} hours'
          AND d.valido = true
          {where_station}
        GROUP BY d.dcp_id, d.sensor_id, d.id_asignado, d.variable, d.tipo,
                 date_trunc('hour', d.ts - INTERVAL '1 minute')
        EXCEPT
        SELECT r.dcp_id, r.sensor_id, r.id_asignado, r.variable, r.tipo, r.ts
        FROM resumen_horario r
        WHERE r.ts > NOW() - INTERVAL '{hours} hours'
        ORDER BY 6, 1, 2
    """)
    faltantes = cur.fetchall()

    if not faltantes:
        print("[OK] No hay resúmenes horarios faltantes.")
        return 0

    print(f"[HORARIO] {len(faltantes)} resúmenes horarios faltantes encontrados")

    if dry_run:
        # Agrupar por hora para mostrar resumen
        horas = {}
        for r in faltantes:
            h = str(r[5])
            horas[h] = horas.get(h, 0) + 1
        for h in sorted(horas.keys()):
            print(f"  {h}: {horas[h]} sensores sin resumen")
        return len(faltantes)

    # Recalcular en batch: INSERT con SAVEPOINT por cada combinación
    insertados = 0
    errores = 0
    for dcp_id, sensor_id, id_asignado, variable, tipo, hora in faltantes:
        try:
            cur.execute("SAVEPOINT sp_rh")
            hora_inicio = hora
            hora_fin = hora + datetime.timedelta(hours=1)

            cur.execute("""
                INSERT INTO resumen_horario
                (ts, dcp_id, sensor_id, id_asignado, variable, tipo,
                 suma, conteo, promedio, minimo, maximo, acumulado)
                SELECT
                    date_trunc('hour', ts - INTERVAL '1 minute') AS hora,
                    dcp_id, sensor_id, id_asignado, variable, tipo,
                    SUM(valor), COUNT(*), AVG(valor), MIN(valor), MAX(valor), SUM(valor)
                FROM dcp_datos
                WHERE id_asignado = %s AND sensor_id = %s
                  AND ts > %s AND ts <= %s
                  AND valido = true
                GROUP BY date_trunc('hour', ts - INTERVAL '1 minute'),
                         dcp_id, sensor_id, id_asignado, variable, tipo
                ON CONFLICT (ts, dcp_id, sensor_id) DO UPDATE SET
                    suma = EXCLUDED.suma,
                    conteo = EXCLUDED.conteo,
                    promedio = EXCLUDED.promedio,
                    minimo = EXCLUDED.minimo,
                    maximo = EXCLUDED.maximo,
                    acumulado = EXCLUDED.acumulado
            """, (id_asignado, sensor_id, hora_inicio, hora_fin))
            cur.execute("RELEASE SAVEPOINT sp_rh")
            insertados += 1
        except Exception as e:
            cur.execute("ROLLBACK TO SAVEPOINT sp_rh")
            errores += 1
            if errores <= 5:
                print(f"  [WARN] {dcp_id}/{sensor_id} {hora}: {e}")

    conn.commit()
    print(f"[HORARIO] {insertados} resúmenes insertados, {errores} errores")
    return insertados


def recalcular_diario(conn, hours, station_filter=None, dry_run=False):
    cur = conn.cursor()

    where_station = f"AND d.dcp_id = '{station_filter}'" if station_filter else ""

    cur.execute(f"""
        SELECT d.dcp_id, d.sensor_id, d.id_asignado, d.variable, d.tipo,
               DATE(d.ts - INTERVAL '1 minute') as fecha
        FROM dcp_datos d
        WHERE d.ts > NOW() - INTERVAL '{hours} hours'
          AND d.valido = true
          {where_station}
        GROUP BY d.dcp_id, d.sensor_id, d.id_asignado, d.variable, d.tipo,
                 DATE(d.ts - INTERVAL '1 minute')
        EXCEPT
        SELECT r.dcp_id, r.sensor_id, r.id_asignado, r.variable, r.tipo, r.fecha
        FROM resumen_diario r
        WHERE r.fecha >= (CURRENT_DATE - {hours // 24 + 1})
        ORDER BY 6, 1, 2
    """)
    faltantes = cur.fetchall()

    if not faltantes:
        print("[OK] No hay resúmenes diarios faltantes.")
        return 0

    print(f"[DIARIO] {len(faltantes)} resúmenes diarios faltantes encontrados")

    if dry_run:
        fechas = {}
        for r in faltantes:
            f = str(r[5])
            fechas[f] = fechas.get(f, 0) + 1
        for f in sorted(fechas.keys()):
            print(f"  {f}: {fechas[f]} sensores sin resumen")
        return len(faltantes)

    insertados = 0
    errores = 0
    for dcp_id, sensor_id, id_asignado, variable, tipo, fecha in faltantes:
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
                  AND DATE(ts - INTERVAL '1 minute') = %s
                  AND valido = true
                GROUP BY DATE(ts - INTERVAL '1 minute'),
                         dcp_id, sensor_id, id_asignado, variable, tipo
                ON CONFLICT (fecha, dcp_id, sensor_id) DO UPDATE SET
                    suma = EXCLUDED.suma,
                    conteo = EXCLUDED.conteo,
                    promedio = EXCLUDED.promedio,
                    minimo = EXCLUDED.minimo,
                    maximo = EXCLUDED.maximo,
                    acumulado = EXCLUDED.acumulado
            """, (id_asignado, sensor_id, fecha))
            cur.execute("RELEASE SAVEPOINT sp_rd")
            insertados += 1
        except Exception as e:
            cur.execute("ROLLBACK TO SAVEPOINT sp_rd")
            errores += 1
            if errores <= 5:
                print(f"  [WARN] {dcp_id}/{sensor_id} {fecha}: {e}")

    conn.commit()
    print(f"[DIARIO] {insertados} resúmenes insertados, {errores} errores")
    return insertados


def main():
    parser = argparse.ArgumentParser(description="Recalcular resúmenes horarios/diarios faltantes")
    parser.add_argument("--hours", "-H", type=int, default=24, help="Horas hacia atrás (default: 24)")
    parser.add_argument("--station", "-s", type=str, default=None, help="DCP ID específico")
    parser.add_argument("--dry-run", action="store_true", help="Solo mostrar qué falta")
    args = parser.parse_args()

    conn = get_conn()

    print("=" * 60)
    print("  RECÁLCULO DE RESÚMENES FALTANTES")
    print("=" * 60)
    print(f"  Rango: últimas {args.hours} horas")
    if args.station:
        print(f"  Estación: {args.station}")
    if args.dry_run:
        print(f"  Modo: DRY-RUN (solo diagnóstico)")
    print()

    h = recalcular_horario(conn, args.hours, args.station, args.dry_run)
    print()
    d = recalcular_diario(conn, args.hours, args.station, args.dry_run)

    print(f"\n{'='*60}")
    if args.dry_run:
        print(f"  Total faltantes: {h} horarios + {d} diarios")
    else:
        print(f"  Recálculo completado")
    print("=" * 60)

    conn.close()


if __name__ == "__main__":
    main()
