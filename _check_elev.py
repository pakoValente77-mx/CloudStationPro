import psycopg2
c = psycopg2.connect(host='atlas16.ddns.net', port=5432, database='mycloud_timescale', user='postgres', password='***REDACTED-PG-PASSWORD***')
cur = c.cursor()
cur.execute("SELECT column_name, data_type FROM information_schema.columns WHERE table_schema='hydro_model' AND table_name='elevation_capacity' ORDER BY ordinal_position")
for r in cur.fetchall():
    print(r)
print()
cur.execute("SELECT elevation, capacity_mm3 FROM hydro_model.elevation_capacity WHERE dam_name='Angostura' AND elevation BETWEEN 519.9 AND 520.1 LIMIT 5")
for r in cur.fetchall():
    print(r)
c.close()
