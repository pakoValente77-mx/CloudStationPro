#!/usr/bin/env python3
"""
recover_goes.py — Script independiente de recuperación de datos GOES vía LRGS/DDS.

Permite configurar servidores LRGS, credenciales, horas a recuperar y
estaciones específicas sin depender del scheduler principal.

Ejemplos:
  # Recuperar últimas 6 horas de todas las estaciones (usa config.ini)
  python recover_goes.py --hours 6

  # Recuperar solo una estación
  python recover_goes.py --hours 4 --station E891DCA2

  # Usar un LRGS específico
  python recover_goes.py --hours 8 --host lrgseddn1.cr.usgs.gov

  # Usar múltiples LRGS
  python recover_goes.py --hours 8 --host lrgseddn1.cr.usgs.gov --host lrgseddn2.cr.usgs.gov

  # Credenciales personalizadas
  python recover_goes.py --hours 4 --host 10.41.75.100 --user myuser --password mypass

  # Listar estaciones disponibles
  python recover_goes.py --list

  # Solo mostrar lo que haría, sin insertar
  python recover_goes.py --hours 4 --dry-run
"""

import argparse
import sys
import os
import datetime

# ── Ajustar path para importar desde mycloud_all_timescale ──────────────
_script_dir = os.path.dirname(os.path.abspath(__file__))
if _script_dir not in sys.path:
    sys.path.insert(0, _script_dir)

# ── Importar todo desde el módulo principal ─────────────────────────────
# Esto carga config.ini, crea pools PG, carga ESTACIONES, SENSORES, etc.
import mycloud_all_timescale as mcloud


def parse_args():
    parser = argparse.ArgumentParser(
        description="Recuperación de datos GOES vía LRGS/DDS",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Ejemplos:
  python recover_goes.py --hours 6
  python recover_goes.py --hours 4 --station E891DCA2
  python recover_goes.py --hours 8 --host lrgseddn1.cr.usgs.gov
  python recover_goes.py --hours 4 --host 10.41.75.100 --user mi_user --password mi_pass
  python recover_goes.py --list
        """,
    )

    parser.add_argument(
        "--hours", "-H",
        type=int,
        default=4,
        help="Horas hacia atrás a recuperar (default: 4)",
    )
    parser.add_argument(
        "--station", "-s",
        type=str,
        default=None,
        help="DCP ID de estación específica (ej: E891DCA2). Si no se indica, recupera todas.",
    )
    parser.add_argument(
        "--host",
        type=str,
        action="append",
        default=None,
        help="Host(s) LRGS a usar (se puede repetir). Si no se indica, usa los de config.ini.",
    )
    parser.add_argument(
        "--user", "-u",
        type=str,
        default=None,
        help="Usuario LRGS (aplica a todos los --host indicados).",
    )
    parser.add_argument(
        "--password", "-p",
        type=str,
        default=None,
        help="Contraseña LRGS (aplica a todos los --host indicados).",
    )
    parser.add_argument(
        "--list", "-l",
        action="store_true",
        help="Listar estaciones disponibles y salir.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Solo mostrar qué se haría, sin conectar ni insertar datos.",
    )
    parser.add_argument(
        "--retransmitted",
        type=str,
        choices=["Y", "N"],
        default="Y",
        help="Incluir retransmitidos (default: Y para recovery).",
    )

    return parser.parse_args()


def list_stations():
    """Imprime tabla de estaciones disponibles."""
    if not mcloud.ESTACIONES:
        print("[ERROR] No se cargaron estaciones desde SQL Server.")
        return

    print(f"\n{'='*80}")
    print(f"  ESTACIONES DISPONIBLES: {len(mcloud.ESTACIONES)}")
    print(f"{'='*80}")
    print(f"  {'DCP ID':<12} {'IdAsignado':<16} {'Minuto':<8} {'TX(min)':<8} {'Nombre'}")
    print(f"  {'-'*12} {'-'*16} {'-'*8} {'-'*8} {'-'*30}")

    for dcp_id in sorted(mcloud.ESTACIONES.keys()):
        data = mcloud.ESTACIONES[dcp_id]
        print(
            f"  {dcp_id:<12} {data.get('id_asignado',''):<16} "
            f"{data.get('minuto','?'):<8} {data.get('rango_transmision',60):<8} "
            f"{data.get('nombre','')}"
        )
    print()


def show_lrgs_config(hosts, user, password):
    """Muestra configuración LRGS que se usará."""
    print(f"\n  Servidores LRGS:")
    for i, h in enumerate(hosts, 1):
        u, p = user, password
        if not u:
            u, p = mcloud.get_credentials(h)
        print(f"    [{i}] {h}  (user: {u})")
    print()


def recover(args):
    """Ejecuta la recuperación de datos."""
    # ── Determinar hosts LRGS ───────────────────────────────────────
    if args.host:
        hosts = args.host
    else:
        hosts = list(mcloud.HOSTS)

    # ── Credenciales override ───────────────────────────────────────
    # Si se pasan --user/--password, se overridean para TODOS los hosts
    if args.user and args.password:
        for h in hosts:
            mcloud.CREDENTIALS[h] = {"user": args.user, "password": args.password}
    elif args.user or args.password:
        print("[ERROR] Debes indicar tanto --user como --password juntos.")
        sys.exit(1)

    # ── Override hosts en el módulo principal ────────────────────────
    original_hosts = mcloud.HOSTS
    mcloud.HOSTS = hosts

    since = f"now - {args.hours} hours"

    # ── Filtrar estaciones ──────────────────────────────────────────
    if args.station:
        dcp_id = args.station.upper()
        if dcp_id not in mcloud.ESTACIONES:
            print(f"[ERROR] Estación {dcp_id} no encontrada en la BD.")
            print(f"  Usa --list para ver las estaciones disponibles.")
            sys.exit(1)
        station_list = [dcp_id]
    else:
        station_list = list(mcloud.ESTACIONES.keys())

    # ── Resumen ─────────────────────────────────────────────────────
    print(f"\n{'='*70}")
    print(f"  RECUPERACIÓN DE DATOS GOES")
    print(f"{'='*70}")
    print(f"  Horas a recuperar : {args.hours}")
    print(f"  Criterio LRGS     : DRS_SINCE: {since}")
    print(f"  Retransmitidos    : {args.retransmitted}")
    print(f"  Estaciones        : {len(station_list)}")
    show_lrgs_config(hosts, args.user, args.password)

    if args.dry_run:
        print("[DRY-RUN] Estaciones que se procesarían:")
        for i, dcp_id in enumerate(station_list, 1):
            data = mcloud.ESTACIONES[dcp_id]
            print(f"  [{i}/{len(station_list)}] {dcp_id} | {data.get('nombre','')}")
        print("\n[DRY-RUN] No se conectó a ningún LRGS ni se insertaron datos.")
        mcloud.HOSTS = original_hosts
        return

    # ── Ejecutar recuperación ───────────────────────────────────────
    t0 = datetime.datetime.now()
    ok_count = 0
    fail_count = 0
    total_msgs = 0

    for i, dcp_id in enumerate(station_list, 1):
        data = mcloud.ESTACIONES[dcp_id]
        print(f"\n[RECOVER] [{i}/{len(station_list)}] {dcp_id} | {data.get('nombre','')}")

        # Reset contadores del módulo antes de cada fetch
        prev_ok = mcloud._fetch_ok_count
        prev_fail = mcloud._fetch_fail_count

        mcloud.fetch_messages_for_dcp(dcp_id, since=since, multi=True)

        if mcloud._fetch_ok_count > prev_ok:
            ok_count += 1
        else:
            fail_count += 1

    elapsed = datetime.datetime.now() - t0

    # ── Resumen final ───────────────────────────────────────────────
    print(f"\n{'='*70}")
    print(f"  RECUPERACIÓN FINALIZADA")
    print(f"{'='*70}")
    print(f"  Estaciones con datos : {ok_count}")
    print(f"  Estaciones sin datos : {fail_count}")
    print(f"  Tiempo total         : {elapsed}")
    print(f"{'='*70}\n")

    # Restaurar hosts originales
    mcloud.HOSTS = original_hosts


def main():
    args = parse_args()

    if args.list:
        list_stations()
        sys.exit(0)

    recover(args)


if __name__ == "__main__":
    main()
