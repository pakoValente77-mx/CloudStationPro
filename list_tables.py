import psycopg2

try:
    conn = psycopg2.connect(
        host='atlas16.ddns.net',
        database='mycloud_timescale',
        user='postgres',
        password='***REDACTED-PG-PASSWORD***'
    )

    cur = conn.cursor()

    # Listar tablas
    cur.execute('''
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_schema = 'public'
        ORDER BY table_name
    ''')
    
    tables = cur.fetchall()
    print('Tablas en la base de datos:')
    for t in tables:
        print(f'  - {t[0]}')

    cur.close()
    conn.close()
    
except Exception as e:
    print(f'Error: {e}')
