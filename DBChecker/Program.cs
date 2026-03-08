using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string cs = "Server=localhost;Database=CloudStationDB;User Id=sa;Password=MyP@ssword123;TrustServerCertificate=True;";
        using (var c = new SqlConnection(cs))
        {
            c.Open();
            var cmd = new SqlCommand("SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Grupo%'", c);
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    Console.WriteLine(r["TABLE_NAME"]);
                }
            }
            Console.WriteLine("\nGruposUsuario Columns:");
            using (var r2 = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'GruposUsuario'", c).ExecuteReader())
            {
                while (r2.Read()) { Console.WriteLine($"{r2[0]} - {r2[1]}"); }
            }
            Console.WriteLine("\nEstacionGrupoUsuario Columns:");
            using (var r3 = new SqlCommand("SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'EstacionGrupoUsuario'", c).ExecuteReader())
            {
                while (r3.Read()) { Console.WriteLine($"{r3[0]} - {r3[1]}"); }
            }
        }
    }
}
