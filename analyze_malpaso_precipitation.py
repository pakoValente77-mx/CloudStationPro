import psycopg2

try:
    conn_pg = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='***REDACTED-PG-PASSWORD***'
    )
    
    cur = conn_pg.cursor()
    
    # Buscar con el nombre exacto
    cur.execute("""
        SELECT DISTINCT id_asignado, dcp_id
        FROM resumen_horario
        WHERE UPPER(id_asignado) LIKE '%DESFOGUE%MALPASO%'
           OR UPPER(id_asignado) LIKE '%MALPASO%'
        LIMIT 10
    """)
    
    print("=== Estaciones encontradas ===")
    mappings = cur.fetchall()
    for row in mappings:
        print(f"ID Asignado: '{row[0]}', DCP ID: '{row[1]}'")
    
    if mappings:
        dcp_id = mappings[0][1]
        id_asignado = mappings[0][0]
        
        print(f"\n=== Analizando estación: {id_asignado} (DCP: {dcp_id}) ===")
        
        # Ver últimas mediciones de precipitación
        cur.execute("""
            SELECT variable, valor, ts
            FROM ultimas_mediciones
            WHERE dcp_id = %s
            ORDER BY variable
        """, (dcp_id,))
        
        print("\n--- Últimas mediciones ---")
        for row in cur.fetchall():
            print(f"Variable: {row[0]}, Valor: {row[1]}, TS: {row[2]}")
        
        # Ver acumulado horario de precipitación (última hora)
        cur.execute("""
            SELECT ts, suma, contador
            FROM resumen_horario
            WHERE dcp_id = %s
            AND variable = 'precipitación'
            AND ts >= now() - interval '1 hour'
            ORDER BY ts DESC
        """, (dcp_id,))
        
        print("\n--- Acumulado última hora (precipitación) ---")
        total = 0
        for row in cur.fetchall():
            print(f"TS: {row[0]}, Suma: {row[1]}, Contador: {row[2]}")
            total += row[1] if row[1] else 0
        print(f"TOTAL última hora: {total}")
        
        # Ver datos crudos recientes
        cur.execute("""
            SELECT ts, variable, valor
            FROM dcp_datos
            WHERE dcp_id = %s
            AND variable = 'precipitación'
            ORDER BY ts DESC
            LIMIT 20
        """, (dcp_id,))
        
        print("\n--- Datos crudos recientes (precipitación) ---")
        for row in cur.fetchall():
            print(f"TS: {row[0]}, Variable: {row[1]}, Valor: {row[2]}")
    else:
        print("\nNo se encontró la estación. Buscando todas las estaciones disponibles...")
        cur.execute("""
            SELECT DISTINCT id_asignado
            FROM resumen_horario
            WHERE id_asignado IS NOT NULL
            ORDER BY id_asignado
            LIMIT 50
        """)
        
        print("\n=== Primeras 50 estaciones en la BD ===")
        for row in cur.fetchall():
            print(f"  - {row[0]}")
    
    cur.close()
    conn_pg.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
