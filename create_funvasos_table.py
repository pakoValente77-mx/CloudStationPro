import psycopg2

conn = psycopg2.connect(host='atlas16.ddns.net', dbname='mycloud_timescale', user='postgres', password='***REDACTED-PG-PASSWORD***')
conn.autocommit = True
cur = conn.cursor()

cur.execute('''
CREATE TABLE IF NOT EXISTS public.funvasos_horario (
    ts TIMESTAMP WITH TIME ZONE NOT NULL,
    presa CHARACTER VARYING(50) NOT NULL,
    hora SMALLINT NOT NULL,
    elevacion REAL,
    almacenamiento REAL,
    diferencia REAL,
    aportaciones_q REAL,
    aportaciones_v REAL,
    extracciones_turb_q REAL,
    extracciones_turb_v REAL,
    extracciones_vert_q REAL,
    extracciones_vert_v REAL,
    extracciones_total_q REAL,
    extracciones_total_v REAL,
    generacion REAL,
    num_unidades SMALLINT,
    aportacion_cuenca_propia REAL,
    aportacion_promedio REAL,
    CONSTRAINT funvasos_horario_pkey PRIMARY KEY (ts, presa, hora)
);
''')
print('Table created successfully')

try:
    cur.execute("SELECT create_hypertable('public.funvasos_horario', 'ts', if_not_exists => TRUE);")
    print('Hypertable created')
except Exception as e:
    print(f'Hypertable note: {e}')

cur.execute('''
    CREATE INDEX IF NOT EXISTS idx_funvasos_presa_ts ON public.funvasos_horario (presa, ts DESC);
''')
print('Index created')

cur.close()
conn.close()
print('Done!')
