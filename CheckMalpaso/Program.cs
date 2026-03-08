using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

var connString = "Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=sa;Password=***REDACTED-SQL-PASSWORD***;TrustServerCertificate=True;";

using (IDbConnection db = new SqlConnection(connString))
{
    var stations = db.Query(@"
        SELECT IdAsignado, Nombre, Latitud, Longitud, Visible, Activo
        FROM Estacion
        WHERE Nombre LIKE '%MALPASO%'
    ");
    
    Console.WriteLine("=== Estaciones MALPASO en SQL Server ===");
    foreach (var s in stations)
    {
        Console.WriteLine($"ID: {s.IdAsignado}, Nombre: {s.Nombre}");
        Console.WriteLine($"  Lat: {s.Latitud}, Lon: {s.Longitud}");
        Console.WriteLine($"  Visible: {s.Visible}, Activo: {s.Activo}");
        Console.WriteLine();
    }
}
