import psycopg2
c = psycopg2.connect(host='atlas16.ddns.net', port=5432, database='mycloud_timescale', user='postgres', password='***REDACTED-PG-PASSWORD***')
cur = c.cursor()
cur.execute("SELECT column_name FROM information_schema.columns WHERE table_schema='rain_forecast' AND table_name='forecast'")
for r in cur.fetchall():
    print(r[0])
print("---")
cur.execute("SELECT * FROM rain_forecast.forecast ORDER BY forecast_date DESC LIMIT 3")
cols = [d[0] for d in cur.description]
print(cols)
for r in cur.fetchall():
    print(r)
c.close()
