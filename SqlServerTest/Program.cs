using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connectionString = "Server=atlas16.ddns.net;Database=IGSCLOUD;User Id=sa;Password=Atlas2025$$;TrustServerCertificate=True;";
        
        try
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                Console.WriteLine("Sample stations in Estacion:");
                string query = "SELECT TOP 20 IdAsignado, Nombre FROM Estacion WHERE Visible = 1 AND Activo = 1";
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"  IdAsignado: {reader.GetValue(0)}, Nombre: {reader.GetValue(1)}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
