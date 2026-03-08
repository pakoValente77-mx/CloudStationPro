import psycopg2
from datetime import datetime, timedelta

try:
    conn_pg = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='Cfe123pass'
    )
    
    cur = conn_pg.cursor()
    
    dcp_id = 'E890C3FC'
    
    print(f"=== Análisis de precipitación para DCP: {dcp_id} ===\n")
    
    # Datos crudos de las últimas 24 horas
    cur.execute("""
        SELECT ts, valor
        FROM dcp_datos
        WHERE dcp_id = %s
        AND variable = 'precipitación'
        AND ts >= now() - interval '24 hours'
        ORDER BY ts DESC
        LIMIT 50
    """, (dcp_id,))
    
    print("--- Datos crudos últimas 24 horas ---")
    data = cur.fetchall()
    if data:
        for row in data:
            print(f"{row[0]}: {row[1]} mm")
    else:
        print("No hay datos en las últimas 24 horas")
    
    # Resumen horario
    cur.execute("""
        SELECT ts, suma, conteo, promedio, minimo, maximo
        FROM resumen_horario
        WHERE dcp_id = %s
        AND variable = 'precipitación'
        AND ts >= now() - interval '24 hours'
        ORDER BY ts DESC
    """, (dcp_id,))
    
    print("\n--- Resumen horario últimas 24 horas ---")
    resumen = cur.fetchall()
    if resumen:
        for row in resumen:
            print(f"{row[0]}: Suma={row[1]}, Count={row[2]}, Prom={row[3]}, Min={row[4]}, Max={row[5]}")
    else:
        print("No hay resumen horario en las últimas 24 horas")
    
    # Valor actual vs acumulado
    cur.execute("""
        SELECT variable, valor, ts
        FROM ultimas_mediciones
        WHERE dcp_id = %s
        AND (variable = 'precipitación' OR variable = 'precipitación_acumulada')
        ORDER BY variable
    """, (dcp_id,))
    
    print("\n--- Valores actuales ---")
    for row in cur.fetchall():
        print(f"{row[0]}: {row[1]} (TS: {row[2]})")
    
    cur.close()
    conn_pg.close()
    
except Exception as e:
    print(f"Error: {e}")
    import traceback
    traceback.print_exc()
