import psycopg2
c = psycopg2.connect(host='atlas16.ddns.net', port=5432, database='mycloud_timescale', user='postgres', password='***REDACTED-PG-PASSWORD***')
cur = c.cursor()
cur.execute("SELECT column_name FROM information_schema.columns WHERE table_schema='hydro_model' AND table_name='dam_params' ORDER BY ordinal_position")
for r in cur.fetchall():
    print(r[0])
c.close()
