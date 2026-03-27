# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec para mycloud_all_timescale.py

a = Analysis(
    ['mycloud_all_timescale.py'],
    pathex=[],
    binaries=[],
    datas=[],              # config.ini va EXTERNO, no empaquetado
    hiddenimports=[
        'psycopg2',
        'psycopg2.pool',
        'psycopg2.extras',
        'psycopg2._psycopg',
        'pyodbc',
        'schedule',
        'openpyxl',
        'openpyxl.workbook',
        'openpyxl.reader.excel',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        'tkinter', '_tkinter', 'unittest', 'email', 'html',
        'http', 'xml', 'pydoc', 'doctest', 'argparse',
        'PIL', 'numpy', 'pandas', 'matplotlib',
    ],
    noarchive=False,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='MyCloudTimescale',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,           # Ventana de consola visible para ver logs
    disable_windowed_traceback=False,
    argv_emulation=False,
    icon=None,
    onefile=True,            # Ejecutable único
)
