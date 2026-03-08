import psycopg2

try:
    conn_pg = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='***REDACTED-PG-PASSWORD***'
    )
    
    cur = conn_pg.cursor()
    
    station_id = '00000CFE06'
    
    # Buscar el mapeo
    cur.execute("""
        SELECT DISTINCT dcp_id, id_asignado
        FROM resumen_horario
        WHERE id_asignado = %s
        LIMIT 1
    """, (station_id,))
    
    mapping = cur.fetchone()
    
    if mapping:
        dcp_id = mapping[0]
        print(f"=== Estación: {station_id} -> DCP: {dcp_id} ===\n")
        
        # Últimas mediciones
        cur.execute("""
            SELECT variable, valor, ts
            FROM ultimas_mediciones
            WHERE dcp_id = %s
            ORDER BY variable
        """, (dcp_id,))
        
        print("--- Últimas mediciones ---")
        for row in cur.fetchall():
            print(f"{row[0]}: {row[1]} (TS: {row[2]})")
        
        # Acumulado horario última hora
        cur.execute("""
            SELECT SUM(suma) as total
            FROM resumen_horario
            WHERE dcp_id = %s
            AND variable = 'precipitación'
            AND ts >= now() - interval '1 hour'
        """, (dcp_id,))
        
        total_hora = cur.fetchone()[0]
        print(f"\n--- Acumulado última hora (precipitación) ---")
        print(f"Total: {total_hora}")
        
        # Detalles del acumulado
        cur.execute("""
            SELECT ts, suma, contador, promedio
            FROM resumen_horario
            WHERE dcp_id = %s
            AND variable = 'precipitación'
            AND ts >= now() - interval '2 hours'
            ORDER BY ts DESC
        """, (dcp_id,))
        
        print(f"\n--- Detalles acumulado (últimas 2 horas) ---")
        for row in cur.fetchall():
            print(f"TS: {row[0]}, Suma: {row[1]}, Count: {row[2]}, Prom: {row[3]}")
        
        # Datos crudos recientes
        cur.execute("""
            SELECT ts, valor
            FROM dcp_datos
            WHERE dcp_id = %s
            AND variable = 'precipitación'
            ORDER BY ts DESC
            LIMIT 30
        """, (dcp_id,))
        
        print(f"\n--- Datos crudos recientes (precipitación) ---")
        for row in cur.fetchall():
            print(f"TS: {row[0]}, Valor: {row[1]}")
    else:
        print(f"No se encontró mapeo para {station_id}")
    
    cur.close()
    conn_pg.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
