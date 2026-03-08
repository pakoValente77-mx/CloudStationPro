using System;
using System.Data.SqlClient;

namespace TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            string connectionString = "Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=sa;Password=Atlas2025$$;TrustServerCertificate=True;";
            
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    
                    string query = @"
                        SELECT TOP 5 Id, IdSensor, ValorReferencia, Umbral, Operador, Nombre, Activo, Color, Periodo
                        FROM UmbralAlertas;
                    ";
                    
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            Console.WriteLine("Id | IdSensor | Referencia | Umbral | Operador | Nombre | Color | Periodo");
                            Console.WriteLine("-------------------------------------------------------------------------");
                            while (reader.Read())
                            {
                                Console.WriteLine($"{reader["Id"]} | {reader["IdSensor"]} | {reader["ValorReferencia"]} | {reader["Umbral"]} | {reader["Operador"]} | {reader["Nombre"]} | {reader["Color"]} | {reader["Periodo"]}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }
    }
}
