"""
upload_cloudstation.py
======================
Cliente Python para subir archivos FunVasos (FIN Excel) y BHG
al API de CloudStation desde una máquina Windows.

Uso:
  python upload_cloudstation.py funvasos FIN100426.xlsx
  python upload_cloudstation.py funvasos C:\FunVasos\inbox
  python upload_cloudstation.py funvasos FIN100426.xlsx --validate
  python upload_cloudstation.py bhg BHG_abril2026.xlsx
  python upload_cloudstation.py bhg C:\BHG\inbox

  Opciones globales:
    --url    URL base del servidor  (default: http://localhost:5215)
    --key    API key                (default: ***REDACTED-API-KEY***)

Dependencias:
  pip install requests
"""

import argparse
import sys
import os
import glob
from pathlib import Path

try:
    import requests
except ImportError:
    print("[ERROR] Instala la dependencia: pip install requests")
    sys.exit(1)

# ─── Constantes por defecto ───────────────────────────────────────────────────
DEFAULT_URL = "http://localhost:5215"
DEFAULT_API_KEY = "***REDACTED-API-KEY***"
ALLOWED_EXT = {".xlsx", ".xls", ".xlsm"}
TIMEOUT_SECONDS = 60

# ─── Funciones de subida ──────────────────────────────────────────────────────

def upload_funvasos(filepath: Path, base_url: str, api_key: str, validate_only: bool = False) -> bool:
    """Sube un archivo al endpoint /api/funvasos/upload (o /validate)."""
    endpoint = "validate" if validate_only else "upload"
    url = f"{base_url.rstrip('/')}/api/funvasos/{endpoint}"

    print(f"  → {'[VALIDAR]' if validate_only else '[SUBIR]'} {filepath.name}  ...", end=" ", flush=True)
    try:
        with open(filepath, "rb") as f:
            resp = requests.post(
                url,
                headers={"X-Api-Key": api_key},
                files={"file": (filepath.name, f, "application/octet-stream")},
                timeout=TIMEOUT_SECONDS,
            )
        data = _parse_json(resp)
        if resp.status_code == 200 and data.get("success"):
            rows = data.get("rowsInserted", "?")
            fecha = data.get("date", "")
            warn = data.get("warnings", [])
            print(f"OK  — {rows} registros, fecha {fecha}")
            for w in warn:
                print(f"      [AVISO] {w}")
            return True
        else:
            err = data.get("errors") or data.get("error") or resp.text
            print(f"FALLO  — {err}")
            return False
    except requests.exceptions.ConnectionError:
        print(f"FALLO  — No se pudo conectar a {base_url}")
        return False
    except requests.exceptions.Timeout:
        print(f"FALLO  — Tiempo de espera agotado ({TIMEOUT_SECONDS}s)")
        return False
    except Exception as exc:
        print(f"FALLO  — {exc}")
        return False


def upload_bhg(filepath: Path, base_url: str, api_key: str) -> bool:
    """Sube un archivo al endpoint /api/bhg/upload."""
    url = f"{base_url.rstrip('/')}/api/bhg/upload"

    print(f"  → [SUBIR] {filepath.name}  ...", end=" ", flush=True)
    try:
        with open(filepath, "rb") as f:
            resp = requests.post(
                url,
                headers={"X-Api-Key": api_key},
                files={"file": (filepath.name, f, "application/octet-stream")},
                timeout=TIMEOUT_SECONDS,
            )
        data = _parse_json(resp)
        if resp.status_code == 200 and data.get("success"):
            msg = data.get("message", "Procesado")
            print(f"OK  — {msg}")
            return True
        else:
            err = data.get("error") or resp.text
            print(f"FALLO  — {err}")
            return False
    except requests.exceptions.ConnectionError:
        print(f"FALLO  — No se pudo conectar a {base_url}")
        return False
    except requests.exceptions.Timeout:
        print(f"FALLO  — Tiempo de espera agotado ({TIMEOUT_SECONDS}s)")
        return False
    except Exception as exc:
        print(f"FALLO  — {exc}")
        return False


# ─── Helpers ──────────────────────────────────────────────────────────────────

def _parse_json(resp: "requests.Response") -> dict:
    try:
        return resp.json()
    except Exception:
        return {}


def collect_files(target: str) -> list[Path]:
    """Devuelve lista de archivos Excel a procesar (archivo individual o carpeta)."""
    p = Path(target)
    if p.is_dir():
        files = []
        for ext in ALLOWED_EXT:
            files.extend(p.glob(f"*{ext}"))
        files.sort()
        return files
    elif p.is_file():
        if p.suffix.lower() not in ALLOWED_EXT:
            print(f"[ERROR] Extensión '{p.suffix}' no soportada. Use .xlsx, .xls o .xlsm")
            sys.exit(1)
        return [p]
    else:
        print(f"[ERROR] No existe el archivo o carpeta: {target}")
        sys.exit(1)


# ─── CLI ──────────────────────────────────────────────────────────────────────

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="upload_cloudstation",
        description="Sube archivos FunVasos o BHG al API de CloudStation",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Ejemplos:
  python upload_cloudstation.py funvasos FIN100426.xlsx
  python upload_cloudstation.py funvasos C:\\FunVasos\\inbox
  python upload_cloudstation.py funvasos FIN100426.xlsx --validate
  python upload_cloudstation.py bhg BHG_abril2026.xlsx
  python upload_cloudstation.py bhg C:\\BHG\\inbox --url http://mi-servidor.com --key mi-clave
        """,
    )
    parser.add_argument(
        "--url",
        default=DEFAULT_URL,
        metavar="URL",
        help=f"URL base del servidor (default: {DEFAULT_URL})",
    )
    parser.add_argument(
        "--key",
        default=DEFAULT_API_KEY,
        metavar="API_KEY",
        help="API key para autenticación",
    )

    sub = parser.add_subparsers(dest="tipo", required=True)

    # --- SubComando: funvasos ---
    p_fv = sub.add_parser("funvasos", help="Sube archivos FIN Excel (Funcionamiento de Vasos)")
    p_fv.add_argument("archivo", help="Archivo .xlsx o carpeta con archivos")
    p_fv.add_argument(
        "--validate", "-v",
        action="store_true",
        help="Solo valida el archivo sin insertarlo en la BD",
    )

    # --- SubComando: bhg ---
    p_bhg = sub.add_parser("bhg", help="Sube archivos BHG Excel (Boletín Hidrológico Grijalva)")
    p_bhg.add_argument("archivo", help="Archivo .xlsx o carpeta con archivos")

    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    base_url: str = args.url
    api_key: str = args.key
    tipo: str = args.tipo

    files = collect_files(args.archivo)
    if not files:
        print(f"[AVISO] No se encontraron archivos Excel en: {args.archivo}")
        sys.exit(0)

    total = len(files)
    ok = 0
    fail = 0

    print(f"\nCloudStation Upload  —  {tipo.upper()}")
    print(f"Servidor : {base_url}")
    print(f"Archivos : {total}")
    print("─" * 60)

    for filepath in files:
        if tipo == "funvasos":
            success = upload_funvasos(filepath, base_url, api_key, validate_only=args.validate)
        else:  # bhg
            success = upload_bhg(filepath, base_url, api_key)

        if success:
            ok += 1
        else:
            fail += 1

    print("─" * 60)
    print(f"Resultado: {ok} OK  /  {fail} fallidos  de {total} archivo(s)")
    if fail > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
