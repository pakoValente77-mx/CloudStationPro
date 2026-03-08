import psycopg2
import sys

try:
    conn = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='Cfe123pass'
    )

    cur = conn.cursor()

    # Buscar la estación Desfogue Malpaso
    cur.execute('''
        SELECT id_asignado, nombre, lat, lon 
        FROM estaciones 
        WHERE nombre ILIKE '%malpaso%'
    ''')

    stations = cur.fetchall()
    print('Estaciones encontradas:')
    for s in stations:
        print(f'  ID: {s[0]}, Nombre: {s[1]}, Lat: {s[2]}, Lon: {s[3]}')

    if stations:
        station_id = stations[0][0]
        print(f'\nÚltimos 20 datos para {station_id}:')
        
        # Ver datos recientes
        cur.execute('''
            SELECT ts, variable, valor 
            FROM datos_estaciones 
            WHERE id_asignado = %s 
            ORDER BY ts DESC 
            LIMIT 20
        ''', (station_id,))
        
        recent = cur.fetchall()
        for r in recent:
            print(f'  {r[0]} | {r[1]}: {r[2]}')
        
        # Ver último valor en ultimos_valores
        print(f'\nÚltimo valor en tabla ultimos_valores:')
        cur.execute('''
            SELECT variable, valor_actual, fecha_actualizacion
            FROM ultimos_valores
            WHERE id_asignado = %s
        ''', (station_id,))
        
        last_vals = cur.fetchall()
        for v in last_vals:
            print(f'  {v[0]}: {v[1]} (actualizado: {v[2]})')

    cur.close()
    conn.close()
    
except Exception as e:
    print(f'Error: {e}')
    sys.exit(1)
