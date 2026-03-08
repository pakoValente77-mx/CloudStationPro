#r "nuget: Default.Npgsql, 1.0.0"
#r "nuget: Npgsql, 8.0.2"
#r "nuget: Dapper, 2.1.28"
using System;
using System.Data;
using Npgsql;
using Dapper;

var connStr = "Host=atlas16.ddns.net;Username=postgres;Password=***REDACTED-PG-PASSWORD***;Database=mycloud_timescale";
using (var db = new NpgsqlConnection(connStr)) {
    db.Open();
    var rows = db.Query("SELECT dcp_id, id_asignado, ts, suma, valor, maximo FROM public.resumen_horario WHERE dcp_id = 'C4B2E0CE' OR id_asignado LIKE '%Desfogue Malpaso%' ORDER BY ts DESC LIMIT 10");
    foreach (var r in rows) {
        Console.WriteLine($"{r.dcp_id} - {r.id_asignado} - {r.ts} - {r.suma}");
    }
}
