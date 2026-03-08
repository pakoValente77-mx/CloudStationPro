import psycopg2

try:
    conn_pg = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='***REDACTED-PG-PASSWORD***'
    )
    
    cur = conn_pg.cursor()
    
    # Buscar todas las estaciones con "DESF" o "MALP"
    cur.execute("""
        SELECT DISTINCT id_asignado, dcp_id
        FROM resumen_horario
        WHERE id_asignado LIKE '%DESF%' OR id_asignado LIKE '%MALP%'
        LIMIT 10
    """)
    
    print("=== Estaciones con DESF o MALP ===")
    mappings = cur.fetchall()
    for row in mappings:
        print(f"ID Asignado: {row[0]}, DCP ID: {row[1]}")
    
    if mappings:
        # Ver últimas mediciones de estas estaciones
        dcp_ids = [row[1] for row in mappings]
        placeholders = ','.join(['%s'] * len(dcp_ids))
        
        cur.execute(f"""
            SELECT dcp_id, variable, valor, ts
            FROM ultimas_mediciones
            WHERE dcp_id IN ({placeholders})
            ORDER BY dcp_id, variable
        """, dcp_ids)
        
        print("\n=== Últimas mediciones ===")
        for row in cur.fetchall():
            print(f"DCP: {row[0]}, Variable: {row[1]}, Valor: {row[2]}, TS: {row[3]}")
        
        # Ver acumulado horario de precipitación
        cur.execute(f"""
            SELECT dcp_id, SUM(suma) as total, MAX(ts) as ultima_hora
            FROM resumen_horario
            WHERE dcp_id IN ({placeholders})
            AND variable = 'precipitación'
            AND ts >= now() - interval '1 hour'
            GROUP BY dcp_id
        """, dcp_ids)
        
        print("\n=== Acumulado última hora (precipitación) ===")
        for row in cur.fetchall():
            print(f"DCP: {row[0]}, Total: {row[1]}, Última hora: {row[2]}")
    
    cur.close()
    conn_pg.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
