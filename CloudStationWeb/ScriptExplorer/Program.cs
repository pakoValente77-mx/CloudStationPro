using System;
using System.Data;
using Npgsql;
using Dapper;
using System.Linq;
using System.Threading.Tasks;

class ScriptExplorerProgram
{
    static async Task Main(string[] args)
    {
        string connStr = "Host=localhost;Database=tsdb;Username=postgres;Password=admin;"; // default o tratar con DataService.cs si no sirve
        
        try{
            using (var conn = new NpgsqlConnection(connStr))
            {
                var columns = await conn.QueryAsync("SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'resumen_horario'");
                Console.WriteLine("Columns in resumen_horario:");
                foreach(var c in columns) {
                    Console.WriteLine($"{c.column_name}: {c.data_type}");
                }

                Console.WriteLine("\nColumns in dcp_datos:");
                var columns2 = await conn.QueryAsync("SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'dcp_datos'");
                foreach(var c in columns2) {
                    Console.WriteLine($"{c.column_name}: {c.data_type}");
                }
            }
        }catch(Exception ex){
            Console.WriteLine(ex.Message);
        }
    }
}
